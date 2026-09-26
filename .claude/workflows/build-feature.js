export const meta = {
  name: 'build-feature',
  description: 'Spec to an open PR: branch, failing tests, implementation, verification, adversarial review, commit, push, PR',
  whenToUse: 'Invoked by /build and /fix on an open spec issue from the FinMel project (args.spec = its local copy, args.issue, args.branch, args.title). Not for exploratory work - the spec is the contract.',
  phases: [
    { title: 'Branch', detail: 'ops cuts the issue branch (feat/<slug> or fix/<slug>) from master before a single file is written', model: 'haiku' },
    { title: 'Tests', detail: 'test-writer turns every acceptance criterion into a failing test; sonnet/high on tier 1, opus/high on tier 2 (skippable)' },
    { title: 'Implement', detail: 'implementer does the work; sonnet/high on tier 1, opus/xhigh on tier 2' },
    { title: 'Verify', detail: 'verifier runs scripts/verify.mjs; failures loop back to Implement', model: 'haiku' },
    { title: 'Review', detail: 'ops stages the tree, reviewer diffs the staged change against the spec (skippable)', model: 'claude-opus-5-5' },
    { title: 'Ship', detail: 'ops commits, pushes and opens the PR - merging is yours', model: 'haiku' },
  ],
}

// ---------------------------------------------------------------- schemas

const BRANCH = {
  type: 'object',
  properties: {
    branch: { type: 'string' },
    commit: { type: 'string' },
    notes: { type: 'array', items: { type: 'string' } },
    blocked: { type: 'boolean' },
  },
  required: ['branch'],
}

const TESTS = {
  type: 'object',
  properties: {
    tests: {
      type: 'array',
      items: {
        type: 'object',
        properties: { name: { type: 'string' }, file: { type: 'string' }, ac: { type: 'string' } },
        required: ['name', 'file', 'ac'],
      },
    },
    projects: { type: 'array', items: { type: 'string' } },
    notes: { type: 'array', items: { type: 'string' } },
  },
  required: ['tests', 'projects'],
}

const IMPL = {
  type: 'object',
  properties: {
    status: { type: 'string', enum: ['done', 'blocked'] },
    filesTouched: { type: 'array', items: { type: 'string' } },
    projects: { type: 'array', items: { type: 'string' } },
    commandsRun: { type: 'array', items: { type: 'string' } },
    notes: { type: 'array', items: { type: 'string' } },
    openQuestions: { type: 'array', items: { type: 'string' } },
  },
  required: ['status', 'filesTouched', 'projects'],
}

const VERIFY = {
  type: 'object',
  properties: {
    ok: { type: 'boolean' },
    failures: {
      type: 'array',
      items: {
        type: 'object',
        properties: { step: { type: 'string' }, summary: { type: 'string' }, file: { type: 'string' } },
        required: ['step', 'summary'],
      },
    },
  },
  required: ['ok', 'failures'],
}

const REVIEW = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          severity: { type: 'string', enum: ['blocking', 'minor'] },
          file: { type: 'string' },
          line: { type: 'number' },
          claim: { type: 'string' },
          evidence: { type: 'string' },
          suggestedFix: { type: 'string' },
        },
        required: ['severity', 'claim', 'evidence'],
      },
    },
    summary: { type: 'string' },
  },
  required: ['findings', 'summary'],
}

const STAGE = {
  type: 'object',
  properties: {
    branch: { type: 'string' },
    stagedFiles: { type: 'number' },
    notes: { type: 'array', items: { type: 'string' } },
  },
  required: ['branch'],
}

const SHIP = {
  type: 'object',
  properties: {
    branch: { type: 'string' },
    commit: { type: 'string' },
    prUrl: { type: 'string' },
    notes: { type: 'array', items: { type: 'string' } },
  },
  required: ['branch'],
}

// ---------------------------------------------------------------- setup

const { spec, issue, branch: issueBranch, title, tier = 1, maxRounds = 2, skip = [] } = args || {}

if (!(spec && issue && issueBranch && title)) {
  return {
    status: 'blocked',
    stage: 'input',
    reason: 'args.spec (skarbiec-plan/issues/<n>.md), args.issue, args.branch and args.title are all required',
  }
}

// The spec is a gitignored local copy of a GitHub issue: branch and title arrive as args, and nobody
// edits the file - the issue and its project card carry all state.
const whereBranch = `The spec is a local copy of issue #${issue}: the branch is \`${issueBranch}\` and the title is "${title}". Never edit the spec file - it is gitignored.`

const skipped = new Set((Array.isArray(skip) ? skip : [skip]).map((s) => String(s).trim().toLowerCase()))

const OPUS = 'claude-opus-5-5'
// Mechanical ops phases (cut, stage, finish) need no judgment.
const MECHANICAL = { model: 'haiku', effort: 'low' }

let model = tier >= 2 ? OPUS : 'sonnet'
let effort = tier >= 2 ? 'xhigh' : 'high'

let rounds = 0
let escalated = false
let tests = { tests: [], projects: [], notes: [] }
let impl = null
let review = null

const RESERVE = 40000
const broke = () => Boolean(budget.total) && budget.remaining() < RESERVE
// Every phase runs one agent and stops the whole run if that agent dies or refuses.
async function step(agentType, label, lines, schema, opts) {
  const result = await agent(lines.filter(Boolean).join('\n'), Object.assign({ agentType, schema, label }, opts || {}))
  return result
}

// What the caller needs from the agents' reports - names and counts, never the whole JSON.
const compact = () => ({
  tests: tests.tests.map((t) => t.name),
  filesTouched: impl ? impl.filesTouched.length : 0,
  openQuestions: (impl && impl.openQuestions) || [],
})

// The run report is built here, as data, and formatted and posted by `gh-project.mjs report`: no
// model writes the issue comment, the ops agent only pastes one command.
const clip = (text, n) => (text && String(text).length > n ? `${String(text).slice(0, n)}…` : text)
const findingsOf = (list) => (list || []).map((f) => ({ file: f.file, line: f.line, claim: clip(f.claim, 300) }))
// Single-quoted for bash (a `'` inside becomes `'"'"'`), so every command stays one line that
// matches its permission rule and runs without a prompt.
const sq = (text) => `'${String(text).replace(/'/g, `'"'"'`)}'`
const reportCommand = (run) => [
  'Run exactly this command, verbatim, as one Bash call:',
  '```',
  `node scripts/gh-project.mjs report ${issue} --json ${sq(JSON.stringify(run))}`,
  '```',
]

async function stop(stage, extra) {
  const result = Object.assign({ status: 'blocked', stage, issue, tier, rounds }, compact(), extra)
  await step(
    'ops',
    'post the blocked report on the issue',
    [`Post the report of a blocked build run on issue #${issue}. Touch no git state.`].concat(
      reportCommand({
        status: 'blocked',
        stage,
        reason: clip(result.reason, 300),
        failures: (result.failures || []).map((f) => ({ step: f.step, summary: clip(f.summary, 300), file: f.file })),
        blocking: findingsOf(result.findings),
      }),
      ['Report the branch you are on, and the command output in notes.'],
    ),
    STAGE,
    MECHANICAL,
  )
  return result
}

const implement = (label, lines) =>
  step('implementer', label, [`Work on the spec at \`${spec}\`. You are already on its branch.`].concat(lines), IMPL, { model, effort })

const verify = (label) =>
  step(
    'verifier',
    label,
    [
      'Run the verification script and report its VERIFY_RESULT line verbatim.',
      `projects: ${JSON.stringify(impl && impl.projects && impl.projects.length ? impl.projects : tests.projects)}`,
      'Fix nothing. Explain nothing.',
    ],
    VERIFY,
  )

// Nothing is committed before the review: `git add -A` is the freeze, and the reviewer diffs the
// index (`git diff --cached`), which - unlike a bare `git diff` - does show new files. The commit
// comes in the Ship phase, once the review is clean.
const stage = (label) =>
  step(
    'ops',
    label,
    [
      `Stage the working tree for the spec at \`${spec}\` so the reviewer sees the whole change.`,
      whereBranch,
      'Confirm you are on that branch - never stage work on master.',
      'Run `git add -A` and nothing else. No commit, no push, no PR - those come after the review.',
      'Report the branch and the number of staged files (`git diff --cached --name-only`).',
    ],
    STAGE,
    MECHANICAL,
  )

// ---------------------------------------------------------------- branch

phase('Branch')
log(`Spec ${spec} - tier ${tier}, implementer on ${model}/${effort}, max ${maxRounds} fix rounds${skipped.size ? `, skipping: ${[...skipped].join(', ')}` : ''}`)

if (broke()) return await stop('branch', { reason: 'budget' })

const cut = await step(
  'ops',
  'cut the issue branch from master',
  [
    `Create the branch for the spec at \`${spec}\`. Create the branch and nothing else - no commit, no push, no PR.`,
    whereBranch,
    `Cut it as the issue's linked branch - \`git fetch origin\`, then \`gh issue develop ${issue} --name ${issueBranch} --base master --checkout\` (never \`git switch -c\`). A linked branch is what closes the issue when its PR merges, with no \`Closes\` keyword. If \`${issueBranch}\` already exists locally or on origin (an earlier run), \`git switch ${issueBranch}\` instead.`,
    'If you are already on that branch, stay on it - this is a resumed run - and report its uncommitted changes in notes.',
    'If you are on master or another branch with changes that are not this spec (the spec file itself belongs to this change), stop: return `blocked: true` and name those files in notes instead of sweeping them along.',
  ],
  BRANCH,
  MECHANICAL,
)

if (!cut) return await stop('branch', { reason: 'ops returned no result' })
if (cut.blocked) return await stop('branch', { reason: 'the working tree holds changes that are not this spec', notes: cut.notes || [] })

log(`working on ${cut.branch}`)

// ---------------------------------------------------------------- tests

phase('Tests')

if (skipped.has('tests')) {
  log('Tests skipped - this spec proves its acceptance criteria with commands, not new tests')
} else {
  if (broke()) return await stop('tests', { reason: 'budget' })

  tests = await step(
    'test-writer',
    'failing tests from acceptance criteria',
    [
      `Write the failing tests for the spec at \`${spec}\`.`,
      'Read that spec first, then only the skarbiec-plan sections it names, then the target test project Fixtures/.',
      'One or more tests per acceptance criterion, named as the spec names them. Include tenancy isolation for any new user-owned resource and outbox/idempotency tests for any new or changed event.',
      'Run them and confirm they are red for the right reason. Write no production code.',
    ],
    TESTS,
    tier >= 2 ? { model: OPUS, effort: 'high' } : { model: 'sonnet', effort: 'high' },
  )

  if (!tests) return await stop('tests', { reason: 'test-writer returned no result' })

  if (!tests.tests.length) {
    return await stop('tests', {
      reason: 'test-writer produced no tests. If this spec genuinely needs none, add the `skip-tests` label to its issue and re-run; otherwise its acceptance criteria are not testable.',
      notes: tests.notes || [],
    })
  }

  log(`${tests.tests.length} failing test(s) across: ${tests.projects.join(', ') || 'no project reported'}`)
}

// ---------------------------------------------------------------- implement

phase('Implement')
if (broke()) return await stop('implement', { reason: 'budget' })

impl = await implement(
  'implement the spec',
  tests.tests.length
    ? [
        `Make these failing tests pass: ${JSON.stringify(tests)}`,
        'Read the spec and only the skarbiec-plan sections it names. Smallest correct change; do not widen scope beyond the spec.',
      ]
    : [
        'This spec has no new tests - it adds no behaviour. Implement its Scope exactly and leave every existing suite green.',
        'Read the spec and only the skarbiec-plan sections it names. Smallest correct change; do not widen scope beyond the spec.',
        'Run the command each acceptance criterion names as its proof, and report those commands in `commandsRun`.',
      ],
)

if (!impl) return await stop('implement', { reason: 'implementer returned no result' })
if (impl.status === 'blocked') return await stop('implement', { reason: 'implementer blocked' })

log(`implementer touched ${impl.filesTouched.length} file(s)`)

// ---------------------------------------------------------------- verify loop

for (let round = 0; ; round++) {
  phase('Verify')
  if (broke()) return await stop('verify', { reason: 'budget' })

  const verified = await verify(`verify round ${round + 1}`)
  if (!verified) return await stop('verify', { reason: 'verifier returned no result' })

  if (verified.ok) {
    log(`verify green after ${rounds} fix round(s)`)
    break
  }

  rounds = round + 1
  log(`verify failed (${verified.failures.map((f) => f.step).join(', ') || 'unspecified'})`)

  // Tier 1 gets one extra round on opus after escalating; tier 2 gets exactly maxRounds.
  if (round === maxRounds && tier === 1 && !escalated) {
    escalated = true
    model = OPUS
    effort = 'xhigh'
    log('tier 1 exhausted its fix rounds - escalating the implementer to opus/xhigh for one final round')
  }
  if (round >= (escalated ? maxRounds + 1 : maxRounds)) {
    log('verify still red after the final round - stopping')
    return await stop('verify', { failures: verified.failures })
  }

  phase('Implement')
  if (broke()) return await stop('implement', { reason: 'budget' })

  impl = await implement(`fix verify failures (round ${round + 1})`, [
    `Fix exactly these verification failures: ${JSON.stringify(verified.failures)}`,
    `tests: ${JSON.stringify(tests)}`,
    'Do not refactor around them and do not weaken or delete a test.',
  ])

  if (!impl) return await stop('implement', { reason: 'implementer returned no result on a fix round' })
  if (impl.status === 'blocked') return await stop('implement', { reason: 'implementer blocked on a fix round' })
}

// ---------------------------------------------------------------- review loop

if (skipped.has('review')) {
  phase('Review')
  log('Review skipped by request - staging on a green verify alone')
} else {
  for (let round = 0; ; round++) {
    phase('Review')
    if (broke()) return await stop('review', { reason: 'budget' })

    const ready = await stage(`stage the tree for review (round ${round + 1})`)
    if (!ready) return await stop('review', { reason: 'ops could not stage the tree for review' })

    review = await step(
      'reviewer',
      `adversarial review (round ${round + 1})`,
      [
        `Review the change for the spec at \`${spec}\`.`,
        `It is staged - not committed - on \`${ready.branch}\`, so \`git diff --cached\` is the diff under review (it shows new files; a bare \`git diff\` does not). Also run \`git status --porcelain\`: anything still unstaged belongs to this change too.`,
        `tests claimed: ${JSON.stringify(tests.tests)}`,
        `implementer report: ${JSON.stringify(impl)}`,
        tests.tests.length ? null : 'This spec carries the `skip-tests` label - no new tests were written. Judge each acceptance criterion by the command it names and confirm the existing suites still cover the behaviour it touches.',
        'blocking only for wrong behaviour, an unproven acceptance criterion, a hard-rule violation or forbidden scope. Everything else is minor.',
      ],
      REVIEW,
    )

    if (!review) return await stop('review', { reason: 'reviewer returned no result' })

    const blocking = review.findings.filter((f) => f.severity === 'blocking')
    if (!blocking.length) {
      log(`review clean (${review.findings.length - blocking.length} minor finding(s))`)
      break
    }

    log(`review returned ${blocking.length} blocking finding(s)`)
    if (round >= maxRounds) return await stop('review', { findings: blocking, summary: review.summary })

    phase('Implement')
    if (broke()) return await stop('implement', { reason: 'budget' })

    impl = await implement(`address blocking findings (round ${round + 1})`, [
      `Address exactly these blocking review findings: ${JSON.stringify(blocking)}`,
      `tests: ${JSON.stringify(tests)}`,
      'A finding you disagree with goes into notes with the reason - do not silently ignore it.',
    ])

    if (!impl) return await stop('implement', { reason: 'implementer returned no result on a review fix' })
    if (impl.status === 'blocked') return await stop('implement', { reason: 'implementer blocked on a review fix' })

    rounds = rounds + 1

    phase('Verify')
    if (broke()) return await stop('verify', { reason: 'budget' })

    const reverified = await verify(`verify after review fix (round ${round + 1})`)
    if (!reverified) return await stop('verify', { reason: 'verifier returned no result after a review fix' })
    if (!reverified.ok) {
      log('the review fix broke verification - stopping')
      return await stop('verify', { failures: reverified.failures })
    }
    log('verify green again after the review fix')
  }
}

// ---------------------------------------------------------------- ship

phase('Ship')
if (broke()) return await stop('ship', { reason: 'budget' })

const minor = review ? review.findings.filter((f) => f.severity === 'minor') : []

// Every git/gh command is built here and run verbatim, one Bash call each, so the git-guard hook
// sees each one (commit/push/PR on feat/* and fix/* pass silently; anything else prompts).
const commitCommand = `git commit -m ${sq(title)} -m ${sq(`Spec: #${issue}`)} -m ${sq('Co-Authored-By: Claude <noreply@anthropic.com>')}`
const pushCommand = `git push -u origin ${issueBranch}`
const prCommand = `gh pr create --base master --head ${issueBranch} --title ${sq(title)} --body ${sq(`Spec: #${issue} - the build run report is on the issue. 🤖 Generated with [Claude Code](https://claude.com/claude-code)`)}`
const shipCommands = ['git add -A', commitCommand, pushCommand, prCommand]

const shipped = await step(
  'ops',
  'commit, push and open the PR',
  [
    `Ship the verified and reviewed change for the spec at \`${spec}\`: commit it, push the branch and open its PR. Never merge - the user merges.`,
    whereBranch,
    `Confirm you are on \`${issueBranch}\` - never commit on master. Then run these commands in order, each verbatim as its own Bash call:`,
    '```',
    ...shipCommands,
    '```',
    '- `git commit` says there is nothing to commit and the branch is already ahead of `origin/master` (a resumed run committed it) → carry on with the push.',
    '- `gh pr create` says a PR for the branch already exists → take its URL from `gh pr view --json url` and carry on.',
    '- Any other failure, or a permission prompt → stop there: run nothing further (not even the report), return no `prUrl`, and put the failing command and its error first in notes.',
    'Only once the PR exists, post the run report on the issue:',
  ].concat(
    reportCommand({
      status: 'shipped',
      branch: issueBranch,
      tests: tests.tests.map((t) => t.name),
      rounds,
      reviewRan: Boolean(review),
      minor: findingsOf(minor),
    }),
    ['Report the branch, the commit sha (`git rev-parse --short HEAD`), the PR URL as `prUrl`, and the report command output in notes.'],
  ),
  SHIP,
  MECHANICAL,
)

if (!shipped) return await stop('ship', { reason: 'ops returned no result', nextSteps: shipCommands })
if (!shipped.prUrl) {
  return await stop('ship', { reason: clip((shipped.notes || [])[0] || 'ops opened no PR', 300), nextSteps: shipCommands })
}

const branch = shipped.branch || cut.branch
log(`shipped ${branch} - PR ${shipped.prUrl}; merging is yours`)

return {
  status: 'shipped',
  branch,
  commit: shipped.commit || null,
  prUrl: shipped.prUrl,
  review: review ? clip(review.summary, 400) : 'review skipped',
  minorFindings: findingsOf(minor),
  skipped: [...skipped],
  rounds,
  issue,
  tier,
  ...compact(),
}
