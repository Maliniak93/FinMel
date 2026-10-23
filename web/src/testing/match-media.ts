type Listener = (event: { matches: boolean }) => void;

export interface MatchMediaStub {
  setDark(dark: boolean): void;
  restore(): void;
}

export interface ViewportStub {
  setWidth(width: number): void;
  restore(): void;
}

function widthMatches(query: string, width: number): boolean {
  const conditions = [...query.matchAll(/\((min|max)-width:\s*([\d.]+)px\)/g)];
  if (conditions.length === 0) {
    return false;
  }

  return conditions.every(([, kind, value]) =>
    kind === 'min' ? width >= Number(value) : width <= Number(value),
  );
}

export function stubViewport(initialWidth: number): ViewportStub {
  let width = initialWidth;
  const registered = new Set<{ query: string; listener: Listener; last: boolean }>();
  const original = Object.getOwnPropertyDescriptor(window, 'matchMedia');

  const matchMedia = (query: string) => {
    const add = (listener: Listener) =>
      registered.add({ query, listener, last: widthMatches(query, width) });
    const remove = (listener: Listener) => {
      for (const entry of [...registered]) {
        if (entry.listener === listener && entry.query === query) {
          registered.delete(entry);
        }
      }
    };

    return {
      get matches() {
        return widthMatches(query, width);
      },
      media: query,
      addEventListener: (_type: string, listener: Listener) => add(listener),
      removeEventListener: (_type: string, listener: Listener) => remove(listener),
      addListener: add,
      removeListener: remove,
      dispatchEvent: () => false,
      onchange: null,
    };
  };

  for (const target of [window, globalThis]) {
    Object.defineProperty(target, 'matchMedia', {
      value: matchMedia,
      configurable: true,
      writable: true,
    });
  }

  return {
    setWidth(value: number) {
      width = value;
      for (const entry of [...registered]) {
        const matches = widthMatches(entry.query, width);
        if (matches !== entry.last) {
          entry.last = matches;
          entry.listener({ matches });
        }
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
