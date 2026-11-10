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

export function nestedRuleBodies(css: string, selector: RegExp): string {
  const rules: { selector: string; body: string }[] = [];
  const stack: { selector: string; body: string }[] = [];
  let text = '';
  for (const char of css) {
    if (char === '{') {
      const local = text.trim();
      const parent = stack.length ? stack[stack.length - 1].selector : '';
      const full = local.includes('&')
        ? local.replaceAll('&', parent)
        : parent
          ? `${parent} ${local}`
          : local;
      stack.push({ selector: full, body: '' });
      text = '';
    } else if (char === ';') {
      if (stack.length) {
        stack[stack.length - 1].body += `${text};`;
      }
      text = '';
    } else if (char === '}') {
      const rule = stack.pop();
      if (rule) {
        rules.push({ selector: rule.selector, body: rule.body + text });
      }
      text = '';
    } else {
      text += char;
    }
  }
  return rules
    .filter((rule) => selector.test(rule.selector))
    .map((rule) => rule.body)
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

export function allMediaBlocks(css: string, condition: string): string {
  const blocks: string[] = [];
  let from = 0;
  for (let start = css.indexOf(`@media ${condition}`, from); start !== -1;) {
    const open = css.indexOf('{', start);
    let depth = 0;
    let close = -1;
    for (let i = open; i < css.length && close === -1; i++) {
      if (css[i] === '{') {
        depth++;
      } else if (css[i] === '}' && --depth === 0) {
        close = i;
      }
    }
    if (close === -1) {
      throw new Error(`unclosed @media ${condition} block`);
    }
    blocks.push(css.slice(open + 1, close));
    from = close + 1;
    start = css.indexOf(`@media ${condition}`, from);
  }
  return blocks.join('\n');
}

type Delimiter = ';' | ',' | 'space';

function splitTopLevel(value: string, delimiter: Delimiter): string[] {
  const parts: string[] = [];
  let depth = 0;
  let current = '';
  for (const char of value) {
    if (char === '(') {
      depth++;
    } else if (char === ')') {
      depth--;
    }
    const splits = depth === 0 && (delimiter === 'space' ? /\s/.test(char) : char === delimiter);
    if (splits) {
      if (current.trim()) {
        parts.push(current.trim());
      }
      current = '';
    } else {
      current += char;
    }
  }
  if (current.trim()) {
    parts.push(current.trim());
  }
  return parts;
}

export function declarationList(body: string): [string, string][] {
  return splitTopLevel(body, ';').flatMap((declaration): [string, string][] => {
    const colon = declaration.indexOf(':');
    return colon === -1
      ? []
      : [[declaration.slice(0, colon).trim(), declaration.slice(colon + 1).trim()]];
  });
}

export function transitionsOf(body: string): { property: string; timing: string }[] {
  return declarationList(body)
    .filter(([name]) => name === 'transition')
    .flatMap(([, value]) =>
      splitTopLevel(value, ',').map((item) => {
        const [property = '', ...timing] = splitTopLevel(item, 'space');
        return { property, timing: timing.join(' ') };
      }),
    );
}

export type Corner = 'top-left' | 'top-right' | 'bottom-right' | 'bottom-left';

export function cornerRadii(body: string): Partial<Record<Corner, string>> {
  const radii: Partial<Record<Corner, string>> = {};
  for (const [name, value] of declarationList(body)) {
    if (name === 'border-radius') {
      const [top = '', right = top, bottom = top, left = right] = splitTopLevel(value, 'space');
      radii['top-left'] = top;
      radii['top-right'] = right;
      radii['bottom-right'] = bottom;
      radii['bottom-left'] = left;
    }
    const longhand = /^border-(top|bottom)-(left|right)-radius$/.exec(name);
    if (longhand) {
      radii[`${longhand[1]}-${longhand[2]}` as Corner] = value;
    }
  }
  return radii;
}
