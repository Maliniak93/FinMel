import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const GROUP_KEYS = [
  'blue',
  'green',
  'orange',
  'purple',
  'red',
  'teal',
  'pink',
  'amber',
  'indigo',
  'lime',
  'cyan',
  'brown',
];

function read(path: string): string {
  return readFileSync(resolve(process.cwd(), path), 'utf8');
}

function darkBlock(tokens: string): string {
  const start = tokens.search(/\[data-theme=["']dark["']\]/);
  expect(start).toBeGreaterThanOrEqual(0);
  return tokens.slice(start, tokens.indexOf('}', start));
}

function ruleBodies(text: string, selector: string): string[] {
  const bodies: string[] = [];
  let at = text.indexOf(selector);
  while (at >= 0) {
    const open = text.indexOf('{', at);
    let depth = 0;
    let close = -1;
    for (let i = open; i >= 0 && i < text.length && close < 0; i++) {
      if (text[i] === '{') depth++;
      if (text[i] === '}' && --depth === 0) close = i;
    }
    if (close < 0) break;
    bodies.push(text.slice(open, close + 1));
    at = text.indexOf(selector, close);
  }
  return bodies;
}

describe('global styles', () => {
  it('loads the tokens after mat.theme', () => {
    const styles = read('src/styles.scss');
    const angularJson = JSON.parse(read('angular.json')) as {
      projects: Record<string, { architect: { build: { options: { styles: string[] } } } }>;
    };
    const buildStyles = Object.values(angularJson.projects)[0].architect.build.options.styles;

    const themeAt = styles.indexOf('mat.theme(');
    expect(themeAt).toBeGreaterThanOrEqual(0);
    expect(styles).toContain('mat.$orange-palette');

    const importedAt = styles.search(/@(use|import|forward)\s+['"][^'"]*skarbiec-tokens/);
    if (importedAt >= 0) {
      expect(importedAt).toBeGreaterThan(themeAt);
    } else {
      const stylesIndex = buildStyles.indexOf('src/styles.scss');
      const tokensIndex = buildStyles.findIndex((entry) => entry.includes('skarbiec-tokens.css'));
      expect(stylesIndex).toBeGreaterThanOrEqual(0);
      expect(tokensIndex).toBeGreaterThan(stylesIndex);
    }
  });

  it('tokens define every group colour', () => {
    const tokens = read('src/skarbiec-tokens.css');
    const dark = darkBlock(tokens);

    for (const key of GROUP_KEYS) {
      expect(tokens, `--sk-g-${key}`).toMatch(new RegExp(`--sk-g-${key}:`));
      expect(dark, `dark --sk-g-${key}`).toMatch(new RegExp(`--sk-g-${key}:`));
    }
    expect(tokens).toMatch(/--sk-c-bank:/);
    expect(tokens).toMatch(/--sk-c-cash:/);
    expect(tokens).toMatch(/--sk-c-stock:/);
    expect(tokens).toContain('.sk-num');
  });

  it('global overrides and tabular figures', () => {
    const styles = read('src/styles.scss');

    expect(styles).toContain('mat-flat-button');
    expect(styles).toContain('mat-stroked-button');
    expect(styles).toContain('--sk-accent-stroke');
    expect(styles).toContain('--mat-sys-outline-variant');
    expect(styles).toMatch(/font-variant-numeric:\s*tabular-nums lining-nums/);
    expect(styles).toContain('.material-symbols-outlined');
    expect(styles).toMatch(/font-display:\s*block/);
    expect(styles).toMatch(/progress-bar/);
    expect(styles).toMatch(/focus-visible|outline-offset/);
  });

  it('tokens define the spacing and radius scale', () => {
    const tokens = read('src/skarbiec-tokens.css');

    expect(tokens).toMatch(/--sk-space-2:\s*9\.2px;/);
    expect(tokens).toMatch(/--sk-space-3:\s*13\.8px;/);
    expect(tokens).toMatch(/--sk-space-4:\s*18\.4px;/);
    expect(tokens).toMatch(/--sk-space-6:\s*27\.6px;/);
    expect(tokens).toMatch(/--sk-space-8:\s*36\.8px;/);
    expect(tokens).toMatch(/--sk-radius-md:\s*4px;/);
    expect(tokens).toMatch(/--sk-radius-lg:\s*7px;/);
  });

  it('the global h1 is headline-large with no margin', () => {
    const styles = read('src/styles.scss');
    const block = styles.match(/^h1\s*\{[^}]*\}/m)?.[0] ?? '';

    expect(block).toMatch(/--mat-sys-headline-large/);
    expect(block).toMatch(/margin:\s*0\b/);
  });

  it('rounds the outer corners of the first and last button toggle with the group radius', () => {
    const styles = read('src/styles.scss');
    const innerRadius =
      'calc(var(--mat-button-toggle-shape, var(--mat-sys-corner-extra-large)) - 1px)';
    const groupSelector =
      '.mat-button-toggle-group-appearance-standard:not(.mat-button-toggle-vertical)';
    const groupRules = ruleBodies(styles, groupSelector)
      .join('\n')
      .replace(/\s+/g, ' ')
      .replace(/\( /g, '(')
      .replace(/ \)/g, ')');

    expect(styles).toMatch(
      /\.mat-button-toggle-checked\s*\{[^}]*inset 0 0 0 1px var\(--sk-accent-stroke\)/,
    );
    expect(groupRules).toContain('mat-button-toggle-checked');
    expect(groupRules).toContain(':first-child');
    expect(groupRules).toContain(':last-child');
    for (const corner of [
      'border-start-start-radius',
      'border-end-start-radius',
      'border-start-end-radius',
      'border-end-end-radius',
    ]) {
      expect(groupRules, corner).toContain(`${corner}: ${innerRadius}`);
    }
    expect(groupRules).not.toMatch(/\bborder-radius\s*:/);
  });
});
