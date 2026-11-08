import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { BREAKPOINTS, belowQuery, fromQuery } from './breakpoints';

describe('breakpoints', () => {
  it('scss and ts breakpoints match', () => {
    const scss = readFileSync(resolve(process.cwd(), 'src/_breakpoints.scss'), 'utf8');
    const read = (name: string) =>
      Number(new RegExp(`\\$sk-bp-${name}:\\s*(\\d+)px`).exec(scss)?.[1]);

    expect([read('sm'), read('md'), read('lg')]).toEqual([600, 960, 1280]);
    expect(Object.values(BREAKPOINTS)).toEqual([600, 960, 1280]);
  });

  it('builds the from and below queries', () => {
    expect(fromQuery(BREAKPOINTS.sm)).toBe('(min-width: 600px)');
    expect(belowQuery(BREAKPOINTS.sm)).toBe('(max-width: 599.98px)');
    expect(fromQuery(BREAKPOINTS.lg)).toBe('(min-width: 1280px)');
  });
});
