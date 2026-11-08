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

export function ruleBodies(css: string, selector: string): string {
  const pattern = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return Array.from(
    css.matchAll(new RegExp(`(?:^|[\\s,])${pattern}\\s*(?:,[^{]*)?\\{([^}]*)`, 'g')),
  )
    .map((match) => match[1])
    .join('\n');
}

export function rowLineSkipsLast(css: string, row: string): boolean {
  const hasLine = (body: string) =>
    /border-bottom:\s*1px solid var\(--mat-sys-outline-variant\)/.test(body);
  const resetsLine = (body: string) => /border-bottom:\s*(none|0)\b/.test(body);
  const everyRow = ruleBodies(css, row);
  const betweenRows = ruleBodies(css, `${row}:not(:last-child)`);
  const lastRow = ruleBodies(css, `${row}:last-child`);
  return (hasLine(betweenRows) && !hasLine(everyRow)) || (hasLine(everyRow) && resetsLine(lastRow));
}

const BREAKPOINT_NAMES: Record<string, string> = { '600': 'sm', '960': 'md', '1280': 'lg' };

function mixinCall(condition: string): RegExp | null {
  const parsed = /^\(width\s*(<|>=)\s*(\d+)px\)$/.exec(condition);
  const name = parsed ? BREAKPOINT_NAMES[parsed[2]] : undefined;
  if (!parsed || !name) {
    return null;
  }
  const mixin = parsed[1] === '<' ? 'sk-below' : 'sk-from';
  return new RegExp(
    `@include\\s+${mixin}\\(\\s*(?:\\$sk-bp-${name}|${parsed[2]}(?:px)?|${name})\\s*\\)`,
  );
}

export function mediaBlock(css: string, condition: string): string {
  const call = mixinCall(condition);
  const found = call ? call.exec(css) : null;
  const start = found ? found.index : css.indexOf(`@media ${condition}`);
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
