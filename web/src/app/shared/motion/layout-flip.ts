import { readMotionMs } from './motion-duration';

const DURATION_VARIABLE = '--sk-motion-duration-long';
const FALLBACK_DURATION_MS = 420;
const FALLBACK_EASING = 'cubic-bezier(0.35, 0, 0.25, 1)';

export interface LayoutFlip {
  record(elements: readonly HTMLElement[]): void;
  play(): void;
  cancel(): void;
}

function readEasing(): string {
  const raw = getComputedStyle(document.documentElement).getPropertyValue('--sk-motion-easing');
  return raw.trim() || FALLBACK_EASING;
}

export function createLayoutFlip(): LayoutFlip {
  let running: Animation[] = [];
  let first = new Map<HTMLElement, DOMRect>();

  const cancel = (): void => {
    const cancelled = running;
    running = [];
    cancelled.forEach((animation) => animation.cancel());
  };

  return {
    cancel,
    record(elements) {
      first = new Map(elements.map((element) => [element, element.getBoundingClientRect()]));
      cancel();
    },
    play() {
      const recorded = first;
      first = new Map();
      const duration = readMotionMs(DURATION_VARIABLE, FALLBACK_DURATION_MS);
      const easing = readEasing();
      for (const [element, before] of recorded) {
        const after = element.getBoundingClientRect();
        const dx = before.left - after.left;
        const dy = before.top - after.top;
        if (dx === 0 && dy === 0) {
          continue;
        }
        const animation = element.animate(
          [{ transform: `translate(${dx}px, ${dy}px)` }, { transform: 'none' }],
          { duration, easing },
        );
        running.push(animation);
        const forget = (): void => {
          running = running.filter((candidate) => candidate !== animation);
        };
        animation.finished.then(forget, forget);
      }
    },
  };
}
