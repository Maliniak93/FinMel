// Node's own experimental Web Storage global (present without any flag on this project's Node
// floor, v26 — see `.claude` memory "T1.9 frontend env gotchas") collides with Vitest's jsdom
// test environment: jsdom implements a real, working `localStorage`, but Vitest's environment
// setup only copies a jsdom `window` property onto `globalThis` when that name isn't already
// present there. Node's own accessor already occupies the name and — without a
// `--localstorage-file` — resolves to `undefined` (with a one-time `ExperimentalWarning`), so
// jsdom's copy is silently skipped. Net effect: any test that touches `localStorage` fails with
// "Cannot read properties of undefined (reading '...')", in a real browser (where the app
// actually runs) `localStorage` works fine — this is a test-environment-only gap.
//
// Not specific to any one feature: `ThemeService` (M1.10) is simply the first thing in this repo
// to use `localStorage` at all. Fixed once, here, for every future test.
class MemoryStorage implements Storage {
  private readonly store = new Map<string, string>();

  get length(): number {
    return this.store.size;
  }

  clear(): void {
    this.store.clear();
  }

  getItem(key: string): string | null {
    return this.store.has(key) ? (this.store.get(key) ?? null) : null;
  }

  key(index: number): string | null {
    return Array.from(this.store.keys())[index] ?? null;
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }
}

for (const key of ['localStorage', 'sessionStorage'] as const) {
  Object.defineProperty(globalThis, key, {
    value: new MemoryStorage(),
    configurable: true,
    writable: true,
  });
}
