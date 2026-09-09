// Learn more about Vitest configuration options at https://vitest.dev/config/
// The Angular CLI overrides certain properties (e.g. `test.projects`, `test.include`) to ensure
// proper integration — don't set those here.

import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // These are jsdom + Angular Material overlay/dialog specs running under `isolate: false`
    // (every spec file sharing one worker process, this project's Angular-CLI default) — under
    // contention that's a machine-speed budget, not a behavioural one, and a genuinely hung test
    // still fails, just later. Vitest's 5000ms default was measured tipping over on the slowest
    // Material-dialog specs once the suite grew past ~22 files (confirmed via a controlled
    // `git stash` A/B on the same machine, same minute: with vs. without a 23rd spec file, all
    // else equal). 20s gives real headroom without the timeout becoming meaningless.
    testTimeout: 20000,
  },
});
