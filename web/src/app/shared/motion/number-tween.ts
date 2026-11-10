export interface NumberTween {
  running(): boolean;
  start(durationMs: number, onProgress: (eased: number) => void, onDone: () => void): void;
  cancel(): void;
}

function easeOut(progress: number): number {
  return 1 - Math.pow(1 - progress, 3);
}

export function createNumberTween(): NumberTween {
  let frame: number | null = null;

  const cancel = (): void => {
    if (frame !== null) {
      cancelAnimationFrame(frame);
      frame = null;
    }
  };

  return {
    running: () => frame !== null,
    cancel,
    start(durationMs, onProgress, onDone) {
      cancel();
      const startedAt = performance.now();
      const tick = (): void => {
        const progress = Math.min((performance.now() - startedAt) / durationMs, 1);
        if (progress >= 1) {
          frame = null;
          onDone();
          return;
        }
        onProgress(easeOut(progress));
        frame = requestAnimationFrame(tick);
      };
      frame = requestAnimationFrame(tick);
    },
  };
}
