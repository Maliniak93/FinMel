export const meta = {
  name: 'build-feature',
  description: 'Spec to PR: failing tests, implementation, verification, adversarial review, commit and PR',
  whenToUse: 'Invoked by /build on a spec whose status is approved. Not for exploratory work - the spec is the contract.',
  phases: [
    { title: 'Tests', detail: 'test-writer turns every acceptance criterion into a failing test', model: 'sonnet' },
    { title: 'Implement', detail: 'implementer makes them pass; sonnet/high on tier 1, opus/xhigh on tier 2' },
    { title: 'Verify', detail: 'verifier runs scripts/verify.mjs; failures loop back to Implement', model: 'haiku' },
    { title: 'Review', detail: 'reviewer checks the tree against the spec; blocking findings loop back', model: 'opus' },
    { title: 'Ship', detail: 'ops commits on feat/<slug>, pushes and opens the PR', model: 'sonnet' },
  ],
}

// ---------------------------------------------------------------- schemas

const TESTS = {
  type: 'object',
  properties: {
    tests: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          name: { type: 'string' },
          file: { type: 'string' },
          ac: { type: 'string' },
        },
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
        properties: {
          step: { type: 'string' },
          summary: { type: 'string' },
          file: { type: 'string' },
        },
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

const { spec, tier = 1, maxRounds = 2 } = args || {}

if (!spec) {
  return {
    status: 'blocked',
    stage: 'input',
    reason: 'args.spec is required - the path to skarbiec-plan/specs/<slug>.md',
  }
}

let model = tier >= 2 ? 'opus' : 'sonnet'
let effort = tier >= 2 ? 'xhigh' : 'high'

const RESERVE = 40000
const outOfBudget = () => Boolean(budget.total) && budget.remaining() < RESERVE
const brokeAt = (stage, extra) =>
  Object.assign({ status: 'blocked', stage, reason: 'budget', spec, tier }, extra || {})
const projectsOf = (result, fallback) => {
  const list = result && Array.isArray(result.projects) && result.projects.length ? result.projects : fallback
  return Array.isArray(list) ? list : []
}

let rounds = 0
let escalated = false

// ---------------------------------------------------------------- tests

phase('Tests')
log(`Spec ${spec} - tier ${tier}, implementer on ${model}/${effort}, max ${maxRounds} fix rounds`)

if (outOfBudget()) return brokeAt('tests')

const tests = await agent(
  [
    `Write the failing tests for the spec at \`${spec}\`.`,
    'Read that spec first, then only the skarbiec-plan sections it names, then the target test project Fixtures/.',
    'One or more tests per acceptance criterion, named as the spec names them. Include tenancy isolation for any new user-owned resource and outbox/idempotency tests for any new or changed event.',
    'Run them and confirm they are red for the right reason. Write no production code.',
  ].join('\n'),
  { agentType: 'test-writer', schema: TESTS, label: 'failing tests from acceptance criteria' },
)

if (!tests) {
  return { status: 'blocked', stage: 'tests', reason: 'test-writer returned no result', spec, tier }
}

log(`${tests.tests.length} failing test(s) across: ${tests.projects.join(', ') || 'no project reported'}`)

if (!tests.tests.length) {
  return {
    status: 'blocked',
    stage: 'tests',
    reason: 'test-writer produced no tests - the spec has no testable acceptance criteria',
    notes: tests.notes || [],
    spec,
    tier,
  }
}

// ---------------------------------------------------------------- implement

phase('Implement')
if (outOfBudget()) return brokeAt('implement', { tests })

let impl = await agent(
  [
    `Make the failing tests pass for the spec at \`${spec}\`.`,
    `Tests written for it: ${JSON.stringify(tests)}`,
    'Read the spec and only the skarbiec-plan sections it names. Smallest correct change; do not widen scope beyond the spec.',
  ].join('\n'),
  { agentType: 'implementer', model, effort, schema: IMPL, label: 'implement the spec' },
)

if (!impl) {
  return { status: 'blocked', stage: 'implement', reason: 'implementer returned no result', tests, spec, tier }
}
if (impl.status === 'blocked') {
  log('implementer reported blocked - stopping before verify')
  return { status: 'blocked', stage: 'implement', reason: 'implementer blocked', impl, tests, spec, tier }
}

log(`implementer touched ${impl.filesTouched.length} file(s)`)

// ---------------------------------------------------------------- verify loop

for (let round = 0; ; round++) {
  phase('Verify')
  if (outOfBudget()) return brokeAt('verify', { tests, impl, rounds })

  const projects = projectsOf(impl, tests.projects)
  const verified = await agent(
    [
      'Run the verification script and report its VERIFY_RESULT line verbatim.',
      `projects: ${JSON.stringify(projects)}`,
      'Fix nothing. Explain nothing.',
    ].join('\n'),
    { agentType: 'verifier', schema: VERIFY, label: `verify round ${round + 1}` },
  )

  if (!verified) {
    return { status: 'blocked', stage: 'verify', reason: 'verifier returned no result', tests, impl, rounds, spec, tier }
  }
  if (verified.ok) {
    log(`verify green after ${rounds} fix round(s)`)
    break
  }

  rounds = round + 1
  log(`verify failed (${verified.failures.map((f) => f.step).join(', ') || 'unspecified'})`)

  if (round === maxRounds && tier === 1 && !escalated) {
    escalated = true
    model = 'opus'
    effort = 'xhigh'
    log('tier 1 exhausted its fix rounds - escalating the implementer to opus/xhigh for one final round')
  }
  // Tier 2 gets exactly maxRounds fix rounds; tier 1 gets one extra round on opus after escalating.
  const fixRoundLimit = escalated ? maxRounds + 1 : maxRounds
  if (round >= fixRoundLimit) {
    log('verify still red after the final round - stopping')
    return { status: 'blocked', stage: 'verify', failures: verified.failures, tests, impl, rounds, spec, tier }
  }

  phase('Implement')
  if (outOfBudget()) return brokeAt('implement', { tests, impl, rounds, failures: verified.failures })

  impl = await agent(
    [
      `Fix the verification failures for the spec at \`${spec}\`.`,
      `failures: ${JSON.stringify(verified.failures)}`,
      `tests: ${JSON.stringify(tests)}`,
      'Fix exactly these failures. Do not refactor around them and do not weaken or delete a test.',
    ].join('\n'),
    { agentType: 'implementer', model, effort, schema: IMPL, label: `fix verify failures (round ${round + 1})` },
  )

  if (!impl) {
    return { status: 'blocked', stage: 'implement', reason: 'implementer returned no result on a fix round', tests, rounds, spec, tier }
  }
  if (impl.status === 'blocked') {
    return { status: 'blocked', stage: 'implement', reason: 'implementer blocked on a fix round', impl, tests, rounds, spec, tier }
  }
}

// ---------------------------------------------------------------- review loop

let reviewSummary = ''

for (let round = 0; ; round++) {
  phase('Review')
  if (outOfBudget()) return brokeAt('review', { tests, impl, rounds })

  const review = await agent(
    [
      `Review the working tree against the spec at \`${spec}\`.`,
      `tests claimed: ${JSON.stringify(tests.tests)}`,
      `implementer report: ${JSON.stringify(impl)}`,
      'Gather the diff yourself (git status --porcelain, git diff, git diff master...HEAD). Treat every claim above as a claim to verify.',
      'blocking only for wrong behaviour, an unproven acceptance criterion, a hard-rule violation or forbidden scope. Everything else is minor.',
    ].join('\n'),
    { agentType: 'reviewer', schema: REVIEW, label: `adversarial review (round ${round + 1})` },
  )

  if (!review) {
    return { status: 'blocked', stage: 'review', reason: 'reviewer returned no result', tests, impl, rounds, spec, tier }
  }

  reviewSummary = review.summary
  const blocking = review.findings.filter((f) => f.severity === 'blocking')
  const minor = review.findings.length - blocking.length

  if (!blocking.length) {
    log(`review clean (${minor} minor finding(s))`)
    break
  }

  log(`review returned ${blocking.length} blocking finding(s)`)

  if (round >= maxRounds) {
    return { status: 'blocked', stage: 'review', findings: blocking, summary: review.summary, tests, impl, rounds, spec, tier }
  }

  phase('Implement')
  if (outOfBudget()) return brokeAt('implement', { tests, impl, rounds, findings: blocking })

  impl = await agent(
    [
      `Address the blocking review findings for the spec at \`${spec}\`.`,
      `findings: ${JSON.stringify(blocking)}`,
      `tests: ${JSON.stringify(tests)}`,
      'Address exactly these findings. A finding you disagree with goes into notes with the reason - do not silently ignore it.',
    ].join('\n'),
    { agentType: 'implementer', model, effort, schema: IMPL, label: `address blocking findings (round ${round + 1})` },
  )

  if (!impl) {
    return { status: 'blocked', stage: 'implement', reason: 'implementer returned no result on a review fix', tests, rounds, spec, tier }
  }
  if (impl.status === 'blocked') {
    return { status: 'blocked', stage: 'implement', reason: 'implementer blocked on a review fix', impl, tests, rounds, spec, tier }
  }

  rounds = rounds + 1

  phase('Verify')
  if (outOfBudget()) return brokeAt('verify', { tests, impl, rounds })

  const reverified = await agent(
    [
      'Run the verification script and report its VERIFY_RESULT line verbatim.',
      `projects: ${JSON.stringify(projectsOf(impl, tests.projects))}`,
      'Fix nothing. Explain nothing.',
    ].join('\n'),
    { agentType: 'verifier', schema: VERIFY, label: `verify after review fix (round ${round + 1})` },
  )

  if (!reverified) {
    return { status: 'blocked', stage: 'verify', reason: 'verifier returned no result after a review fix', tests, impl, rounds, spec, tier }
  }
  if (!reverified.ok) {
    log('the review fix broke verification - stopping')
    return { status: 'blocked', stage: 'verify', failures: reverified.failures, tests, impl, rounds, spec, tier }
  }
  log('verify green again after the review fix')
}

// ---------------------------------------------------------------- ship

phase('Ship')
if (outOfBudget()) return brokeAt('ship', { tests, impl, rounds })

const ship = await agent(
  [
    `Ship the work for the spec at \`${spec}\`.`,
    'Read that spec first and derive the slug, the branch `feat/<slug>` and the PR title from its frontmatter (`branch`, `title`).',
    'Verify git status is clean of unrelated changes, create or switch to the branch from master (never commit on master), stage everything, commit with the spec title as the subject, push with -u, then open the PR to master.',
    'Then set the spec frontmatter to `status: done` and append the PR URL under a `## Result` heading in the spec file.',
    'Do not merge the PR - that is the user\'s decision.',
    `implementer report: ${JSON.stringify(impl)}`,
  ].join('\n'),
  { agentType: 'ops', schema: SHIP, label: 'commit, push, open PR' },
)

if (!ship) {
  return { status: 'blocked', stage: 'ship', reason: 'ops returned no result', tests, impl, rounds, spec, tier }
}

log(`PR: ${ship.prUrl || 'not reported - check the ops notes'}`)

return {
  status: 'ready-for-merge',
  pr: ship.prUrl || null,
  branch: ship.branch,
  ciStatus: ship.ciStatus || null,
  review: reviewSummary,
  tests,
  impl,
  rounds,
  spec,
  tier,
}
