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
});
