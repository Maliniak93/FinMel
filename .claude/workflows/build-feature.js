export const meta = {
  name: 'build-feature',
  description: 'Spec to a verified, reviewed change and the exact ship command: failing tests, implementation, verification, adversarial review',
  whenToUse: 'Invoked by /build and /fix after `gh-project.mjs prepare` cut the issue branch (args.spec = its local copy, args.issue, args.branch, args.title). Not for exploratory work - the spec is the contract.',
  phases: [
    { title: 'Tests', detail: 'test-writer turns every acceptance criterion into a failing test; haiku/high on tier 1, sonnet/medium on tier 2 (skippable)' },
    { title: 'Implement', detail: 'implementer does the work and runs the full scripts/verify.mjs itself with up to 3 fixes; sonnet/medium on tier 1, haiku/high on a tier-1 skip-tests cleanup, sonnet/high on tier 2; a red result escalates once to opus (medium on tier 1, high on tier 2), a second red blocks the run' },
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

const FAILURES = {
  type: 'array',
  items: {
    type: 'object',
    properties: { step: { type: 'string' }, summary: { type: 'string' }, file: { type: 'string' } },
    required: ['step', 'summary'],
  },
}

const IMPL = {
  type: 'object',
  properties: {
    status: { type: 'string', enum: ['done', 'blocked'] },
    verified: { type: 'boolean' },
    failures: FAILURES,
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
  required: ['status', 'verified', 'filesTouched', 'projects'],
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
const startLevel =
  tier >= 2 ? { model: 'sonnet', effort: 'high' } : noTests ? { model: 'haiku', effort: 'high' } : { model: 'sonnet', effort: 'medium' }
const escalatedLevel = { model: OPUS, effort: tier >= 2 ? 'high' : 'medium' }
// Escalation happens once per run; every later implementer call, review fixes included, stays escalated.
let level = startLevel
const testWriter = tier >= 2 ? { model: 'sonnet', effort: 'medium' } : { model: 'haiku', effort: 'high' }
const reviewEffort = tier >= 2 ? 'high' : 'medium'

let rounds = 0
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
const VERIFY_LINE =
  'Before you return, run the full verification (`scripts/verify.mjs --all`) as your agent prompt describes, with at most 3 fixes. `verified: true` only on a green result, and edit nothing after it.'
async function implement(label, lines) {
  const result = await step(
    'implementer',
    label,
    [`Work on the spec at \`${spec}\`. You are already on its branch \`${branch}\`.`].concat(lines, [OWN, VERIFY_LINE]),
    IMPL,
    level,
  )
  if (result && result.deviations) deviations.push(...result.deviations)
  return result
}

const failed = (r) => !r || r.status === 'blocked' || r.verified !== true
const why = (r) => (!r ? 'returned no result' : r.status === 'blocked' ? 'blocked' : 'still red after its fixes')
const giveUp = (r, reason) => ({ reason, failures: (r && r.failures) || [], notes: (r && r.notes) || [] })

// Runs the implementer at the current level and escalates once on a red or blocked result.
// Returns { impl } or { reason, failures, notes } for the caller's stop('implement').
async function attempt(label, lines) {
  const first = await implement(label, lines)
  if (!failed(first)) return { impl: first }
  if (level === escalatedLevel) return giveUp(first, `implementer ${why(first)} on the escalated level`)

  log(`${label}: implementer ${why(first)} on ${level.model}/${level.effort} - escalating to ${escalatedLevel.model}/${escalatedLevel.effort}`)
  level = escalatedLevel
  rounds = rounds + 1
  if (broke()) return giveUp(first, 'budget')

  const second = await implement(
    `${label} (escalated)`,
    lines.concat([
      `A previous attempt ${why(first)}. Its tree is still on disk - continue from it rather than starting over.`,
      first ? `Its last verify failures: ${JSON.stringify(first.failures || [])}` : null,
      first && first.notes && first.notes.length ? `Its notes: ${JSON.stringify(first.notes)}` : null,
      first && first.deviations && first.deviations.length ? `Its deviations: ${JSON.stringify(first.deviations)}` : null,
      first && first.openQuestions && first.openQuestions.length ? `Its open questions: ${JSON.stringify(first.openQuestions)}` : null,
    ]),
  )
  if (!failed(second)) return { impl: second }
  return giveUp(second, `escalated implementer ${why(second)}`)
}

log(
  `Spec ${spec} on ${branch} - tier ${tier}, test-writer ${testWriter.model}/${testWriter.effort}, implementer ${startLevel.model}/${startLevel.effort} escalating once to ${escalatedLevel.model}/${escalatedLevel.effort}, reviewer opus/${reviewEffort} then opus/medium re-reviews, max ${maxRounds} review fix rounds${skipped.size ? `, skipping: ${[...skipped].join(', ')}` : ''}`,
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

const initial = await attempt(
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

if (!initial.impl) return stop('implement', initial)
impl = initial.impl

log(`implementer touched ${impl.filesTouched.length} file(s), verify green`)

// ---------------------------------------------------------------- review loop

if (skipped.has('review')) {
  phase('Review')
  log('Review skipped by request - shipping on a green verify alone')
} else {
  let lastFix = null
  for (let round = 0; ; round++) {
    phase('Review')
    if (broke()) return stop('review', { reason: 'budget' })

    const previous = round > 0 ? lastFix : null
    review = await step(
      'reviewer',
      previous ? `re-review (round ${round + 1})` : `adversarial review (round ${round + 1})`,
      previous
        ? [
            `Re-review the change for the spec at \`${spec}\` (round ${round + 1}) - see "Re-review" in your agent prompt.`,
            `It is uncommitted on \`${branch}\`: \`node scripts/review-diff.mjs -- <path>\` shows a file's diff, new untracked files included.`,
            `blocking findings of the previous round: ${JSON.stringify(previous.blocking)}`,
            `implementer response: ${JSON.stringify({ notes: previous.impl.notes || [], deviations: previous.impl.deviations || [], filesTouched: previous.impl.filesTouched })}`,
            'Check only that each finding is resolved (or rebutted with a sound reason in notes) and that the diff of the files touched in that round broke nothing. A full re-review is not your job.',
            'blocking only for an unresolved finding or a regression in those files. Everything else is minor.',
          ]
        : [
            `Review the change for the spec at \`${spec}\`.`,
            `It is uncommitted on \`${branch}\`: \`node scripts/review-diff.mjs --stat\` lists it and \`node scripts/review-diff.mjs -- <path>\` shows a file's diff, new untracked files included.`,
            `tests claimed: ${JSON.stringify(tests.tests)}`,
            `implementer report: ${JSON.stringify(impl)}`,
            deviations.length ? `deviations from the spec or the tests, all rounds: ${JSON.stringify(deviations)} - judge each on its why.` : null,
            tests.tests.length ? null : 'This spec carries the `skip-tests` label - no new tests were written. Judge each acceptance criterion by the command it names and confirm the existing suites still cover the behaviour it touches.',
            'blocking only for wrong behaviour, an unproven acceptance criterion, a hard-rule violation or forbidden scope. Everything else is minor.',
          ],
      REVIEW,
      { model: OPUS, effort: previous ? 'medium' : reviewEffort },
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

    rounds = rounds + 1
    const fix = await attempt(`address blocking findings (round ${round + 1})`, [
      `Address exactly these blocking review findings: ${JSON.stringify(blocking)}`,
      `tests: ${JSON.stringify(tests)}`,
      'Change only what the findings name. A finding you disagree with goes into notes with the reason - do not silently ignore it.',
    ])
    if (!fix.impl) return stop('implement', fix)
    impl = fix.impl
    lastFix = { blocking, impl }
    log(`review fix round ${round + 1} verified green`)
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
