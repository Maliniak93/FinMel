export const meta = {
  name: 'build-feature',
  description: 'Spec to a staged tree: branch, failing tests, implementation, verification, adversarial review',
  whenToUse: 'Invoked by /build on a spec whose status is approved. Not for exploratory work - the spec is the contract.',
  phases: [
    { title: 'Branch', detail: 'ops cuts feat/<slug> from master before a single file is written', model: 'haiku' },
    { title: 'Tests', detail: 'test-writer turns every acceptance criterion into a failing test; sonnet/high on tier 1, opus/high on tier 2 (skippable)' },
    { title: 'Implement', detail: 'implementer does the work; sonnet/high on tier 1, opus/xhigh on tier 2' },
    { title: 'Verify', detail: 'verifier runs scripts/verify.mjs; failures loop back to Implement', model: 'haiku' },
    { title: 'Review', detail: 'ops stages the tree, reviewer diffs the staged change against the spec (skippable)', model: 'claude-opus-5-5' },
    { title: 'Stage', detail: 'ops flips the spec to done and leaves everything staged - the commit, push and PR are yours', model: 'haiku' },
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

// ---------------------------------------------------------------- setup

const { spec, tier = 1, maxRounds = 2, skip = [] } = args || {}

if (!spec) {
  return { status: 'blocked', stage: 'input', reason: 'args.spec is required - the path to skarbiec-plan/specs/<slug>.md' }
}

const skipped = new Set((Array.isArray(skip) ? skip : [skip]).map((s) => String(s).trim().toLowerCase()))

const OPUS = 'claude-opus-5-5'
// Mechanical ops phases (cut, stage, finish) need no judgment beyond reading frontmatter.
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
const stop = (stage, extra) => Object.assign({ status: 'blocked', stage, spec, tier, tests, impl, rounds }, extra)

// Every phase runs one agent and stops the whole run if that agent dies or refuses.
async function step(agentType, label, lines, schema, opts) {
  const result = await agent(lines.filter(Boolean).join('\n'), Object.assign({ agentType, schema, label }, opts || {}))
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

// Nothing is ever committed by the pipeline: `git add -A` is the whole freeze, and the reviewer
// diffs the index (`git diff --cached`), which - unlike a bare `git diff` - does show new files.
const stage = (label) =>
  step(
    'ops',
    label,
    [
      `Stage the working tree for the spec at \`${spec}\` so the reviewer sees the whole change.`,
      'Read the spec frontmatter for `title` and `branch`. Confirm you are on that branch - never stage work on master.',
      'Run `git add -A` and nothing else. No commit, no push, no PR - the user does all three by hand.',
      'Report the branch and the number of staged files (`git diff --cached --name-only`).',
    ],
    STAGE,
    MECHANICAL,
  )

// ---------------------------------------------------------------- branch

phase('Branch')
log(`Spec ${spec} - tier ${tier}, implementer on ${model}/${effort}, max ${maxRounds} fix rounds${skipped.size ? `, skipping: ${[...skipped].join(', ')}` : ''}`)

if (broke()) return stop('branch', { reason: 'budget' })

const cut = await step(
  'ops',
  'cut feat/<slug> from master',
  [
    `Create the branch for the spec at \`${spec}\`. Create the branch and nothing else - no commit, no push, no PR.`,
    'Read the spec frontmatter for `branch`. `git fetch origin` first; branch from an up-to-date master and say so in notes if local master was behind.',
    'If you are already on that branch, stay on it - this is a resumed run - and report its uncommitted changes in notes.',
    'If you are on master or another branch with changes that are not this spec (the spec file itself belongs to this change), stop: return `blocked: true` and name those files in notes instead of sweeping them along.',
  ],
  BRANCH,
  MECHANICAL,
)

if (!cut) return stop('branch', { reason: 'ops returned no result' })
if (cut.blocked) return stop('branch', { reason: 'the working tree holds changes that are not this spec', notes: cut.notes || [] })

log(`working on ${cut.branch}`)

// ---------------------------------------------------------------- tests

phase('Tests')

if (skipped.has('tests')) {
  log('Tests skipped - this spec proves its acceptance criteria with commands, not new tests')
} else {
  if (broke()) return stop('tests', { reason: 'budget' })

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

  if (!tests) return stop('tests', { reason: 'test-writer returned no result' })

  if (!tests.tests.length) {
    return stop('tests', {
      reason: 'test-writer produced no tests. If this spec genuinely needs none, add `skip: [tests]` to its frontmatter and re-run; otherwise its acceptance criteria are not testable.',
      notes: tests.notes || [],
    })
  }

  log(`${tests.tests.length} failing test(s) across: ${tests.projects.join(', ') || 'no project reported'}`)
}

// ---------------------------------------------------------------- implement

phase('Implement')
if (broke()) return stop('implement', { reason: 'budget' })

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

if (!impl) return stop('implement', { reason: 'implementer returned no result' })
if (impl.status === 'blocked') return stop('implement', { reason: 'implementer blocked' })

log(`implementer touched ${impl.filesTouched.length} file(s)`)

// ---------------------------------------------------------------- verify loop

for (let round = 0; ; round++) {
  phase('Verify')
  if (broke()) return stop('verify', { reason: 'budget' })

  const verified = await verify(`verify round ${round + 1}`)
  if (!verified) return stop('verify', { reason: 'verifier returned no result' })

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
    return stop('verify', { failures: verified.failures })
  }

  phase('Implement')
  if (broke()) return stop('implement', { reason: 'budget' })

  impl = await implement(`fix verify failures (round ${round + 1})`, [
    `Fix exactly these verification failures: ${JSON.stringify(verified.failures)}`,
    `tests: ${JSON.stringify(tests)}`,
    'Do not refactor around them and do not weaken or delete a test.',
  ])

  if (!impl) return stop('implement', { reason: 'implementer returned no result on a fix round' })
  if (impl.status === 'blocked') return stop('implement', { reason: 'implementer blocked on a fix round' })
}

// ---------------------------------------------------------------- review loop

if (skipped.has('review')) {
  phase('Review')
  log('Review skipped by request - staging on a green verify alone')
} else {
  for (let round = 0; ; round++) {
    phase('Review')
    if (broke()) return stop('review', { reason: 'budget' })

    const ready = await stage(`stage the tree for review (round ${round + 1})`)
    if (!ready) return stop('review', { reason: 'ops could not stage the tree for review' })

    review = await step(
      'reviewer',
      `adversarial review (round ${round + 1})`,
      [
        `Review the change for the spec at \`${spec}\`.`,
        `It is staged - not committed - on \`${ready.branch}\`, so \`git diff --cached\` is the diff under review (it shows new files; a bare \`git diff\` does not). Also run \`git status --porcelain\`: anything still unstaged belongs to this change too.`,
        `tests claimed: ${JSON.stringify(tests.tests)}`,
        `implementer report: ${JSON.stringify(impl)}`,
        tests.tests.length ? null : 'This spec declared `skip: [tests]` - no new tests were written. Judge each acceptance criterion by the command it names and confirm the existing suites still cover the behaviour it touches.',
        'blocking only for wrong behaviour, an unproven acceptance criterion, a hard-rule violation or forbidden scope. Everything else is minor.',
      ],
      REVIEW,
    )

    if (!review) return stop('review', { reason: 'reviewer returned no result' })

    const blocking = review.findings.filter((f) => f.severity === 'blocking')
    if (!blocking.length) {
      log(`review clean (${review.findings.length - blocking.length} minor finding(s))`)
      break
    }

    log(`review returned ${blocking.length} blocking finding(s)`)
    if (round >= maxRounds) return stop('review', { findings: blocking, summary: review.summary })

    phase('Implement')
    if (broke()) return stop('implement', { reason: 'budget' })

    impl = await implement(`address blocking findings (round ${round + 1})`, [
      `Address exactly these blocking review findings: ${JSON.stringify(blocking)}`,
      `tests: ${JSON.stringify(tests)}`,
      'A finding you disagree with goes into notes with the reason - do not silently ignore it.',
    ])

    if (!impl) return stop('implement', { reason: 'implementer returned no result on a review fix' })
    if (impl.status === 'blocked') return stop('implement', { reason: 'implementer blocked on a review fix' })

    rounds = rounds + 1

    phase('Verify')
    if (broke()) return stop('verify', { reason: 'budget' })

    const reverified = await verify(`verify after review fix (round ${round + 1})`)
    if (!reverified) return stop('verify', { reason: 'verifier returned no result after a review fix' })
    if (!reverified.ok) {
      log('the review fix broke verification - stopping')
      return stop('verify', { failures: reverified.failures })
    }
    log('verify green again after the review fix')
  }
}

// ---------------------------------------------------------------- stage

phase('Stage')
if (broke()) return stop('stage', { reason: 'budget' })

const minor = review ? review.findings.filter((f) => f.severity === 'minor') : []

const staged = await step(
  'ops',
  'flip the spec to done and leave the tree staged',
  [
    `Finish the run for the spec at \`${spec}\`. Nothing is committed and nothing may be: the user commits, pushes and opens the PR by hand.`,
    'Read the spec frontmatter for `title` and `branch`. Confirm you are on that branch.',
    'Set the spec frontmatter to `status: done` and, under a `## Result` heading at the end of the spec (create it if missing), record that the change is staged on its branch and awaiting the user\'s own commit and PR.',
    'Then run `git add -A`. No commit, no push, no `gh pr create`, no PR review comments - there is no PR yet.',
    'Report the branch and the number of staged files.',
    `implementer report: ${JSON.stringify(impl)}`,
  ],
  STAGE,
  MECHANICAL,
)

if (!staged) return stop('stage', { reason: 'ops returned no result' })

const branch = staged.branch || cut.branch
log(`staged on ${branch} - ${staged.stagedFiles ?? '?'} file(s); commit, push and PR are yours`)

return {
  status: 'staged',
  branch,
  stagedFiles: staged.stagedFiles ?? null,
  nextSteps: [`git commit -m "<spec title>"`, `git push -u origin ${branch}`, `gh pr create --base master --head ${branch}`],
  review: review ? review.summary : 'review skipped',
  minorFindings: minor,
  skipped: [...skipped],
  tests,
  impl,
  rounds,
  spec,
  tier,
}
