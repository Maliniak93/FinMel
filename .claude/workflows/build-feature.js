export const meta = {
  name: 'build-feature',
  description: 'Spec to PR: branch, failing tests, implementation, verification, adversarial review, PR',
  whenToUse: 'Invoked by /build on a spec whose status is approved. Not for exploratory work - the spec is the contract.',
  phases: [
    { title: 'Branch', detail: 'ops cuts feat/<slug> from master before a single file is written', model: 'sonnet' },
    { title: 'Tests', detail: 'test-writer turns every acceptance criterion into a failing test (skippable)', model: 'sonnet' },
    { title: 'Implement', detail: 'implementer does the work; sonnet/high on tier 1, opus/xhigh on tier 2' },
    { title: 'Verify', detail: 'verifier runs scripts/verify.mjs; failures loop back to Implement', model: 'haiku' },
    { title: 'Review', detail: 'ops commits the tree, reviewer diffs it against the spec (skippable)', model: 'opus' },
    { title: 'Ship', detail: 'ops pushes, opens the PR and posts the minor findings on it', model: 'sonnet' },
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

const SHIP = {
  type: 'object',
  properties: {
    branch: { type: 'string' },
    commit: { type: 'string' },
    prUrl: { type: 'string' },
    ciStatus: { type: 'string' },
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

let model = tier >= 2 ? 'opus' : 'sonnet'
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

// One commit per run, amended on every re-freeze: the reviewer always diffs `master...HEAD`.
const freeze = (label) =>
  step(
    'ops',
    label,
    [
      `Freeze the working tree for the spec at \`${spec}\` in one commit on its branch, so the reviewer can diff it.`,
      'Read the spec frontmatter for `title` and `branch`. Confirm you are on that branch - never commit on master.',
      '`git add -A`, then commit with the spec title as the subject. If your own freeze commit from this run is already HEAD, amend it instead of stacking a second commit.',
      'Do not push. Do not open a PR. Report the commit sha.',
    ],
    SHIP,
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
    model = 'opus'
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
  log('Review skipped by request - shipping on a green verify alone')
} else {
  for (let round = 0; ; round++) {
    phase('Review')
    if (broke()) return stop('review', { reason: 'budget' })

    const frozen = await freeze(`commit the tree for review (round ${round + 1})`)
    if (!frozen) return stop('review', { reason: 'ops could not commit the tree for review' })

    review = await step(
      'reviewer',
      `adversarial review (round ${round + 1})`,
      [
        `Review the change for the spec at \`${spec}\`.`,
        `It is committed on \`${frozen.branch}\` - \`git diff master...HEAD\` is the diff under review. Also run \`git status --porcelain\`: anything still uncommitted belongs to this change too.`,
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

// ---------------------------------------------------------------- ship

phase('Ship')
if (broke()) return stop('ship', { reason: 'budget' })

const minor = review ? review.findings.filter((f) => f.severity === 'minor') : []

const ship = await step(
  'ops',
  'push, open the PR, post the minor findings',
  [
    `Ship the work for the spec at \`${spec}\`. It is already committed on its branch.`,
    'Read the spec frontmatter for `title` and `branch`. Check the commit subject matches the spec title and amend it if it does not; commit anything still unstaged into that same commit.',
    'Push with -u, then open the PR to master. Do not merge.',
    'Then set the spec frontmatter to `status: done`, append the PR URL under a `## Result` heading in the spec file, and land that edit as a second commit pushed to the same branch - the first commit is already pushed, so never amend it and never force-push.',
    minor.length
      ? `Finally post these minor review findings on the PR as one review with inline comments (GitHub MCP: create a pending review, add a comment per finding at its file and line, submit it as COMMENT - never as an approval or a change request). A finding with no file or line goes into the review body: ${JSON.stringify(minor)}`
      : 'The review left no minor findings - post no review comments.',
    `implementer report: ${JSON.stringify(impl)}`,
  ],
  SHIP,
)

if (!ship) return stop('ship', { reason: 'ops returned no result' })

log(`PR: ${ship.prUrl || 'not reported - check the ops notes'}`)

return {
  status: 'ready-for-merge',
  pr: ship.prUrl || null,
  branch: ship.branch || cut.branch,
  ciStatus: ship.ciStatus || null,
  review: review ? review.summary : 'review skipped',
  minorFindings: minor.length,
  skipped: [...skipped],
  tests,
  impl,
  rounds,
  spec,
  tier,
}
