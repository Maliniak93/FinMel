const DURATION = /^(-?[\d.]+)(ms|s)$/;

function toMs(raw: string): number | null {
  const match = DURATION.exec(raw.trim());
  if (match === null) {
    return null;
  }
  const value = Number(match[1]);
  return match[2] === 's' ? value * 1000 : value;
}

function resolveThroughProbe(variable: string): number | null {
  const probe = document.createElement('span');
  probe.style.setProperty('transition-duration', `var(${variable})`);
  probe.style.display = 'none';
  document.body.appendChild(probe);
  const resolved = toMs(getComputedStyle(probe).transitionDuration.split(',')[0]);
  probe.remove();
  return resolved;
}

export function readMotionMs(variable: string, fallbackMs: number): number {
  const raw = getComputedStyle(document.documentElement).getPropertyValue(variable);
  return toMs(raw) ?? resolveThroughProbe(variable) ?? fallbackMs;
}
