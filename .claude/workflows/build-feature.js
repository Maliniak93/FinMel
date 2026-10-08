export const meta = {
  name: 'build-feature',
  description: 'Spec to a verified, reviewed change and the exact ship command: failing tests, implementation, verification, adversarial review',
  whenToUse: 'Invoked by /build and /fix after `gh-project.mjs prepare` cut the issue branch (args.spec = its local copy, args.issue, args.branch, args.title). Not for exploratory work - the spec is the contract.',
  phases: [
    { title: 'Tests', detail: 'test-writer turns every acceptance criterion into a failing test; haiku/high on tier 1, sonnet/medium on tier 2 (skippable)' },
    { title: 'Implement', detail: 'implementer does the work; haiku/high on a tier-1 skip-tests cleanup, opus/medium on tier 1, opus/high on tier 2; its Stop hook runs the affected suites' },
    { title: 'Verify', detail: 'verifier runs scripts/verify.mjs --fix --cache (a tree the Stop hook already proved green answers from cache); failures loop back to Implement', model: 'haiku' },
    { title: 'Review', detail: 'reviewer reads scripts/review-diff.mjs against the spec; opus/medium on tier 1, opus/high on tier 2 (skippable)', model: 'claude-opus-5-5' },
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
        properties: { name: { type: 'string' }, file: { type: 'string' }, ac: { type: 'string' } },
        required: ['name', 'file', 'ac'],
      },
    },
    projects: { type: 'array', items: { type: 'string' } },
    // Existing files the implementer should read first, as `path` or `path:start-end`, so it does not
    // rediscover what the test-writer already found.
    contextFiles: { type: 'array', items: { type: 'string' } },
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
    deviations: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          kind: { type: 'string', enum: ['test', 'design'] },
          file: { type: 'string' },
          what: { type: 'string' },
          why: { type: 'string' },
        },
        required: ['kind', 'what', 'why'],
      },
    },
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

// ---------------------------------------------------------------- setup

const { spec, issue, branch, title, tier = 1, maxRounds = 2, skip = [] } = args || {}

if (!(spec && issue && branch && title)) {
  return {
    status: 'blocked',
    stage: 'input',
    reason: 'args.spec (skarbiec-plan/issues/<n>.md), args.issue, args.branch and args.title are all required',
  }
}

const skipped = new Set((Array.isArray(skip) ? skip : [skip]).map((s) => String(s).trim().toLowerCase()))
const noTests = skipped.has('tests')

const OPUS = 'claude-opus-5-5'
// A tier-1 skip-tests spec is a mechanical cleanup: Haiku tries first and the first red verify hands it to Opus.
const cheapStart = noTests && tier === 1
let model = cheapStart ? 'haiku' : OPUS
let effort = cheapStart || tier >= 2 ? 'high' : 'medium'
const testWriter = tier >= 2 ? { model: 'sonnet', effort: 'medium' } : { model: 'haiku', effort: 'high' }
const reviewEffort = tier >= 2 ? 'high' : 'medium'

let rounds = 0
let escalated = false
let tests = { tests: [], projects: [], notes: [] }
let impl = null
// Every test or design change the implementer made, across all its rounds.
const deviations = []
let review = null

const RESERVE = 40000
const broke = () => Boolean(budget.total) && budget.remaining() < RESERVE
function step(agentType, label, lines, schema, opts) {
  return agent(lines.filter(Boolean).join('\n'), Object.assign({ agentType, schema, label }, opts || {}))
}

const clip = (text, n) => (text && String(text).length > n ? `${String(text).slice(0, n)}…` : text)
const findingsOf = (list) => (list || []).map((f) => ({ file: f.file, line: f.line, claim: clip(f.claim, 300) }))
// Single-quoted for bash (a `'` inside becomes `'"'"'`), so the command stays one line that matches its
// permission rule and runs without a prompt.
const sq = (text) => `'${String(text).replace(/'/g, `'"'"'`)}'`

const compact = () => ({
  tests: tests.tests.map((t) => t.name),
  filesTouched: impl ? impl.filesTouched.length : 0,
  openQuestions: (impl && impl.openQuestions) || [],
  deviations: deviations.map((d) => ({ kind: d.kind, file: d.file, what: clip(d.what, 200), why: clip(d.why, 300) })),
})

// No model touches git or the issue: the caller runs `nextCommand` verbatim, and ship.mjs commits, pushes,
// opens the PR and posts the run report (or, with --blocked, only the report).
const shipCommand = (run, blocked) =>
  `node scripts/ship.mjs ${issue} --branch ${sq(branch)} --title ${sq(title)} --json ${sq(JSON.stringify(run))}${blocked ? ' --blocked' : ''}`

function stop(stage, extra) {
  const result = Object.assign({ status: 'blocked', stage, issue, tier, rounds, branch }, compact(), extra)
  result.nextCommand = shipCommand(
    {
      status: 'blocked',
      stage,
      reason: clip(result.reason, 300),
      failures: (result.failures || []).map((f) => ({ step: f.step, summary: clip(f.summary, 300), file: f.file })),
      blocking: findingsOf(result.findings),
      deviations: result.deviations,
    },
    true,
  )
  return result
}

const OWN =
  'You own the design and the tests: a wrong test or a wrong design decision is yours to fix - list each change in `deviations` with why. Every acceptance criterion must still be proven by a test.'
async function implement(label, lines) {
  const result = await step('implementer', label, [`Work on the spec at \`${spec}\`. You are already on its branch \`${branch}\`.`].concat(lines, [OWN]), IMPL, { model, effort })
  if (result && result.deviations) deviations.push(...result.deviations)
  return result
}

// No project list: the verifier auto-detects the affected areas exactly as the implementer's Stop hook
// does, so a tree the hook already proved green is a cache hit.
const verify = (label) =>
  step('verifier', label, ['Run the verification script and report its VERIFY_RESULT line verbatim.', 'projects: []', 'Fix nothing. Explain nothing.'], VERIFY)

const fixRound = (label, failures) =>
  implement(label, [
    `Fix exactly these verification failures: ${JSON.stringify(failures)}`,
    `tests: ${JSON.stringify(tests)}`,
    'Do not refactor around them. A failure caused by a wrong test is fixed in the test and recorded in `deviations`.',
  ])

log(
  `Spec ${spec} on ${branch} - tier ${tier}, test-writer ${testWriter.model}/${testWriter.effort}, implementer ${model}/${effort}, reviewer opus/${reviewEffort}, max ${maxRounds} fix rounds${skipped.size ? `, skipping: ${[...skipped].join(', ')}` : ''}`,
)

// ---------------------------------------------------------------- tests

phase('Tests')

if (noTests) {
  log('Tests skipped - this spec proves its acceptance criteria with commands, not new tests')
} else {
  if (broke()) return stop('tests', { reason: 'budget' })

  tests = await step(
    'test-writer',
    'failing tests from acceptance criteria',
    [
      `Write the failing tests for the spec at \`${spec}\`. You are on its branch \`${branch}\`.`,
      'Read that spec first and start from its Code map section, then only the skarbiec-plan sections it names, then the target test project Fixtures/ helpers the Code map names.',
      'One or more tests per acceptance criterion, named as the spec names them. Include tenancy isolation for any new user-owned resource and outbox/idempotency tests for any new or changed event.',
      'Run them and confirm they are red for the right reason. Write no production code.',
      'In `contextFiles` list the existing files the implementer should read first - the fixtures you extended, the precedent slice or component, the code under test - as `path` or `path:start-end`. At most 15.',
    ],
    TESTS,
    testWriter,
  )

  if (!tests) return stop('tests', { reason: 'test-writer returned no result' })

  if (!tests.tests.length) {
    return stop('tests', {
      reason: 'test-writer produced no tests. If this spec genuinely needs none, add the `skip-tests` label to its issue and re-run; otherwise its acceptance criteria are not testable.',
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
        "Start from the spec's Code map and the tests' contextFiles - read those instead of rediscovering the code. Then only the skarbiec-plan sections the spec names. Smallest correct change; do not widen scope beyond the spec.",
      ]
    : [
        'This spec has no new tests - it adds no behaviour. Implement its Scope exactly and leave every existing suite green.',
        "Start from the spec's Code map, then only the skarbiec-plan sections it names. Smallest correct change; do not widen scope beyond the spec.",
        'Run the command each acceptance criterion names as its proof, and report those commands in `commandsRun`.',
      ],
)

if (!impl) return stop('implement', { reason: 'implementer returned no result' })
if (impl.status === 'blocked') return stop('implement', { reason: 'implementer blocked', notes: impl.notes || [] })

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

  if (cheapStart && !escalated) {
    escalated = true
    model = OPUS
    effort = 'high'
    log('the Haiku cleanup attempt is red - escalating the implementer to opus/high')
  }
  // Tier 1 gets one extra round at high effort after escalating; tier 2 gets exactly maxRounds.
  if (round === maxRounds && tier === 1 && !escalated) {
    escalated = true
    model = OPUS
    effort = 'high'
    log('tier 1 exhausted its fix rounds - escalating the implementer to opus/high for one final round')
  }
  if (round >= (escalated ? maxRounds + 1 : maxRounds)) {
    log('verify still red after the final round - stopping')
    return stop('verify', { failures: verified.failures })
  }

  phase('Implement')
  if (broke()) return stop('implement', { reason: 'budget' })

  impl = await fixRound(`fix verify failures (round ${round + 1})`, verified.failures)
  if (!impl) return stop('implement', { reason: 'implementer returned no result on a fix round' })
  if (impl.status === 'blocked') return stop('implement', { reason: 'implementer blocked on a fix round', notes: impl.notes || [] })
}

// ---------------------------------------------------------------- review loop

if (skipped.has('review')) {
  phase('Review')
  log('Review skipped by request - shipping on a green verify alone')
} else {
  for (let round = 0; ; round++) {
    phase('Review')
    if (broke()) return stop('review', { reason: 'budget' })

    review = await step(
      'reviewer',
      `adversarial review (round ${round + 1})`,
      [
        `Review the change for the spec at \`${spec}\`.`,
        `It is uncommitted on \`${branch}\`: \`node scripts/review-diff.mjs --stat\` lists it and \`node scripts/review-diff.mjs -- <path>\` shows a file's diff, new untracked files included.`,
        `tests claimed: ${JSON.stringify(tests.tests)}`,
        `implementer report: ${JSON.stringify(impl)}`,
        deviations.length ? `deviations from the spec or the tests, all rounds: ${JSON.stringify(deviations)} - judge each on its why.` : null,
        tests.tests.length ? null : 'This spec carries the `skip-tests` label - no new tests were written. Judge each acceptance criterion by the command it names and confirm the existing suites still cover the behaviour it touches.',
        'blocking only for wrong behaviour, an unproven acceptance criterion, a hard-rule violation or forbidden scope. Everything else is minor.',
      ],
      REVIEW,
      { model: OPUS, effort: reviewEffort },
    )

    if (!review) return stop('review', { reason: 'reviewer returned no result' })

    const blocking = review.findings.filter((f) => f.severity === 'blocking')
    if (!blocking.length) {
      log(`review clean (${review.findings.length} minor finding(s))`)
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
    if (impl.status === 'blocked') return stop('implement', { reason: 'implementer blocked on a review fix', notes: impl.notes || [] })

    rounds = rounds + 1

    for (let fix = 0; ; fix++) {
      phase('Verify')
      if (broke()) return stop('verify', { reason: 'budget' })
      const label = fix ? `verify after review fix (round ${round + 1}, fix ${fix})` : `verify after review fix (round ${round + 1})`
      const reverified = await verify(label)
      if (!reverified) return stop('verify', { reason: 'verifier returned no result after a review fix' })
      if (reverified.ok) break
      log(`the review fix broke verification (${reverified.failures.map((f) => f.step).join(', ') || 'unspecified'})`)
      if (fix >= maxRounds) return stop('verify', { failures: reverified.failures })

      phase('Implement')
      if (broke()) return stop('implement', { reason: 'budget' })

      impl = await fixRound(`fix verify failures after review fix (round ${round + 1}, fix ${fix + 1})`, reverified.failures)
      if (!impl) return stop('implement', { reason: 'implementer returned no result on a fix round' })
      if (impl.status === 'blocked') return stop('implement', { reason: 'implementer blocked on a fix round', notes: impl.notes || [] })

      rounds = rounds + 1
    }
    log('verify green again after the review fix')
  }
}

// ---------------------------------------------------------------- ready to ship

const minor = review ? review.findings.filter((f) => f.severity === 'minor') : []
log(`${branch} is verified${review ? ' and reviewed' : ''} - the caller ships it with nextCommand; merging is yours`)

return {
  status: 'ready',
  branch,
  review: review ? clip(review.summary, 400) : 'review skipped',
  minorFindings: findingsOf(minor),
  skipped: [...skipped],
  rounds,
  issue,
  tier,
  ...compact(),
  nextCommand: shipCommand({
    status: 'shipped',
    branch,
    tests: tests.tests.map((t) => t.name),
    rounds,
    reviewRan: Boolean(review),
    minor: findingsOf(minor),
    deviations: compact().deviations,
  }),
}
