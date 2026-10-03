import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // Material dialog specs share one worker (isolate: false) and exceed Vitest's 5 s default under load.
    testTimeout: 20000,
  },
});
