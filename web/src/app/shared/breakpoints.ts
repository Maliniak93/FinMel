export const BREAKPOINTS = { sm: 600, md: 960, lg: 1280 } as const;

export function fromQuery(breakpoint: number): string {
  return `(min-width: ${breakpoint}px)`;
}

export function belowQuery(breakpoint: number): string {
  return `(max-width: ${Number((breakpoint - 0.02).toFixed(2))}px)`;
}

export const PHONE_QUERY = belowQuery(BREAKPOINTS.sm);
