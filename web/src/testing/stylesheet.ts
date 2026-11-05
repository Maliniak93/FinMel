import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

export function readStylesheet(path: string): string {
  return readFileSync(resolve(process.cwd(), path), 'utf8');
}

export function rulesFor(css: string, selector: RegExp): string {
  return Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g))
    .filter((match) => selector.test(match[1]))
    .map((match) => match[2])
    .join('\n');
}

export function mediaBlock(css: string, condition: string): string {
  const start = css.indexOf(`@media ${condition}`);
  if (start === -1) {
    throw new Error(`no @media ${condition} block`);
  }
  const open = css.indexOf('{', start);
  let depth = 0;
  for (let i = open; i < css.length; i++) {
    if (css[i] === '{') {
      depth++;
    } else if (css[i] === '}') {
      depth--;
      if (depth === 0) {
        return css.slice(open + 1, i);
      }
    }
  }
  throw new Error(`unclosed @media ${condition} block`);
}
