export const meta = {
  name: 'build-feature',
  description: 'Spec to a verified, reviewed change and the exact ship command: failing tests, implementation, verification, adversarial review',
  whenToUse: 'Invoked by /build and /fix after `gh-project.mjs prepare` cut the issue branch (args.spec = its local copy, args.issue, args.branch, args.title). Not for exploratory work - the spec is the contract.',
  phases: [
    { title: 'Tests', detail: 'test-writer turns every acceptance criterion into a failing test; haiku/high on tier 1, sonnet/medium on tier 2 (skippable)' },
    { title: 'Implement', detail: 'implementer does the work and runs the full scripts/verify.mjs itself with up to 3 fixes; sonnet/medium on tier 1, haiku/high on a tier-1 skip-tests cleanup, sonnet/high on tier 2; a red result escalates once to opus (medium on tier 1, high on tier 2), a second red blocks the run' },
    { title: 'E2E', detail: 'only for an epic\'s e2e part (args.e2e): replaces Tests and Implement. The e2e agent automates the epic\'s manual test plan as Playwright tests, runs verify --projects e2e and the whole e2e suite; sonnet (medium on tier 1, high on tier 2), one escalation to opus; a stable app failure or an environment problem blocks the run' },
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
    manualTest: {
      type: 'array',
      items: {
        type: 'object',
        properties: { issue: { type: 'number' }, file: { type: 'string' } },
        required: ['issue', 'file'],
      },
    },
  },
  required: ['status', 'verified', 'filesTouched', 'projects'],
}

const E2E_RESULT = {
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
    deviations: IMPL.properties.deviations,
    manualTest: IMPL.properties.manualTest,
    e2e: {
      type: 'object',
      properties: { passed: { type: 'number' }, total: { type: 'number' }, flaky: { type: 'array', items: { type: 'string' } } },
      required: ['passed', 'total'],
    },
    blocked: {
      type: 'object',
      properties: {
        class: { type: 'string', enum: ['app', 'environment'] },
        summary: { type: 'string' },
        failed: {
          type: 'array',
          items: {
            type: 'object',
            properties: { test: { type: 'string' }, step: { type: 'string' }, trace: { type: 'string' } },
            required: ['test'],
          },
        },
        reportPath: { type: 'string' },
      },
      required: ['class'],
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

const { spec, issue, branch, title, tier = 1, maxRounds = 2, skip = [], epic = null, e2e = false } = args || {}

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
const e2eStartLevel = { model: 'sonnet', effort: tier >= 2 ? 'high' : 'medium' }
// Escalation happens once per run; every later implementer (or e2e agent) call, review fixes included, stays escalated.
let level = e2e ? e2eStartLevel : startLevel
const testWriter = tier >= 2 ? { model: 'sonnet', effort: 'medium' } : { model: 'haiku', effort: 'high' }
const reviewEffort = tier >= 2 ? 'high' : 'medium'

let rounds = 0
let tests = { tests: [], projects: [], notes: [] }
let impl = null
// Every test or design change the implementer made, across all its rounds.
const deviations = []
// Plan files the implementer wrote (by issue), across all its rounds; a later round overrides an earlier one.
const manualTest = new Map()
let review = null

const RESERVE = 40000
const broke = () => Boolean(budget.total) && budget.remaining() < RESERVE
const REPLAY = { steps: [], interrupted: null } // @replay
function step(agentType, label, lines, schema, opts) {
  if (REPLAY.steps.length) {
    const next = REPLAY.steps[0]
    if (next.label === label) {
      REPLAY.steps.shift()
      log(`replayed: ${label}`)
      return Promise.resolve(next.result)
    }
    log(`replay diverged at ${label} (expected ${next.label}) - running live from here`)
    REPLAY.steps.length = 0
  }
  const resumed = label === REPLAY.interrupted
  if (resumed) REPLAY.interrupted = null
  const continueLine = 'A previous run was interrupted during this step; its partial changes are on disk - continue from them rather than starting over.'
  return agent(lines.concat(resumed ? continueLine : null).filter(Boolean).join('\n'), Object.assign({ agentType, schema, label }, opts || {}))
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
  const run = {
    status: 'blocked',
    stage,
    reason: clip(result.reason, 300),
    failures: (result.failures || []).map((f) => ({ step: f.step, summary: clip(f.summary, 300), file: f.file })),
    blocking: findingsOf(result.findings),
    deviations: result.deviations,
  }
  if (stage === 'e2e' && result.e2eBlock) {
    Object.assign(run, {
      e2e: true,
      class: result.e2eBlock.class,
      failed: (result.e2eBlock.failed || []).map((f) => ({ test: clip(f.test, 200), step: clip(f.step, 200), trace: f.trace })),
      reportPath: result.e2eBlock.reportPath,
    })
  }
  result.nextCommand = shipCommand(run, true)
  return result
}

// The e2e agent classified a stable app failure or an environment problem: that is a result, not a failed attempt.
function e2eStop(r) {
  const b = r.blocked
  return stop('e2e', {
    reason: b.summary || `e2e ${b.class} failure`,
    e2eBlock: b,
    notes: r.notes || [],
    failures: r.failures || [],
  })
}

const OWN =
  'You own the design and the tests: a wrong test or a wrong design decision is yours to fix - list each change in `deviations` with why. Every acceptance criterion must still be proven by a test.'
const epicLast = Boolean(epic && epic.last)
const MANUAL_LINE = epicLast
  ? `This is the last part of epic #${epic.number}: compare the spec's \`## Test manualny\` with what you built (rewrite \`skarbiec-plan/issues/${issue}.manual.md\` on a deviation) and ALWAYS write the refreshed whole-feature plan to \`skarbiec-plan/issues/${epic.number}.manual.md\`, built from the epic draft \`skarbiec-plan/issues/${epic.number}.md\`, the sibling parts' plans (\`skarbiec-plan/issues/<m>.md\`) and the code. Return both in \`manualTest\` as \`{ issue, file }\`.`
  : "Compare the spec's `## Test manualny` with what you built. On a deviation (screen, route, action, amount shown) write the full corrected section to `skarbiec-plan/issues/" +
    issue +
    '.manual.md` and return `manualTest: [{ issue, file }]`; otherwise return no `manualTest`.'
const VERIFY_LINE =
  'Before you return, run the full verification (`scripts/verify.mjs --all`) as your agent prompt describes, with at most 3 fixes. `verified: true` only on a green result, and edit nothing after it.'
async function implement(label, lines) {
  const result = await step(
    'implementer',
    label,
    [`Work on the spec at \`${spec}\`. You are already on its branch \`${branch}\`.`].concat(lines, [OWN, MANUAL_LINE, VERIFY_LINE]),
    IMPL,
    level,
  )
  if (result && result.deviations) deviations.push(...result.deviations)
  if (result && result.manualTest) for (const m of result.manualTest) manualTest.set(m.issue, m)
  return result
}

// The e2e part: the e2e agent replaces the test-writer and the implementer. It owns e2e/ and the epic's plan file only.
const E2E_LINES = [
  'This is the e2e part of the epic: automate the epic\'s `## Test manualny` 1:1 as Playwright tests (one `Krok <n>` step per plan step, `(tylko manualnie)` for a step that cannot be automated) and run the whole suite.',
  `Read the epic body \`skarbiec-plan/issues/${epic ? epic.number : '<epic>'}.md\` and the sibling parts' bodies \`skarbiec-plan/issues/<m>.md\` first. Edit only files under \`e2e/\` and the plan files; production code is never yours.`,
  'Run `node scripts/verify.mjs --projects e2e --fix --cache`, then the whole suite with `node scripts/e2e.mjs run --detach` / `--await`. A stable app failure or an environment problem is a `blocked` result with `blocked.class` (`app` or `environment`), the test, the `Krok <n>` step, the trace path and the report path - never a patch to production code.',
  'Refresh the epic\'s whole-feature plan in `skarbiec-plan/issues/' + (epic ? epic.number : '<epic>') + '.manual.md` and return it in `manualTest` as `{ issue, file }`.',
  'You own the test code: a wrong test is yours to fix - list each change in `deviations` with why. `verified: true` only on a green verify and a green whole-suite e2e run of the same tree, and edit nothing after it.',
]
async function e2eRun(label, lines) {
  const result = await step(
    'e2e',
    label,
    [`Work on the e2e part at \`${spec}\`. You are already on its branch \`${branch}\`.`].concat(lines, E2E_LINES),
    E2E_RESULT,
    level,
  )
  if (result && result.deviations) deviations.push(...result.deviations)
  if (result && result.manualTest) for (const m of result.manualTest) manualTest.set(m.issue, m)
  return result
}

const failed = (r) => !r || r.status === 'blocked' || r.verified !== true
// A classified block (app or environment) is the e2e agent's answer, not a failed attempt: it neither escalates nor retries.
const e2eFailed = (r) => !r || (r.status === 'blocked' ? !(r.blocked && r.blocked.class) : r.verified !== true || !r.e2e)
const why = (r) => (!r ? 'returned no result' : r.status === 'blocked' ? 'blocked' : 'still red after its fixes')
const giveUp = (r, reason) => ({ reason, failures: (r && r.failures) || [], notes: (r && r.notes) || [] })

// Runs the implementer (the e2e agent in an e2e part) at the current level and escalates once on a red or blocked result.
// Returns { impl } or { reason, failures, notes } for the caller's stop('implement').
async function attempt(label, lines, runner = implement, isFailed = failed) {
  const first = await runner(label, lines)
  if (!isFailed(first)) return { impl: first }
  if (level === escalatedLevel) return giveUp(first, `${e2e ? 'e2e agent' : 'implementer'} ${why(first)} on the escalated level`)

  log(`${label}: ${e2e ? 'e2e agent' : 'implementer'} ${why(first)} on ${level.model}/${level.effort} - escalating to ${escalatedLevel.model}/${escalatedLevel.effort}`)
  level = escalatedLevel
  rounds = rounds + 1
  if (broke()) return giveUp(first, 'budget')

  const second = await runner(
    `${label} (escalated)`,
    lines.concat([
      `A previous attempt ${why(first)}. Its tree is still on disk - continue from it rather than starting over.`,
      first ? `Its last verify failures: ${JSON.stringify(first.failures || [])}` : null,
      first && first.notes && first.notes.length ? `Its notes: ${JSON.stringify(first.notes)}` : null,
      first && first.deviations && first.deviations.length ? `Its deviations: ${JSON.stringify(first.deviations)}` : null,
      first && first.openQuestions && first.openQuestions.length ? `Its open questions: ${JSON.stringify(first.openQuestions)}` : null,
    ]),
  )
  if (!isFailed(second)) return { impl: second }
  return giveUp(second, `escalated ${e2e ? 'e2e agent' : 'implementer'} ${why(second)}`)
}

log(
  e2e
    ? `Spec ${spec} on ${branch} - e2e part, tier ${tier}, e2e agent ${e2eStartLevel.model}/${e2eStartLevel.effort} escalating once to ${escalatedLevel.model}/${escalatedLevel.effort}, no test-writer and no implementer, reviewer opus/${reviewEffort} then opus/medium re-reviews, max ${maxRounds} review fix rounds${skipped.size ? `, skipping: ${[...skipped].join(', ')}` : ''}`
    : `Spec ${spec} on ${branch} - tier ${tier}, test-writer ${testWriter.model}/${testWriter.effort}, implementer ${startLevel.model}/${startLevel.effort} escalating once to ${escalatedLevel.model}/${escalatedLevel.effort}, reviewer opus/${reviewEffort} then opus/medium re-reviews, max ${maxRounds} review fix rounds${skipped.size ? `, skipping: ${[...skipped].join(', ')}` : ''}`,
)

// ---------------------------------------------------------------- tests

if (!e2e) phase('Tests')

if (e2e) {
  log('Tests skipped - an e2e part writes its tests through the e2e agent')
} else if (noTests) {
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

phase(e2e ? 'E2E' : 'Implement')
if (broke()) return stop('implement', { reason: 'budget' })

const implementLines = tests.tests.length
  ? [
      `Make these failing tests pass: ${JSON.stringify(tests)}`,
      "Start from the spec's Code map and the tests' contextFiles - read those instead of rediscovering the code. Then only the skarbiec-plan sections the spec names. Smallest correct change; do not widen scope beyond the spec.",
    ]
  : [
      'This spec has no new tests - it adds no behaviour. Implement its Scope exactly and leave every existing suite green.',
      "Start from the spec's Code map, then only the skarbiec-plan sections it names. Smallest correct change; do not widen scope beyond the spec.",
      'Run the command each acceptance criterion names as its proof, and report those commands in `commandsRun`.',
    ]

const initial = e2e
  ? await attempt('e2e: automate the epic plan and run the suite', [], e2eRun, e2eFailed)
  : await attempt('implement the spec', implementLines)

if (!initial.impl) return stop('implement', initial)
impl = initial.impl
if (e2e && impl.status === 'blocked') return e2eStop(impl)

log(e2e ? `e2e agent touched ${impl.filesTouched.length} file(s), verify and the whole e2e suite green` : `implementer touched ${impl.filesTouched.length} file(s), verify green`)

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
            e2e ? null : `tests claimed: ${JSON.stringify(tests.tests)}`,
            `${e2e ? 'e2e agent' : 'implementer'} report: ${JSON.stringify(impl)}`,
            e2e ? 'This is an epic\'s e2e part (the `e2e` label): there is no test-writer and no implementer. Apply the "An e2e part" rules of your agent prompt; the epic body and the sibling parts are in `skarbiec-plan/issues/`, and `.git/e2e-result.json` holds the run.' : null,
            deviations.length ? `deviations from the spec or the tests, all rounds: ${JSON.stringify(deviations)} - judge each on its why.` : null,
            e2e || tests.tests.length ? null : 'This spec carries the `skip-tests` label - no new tests were written. Judge each acceptance criterion by the command it names and confirm the existing suites still cover the behaviour it touches.',
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

    phase(e2e ? 'E2E' : 'Implement')
    if (broke()) return stop('implement', { reason: 'budget' })

    rounds = rounds + 1
    const fixLines = [
      `Address exactly these blocking review findings: ${JSON.stringify(blocking)}`,
      e2e ? null : `tests: ${JSON.stringify(tests)}`,
      'Change only what the findings name. A finding you disagree with goes into notes with the reason - do not silently ignore it.',
    ]
    const fix = e2e
      ? await attempt(`address blocking findings (round ${round + 1})`, fixLines, e2eRun, e2eFailed)
      : await attempt(`address blocking findings (round ${round + 1})`, fixLines)
    if (!fix.impl) return stop('implement', fix)
    if (e2e && fix.impl.status === 'blocked') return e2eStop(fix.impl)
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
    e2e: e2e ? true : undefined,
    rounds,
    reviewRan: Boolean(review),
    minor: findingsOf(minor),
    deviations: compact().deviations,
    manualTest: [...manualTest.values()],
  }),
}
