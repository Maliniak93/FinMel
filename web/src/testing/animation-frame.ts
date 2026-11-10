import { vi } from 'vitest';

const FRAME_MS = 16;
const FLUSH_LIMIT_MS = 10_000;

export interface AnimationFrames {
  now(): number;
  pending(): number;
  advance(ms: number): void;
  flush(): void;
  restore(): void;
}

export function stubAnimationFrames(): AnimationFrames {
  let time = 0;
  let nextId = 1;
  let callbacks = new Map<number, FrameRequestCallback>();

  const nowSpy = vi.spyOn(performance, 'now').mockImplementation(() => time);
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    const id = nextId++;
    callbacks.set(id, callback);
    return id;
  });
  vi.stubGlobal('cancelAnimationFrame', (id: number) => {
    callbacks.delete(id);
  });

  const step = (ms: number): void => {
    time += ms;
    const due = [...callbacks.values()];
    callbacks = new Map();
    for (const callback of due) {
      callback(time);
    }
  };

  return {
    now: () => time,
    pending: () => callbacks.size,
    advance(ms: number): void {
      const target = time + ms;
      while (time < target) {
        step(Math.min(FRAME_MS, target - time));
      }
    },
    flush(): void {
      const limit = time + FLUSH_LIMIT_MS;
      while (callbacks.size > 0 && time < limit) {
        step(FRAME_MS);
      }
      if (callbacks.size > 0) {
        throw new Error('Animation frames did not settle.');
      }
    },
    restore(): void {
      callbacks = new Map();
      nowSpy.mockRestore();
    },
  };
}
