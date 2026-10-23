type Listener = (event: { matches: boolean }) => void;

export interface MatchMediaStub {
  setDark(dark: boolean): void;
  restore(): void;
}

export function stubMatchMedia(initialDark: boolean): MatchMediaStub {
  let dark = initialDark;
  const listeners = new Set<Listener>();
  const original = Object.getOwnPropertyDescriptor(window, 'matchMedia');

  const matchMedia = (query: string) => ({
    get matches() {
      return query.includes('prefers-color-scheme: dark') ? dark : false;
    },
    media: query,
    addEventListener: (_type: string, listener: Listener) => listeners.add(listener),
    removeEventListener: (_type: string, listener: Listener) => listeners.delete(listener),
    addListener: (listener: Listener) => listeners.add(listener),
    removeListener: (listener: Listener) => listeners.delete(listener),
  });

  for (const target of [window, globalThis]) {
    Object.defineProperty(target, 'matchMedia', {
      value: matchMedia,
      configurable: true,
      writable: true,
    });
  }

  return {
    setDark(value: boolean) {
      dark = value;
      for (const listener of [...listeners]) {
        listener({ matches: value });
      }
    },
    restore() {
      if (original) {
        Object.defineProperty(window, 'matchMedia', original);
      } else {
        delete (window as unknown as Record<string, unknown>)['matchMedia'];
        delete (globalThis as unknown as Record<string, unknown>)['matchMedia'];
      }
    },
  };
}
