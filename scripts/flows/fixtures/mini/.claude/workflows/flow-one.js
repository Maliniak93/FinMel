export const meta = {
  name: 'flow-one',
  description: 'Fixture workflow with two phases',
  phases: [
    { title: 'Prepare', detail: 'get ready' },
    { title: 'Run', detail: 'do the work' },
  ],
}

const OPUS = 'claude-opus-5-5'
const startLevel =
  tier >= 2 ? { model: 'sonnet', effort: 'high' } : noTests ? { model: 'haiku', effort: 'high' } : { model: 'sonnet', effort: 'medium' }
const escalatedLevel = { model: OPUS, effort: tier >= 2 ? 'high' : 'medium' }

await step('worker', 'Do the work', ['go'])
