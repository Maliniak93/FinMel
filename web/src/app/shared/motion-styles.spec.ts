import { readStylesheet } from '../../testing/stylesheet';

const NO_PREFERENCE = '@media (prefers-reduced-motion: no-preference)';

function splitMotion(css: string): { outside: string; inside: string } {
  let outside = '';
  let inside = '';
  let cursor = 0;
  for (;;) {
    const start = css.indexOf(NO_PREFERENCE, cursor);
    if (start === -1) {
      outside += css.slice(cursor);
      break;
    }
    outside += css.slice(cursor, start);
    const open = css.indexOf('{', start);
    let depth = 0;
    let end = open;
    for (; end < css.length; end++) {
      if (css[end] === '{') {
        depth++;
      } else if (css[end] === '}') {
        depth--;
        if (depth === 0) {
          break;
        }
      }
    }
    inside += css.slice(open + 1, end);
    cursor = end + 1;
  }
  return { outside, inside };
}

describe('Motion styles', () => {
  it('transitions only under no-preference', () => {
    const files = [
      'src/app/layout/shell/shell.scss',
      'src/app/features/dashboard/net-worth-chart/net-worth-chart.scss',
      'src/app/shared/collapse/collapse.scss',
      'src/app/features/wealth/wealth-all/wealth-all.scss',
    ];
    for (const file of files) {
      const { outside, inside } = splitMotion(readStylesheet(file));
      expect(outside, file).not.toMatch(/(^|[\s;{])(transition|animation)(-[a-z]+)?\s*:/);
      expect(inside, file).toContain('var(--sk-motion-duration)');
      if (!file.includes('wealth-all')) {
        expect(inside, file).toMatch(/(^|[\s;{])transition\s*:/);
      }
    }
  });
});
