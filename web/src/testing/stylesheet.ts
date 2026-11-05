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
