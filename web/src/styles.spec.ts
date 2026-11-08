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

function hexOf(block: string, name: string): string {
  const value = block.match(new RegExp(`${name}:\\s*(#[0-9a-f]{6})\\b`, 'i'))?.[1];
  expect(value, name).toBeDefined();
  return value!;
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((at) => {
    const channel = parseInt(hex.slice(at, at + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrastRatio(a: string, b: string): number {
  const [lighter, darker] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (lighter + 0.05) / (darker + 0.05);
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

  it('defines the 4px spacing grid', () => {
    const tokens = read('src/skarbiec-tokens.css');
    const spacing = Array.from(tokens.matchAll(/--sk-space-(\d+):\s*([^;]+);/g));

    expect(spacing.map(([, step]) => Number(step))).toEqual(
      Array.from({ length: 12 }, (_, index) => index + 1),
    );
    for (const [, step, value] of spacing) {
      expect(value, `--sk-space-${step}`).toBe(`${Number(step) * 4}px`);
    }
  });

  it('tokens define the radius scale', () => {
    const tokens = read('src/skarbiec-tokens.css');

    expect(tokens).toMatch(/--sk-radius-md:\s*4px;/);
    expect(tokens).toMatch(/--sk-radius-lg:\s*7px;/);
  });

  it('the global h1 is headline-large with no margin', () => {
    const styles = read('src/styles.scss');
    const block = styles.match(/^h1\s*\{[^}]*\}/m)?.[0] ?? '';

    expect(block).toMatch(/--mat-sys-headline-large/);
    expect(block).toMatch(/margin:\s*0\b/);
  });

  it('section titles are 24 px and sub titles 20 px, both weight 600', () => {
    const styles = read('src/styles.scss');
    const main = ruleBodies(styles, '.sk-section-title ')[0] ?? '';
    const sub = ruleBodies(styles, '.sk-section-title--sub')[0] ?? '';

    expect(main).toMatch(/font-size:\s*var\(--sk-text-2xl\)/);
    expect(main).toMatch(/font-weight:\s*600/);
    expect(sub).toMatch(/font-size:\s*var\(--sk-text-xl\)/);
    expect(sub).toMatch(/font-weight:\s*600/);
  });

  it('defines the text scale', () => {
    const tokens = read('src/skarbiec-tokens.css');
    const scale = { xs: 12, sm: 13, base: 14, lg: 16, xl: 20, '2xl': 24, '3xl': 40 };

    for (const [name, size] of Object.entries(scale)) {
      expect(tokens, `--sk-text-${name}`).toMatch(
        new RegExp(`--sk-text-${name}:\\s*${size}px\\s*;`),
      );
    }
  });

  it('typography roles use the scale and stay at or above 12px', () => {
    const tokens = read('src/skarbiec-tokens.css');
    const scale: Record<string, number> = {
      xs: 12,
      sm: 13,
      base: 14,
      lg: 16,
      xl: 20,
      '2xl': 24,
      '3xl': 40,
    };
    const roles = Array.from(
      tokens.matchAll(
        /--mat-sys-((?:display|headline|title|body|label)-(?:large|medium|small)):\s*([^;]+);/g,
      ),
    );

    expect(roles).toHaveLength(11);
    for (const [, role, value] of roles) {
      const token = value.match(/\bvar\(--sk-text-(xs|sm|base|lg|xl|2xl|3xl)\)/)?.[1];
      expect(token, `${role}: ${value}`).toBeDefined();
      expect(scale[token!], role).toBeGreaterThanOrEqual(12);
    }
  });

  it('material component overrides stay on the scale', () => {
    const styles = read('src/styles.scss');
    const tabs = styles.match(/mat\.tabs-overrides\(([\s\S]*?)\n\s*\);/)?.[1] ?? '';

    expect(tabs).toMatch(/label-text-size:\s*var\(--sk-text-lg\)/);
    const sizeOverrides = Array.from(
      styles.matchAll(/^\s*[\w-]*(?:text-size|font-size)[\w-]*:\s*([^,;\n]+)/gm),
    ).map((match) => match[1].trim());
    expect(sizeOverrides.length).toBeGreaterThan(0);
    for (const value of sizeOverrides) {
      expect(value, value).toMatch(/^var\(--sk-text-(xs|sm|base|lg|xl|2xl|3xl)\)$/);
    }
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

describe('todo block on the overdue tint', () => {
  const TEXT_ON_TINT = [
    '--mat-sys-on-surface',
    '--mat-sys-on-surface-variant',
    '--mat-sys-primary',
  ];

  it('todo block text keeps AA contrast on the overdue tint in both themes', () => {
    const tokens = read('src/skarbiec-tokens.css');
    const todoStyles = read('src/app/features/dashboard/todo-card/todo-card.scss');
    const tinted = ruleBodies(todoStyles, '.todo-card--overdue')[0] ?? '';
    const light = tokens.slice(tokens.indexOf(':root'), tokens.indexOf('}'));

    expect(tinted).toMatch(/background(-color)?:\s*var\(--mat-sys-surface-container-low\)/);
    for (const [theme, block] of [
      ['light', light],
      ['dark', darkBlock(tokens)],
    ] as const) {
      const tint = hexOf(block, '--mat-sys-surface-container-low');
      for (const name of TEXT_ON_TINT) {
        expect(contrastRatio(hexOf(block, name), tint), `${theme} ${name}`).toBeGreaterThanOrEqual(
          4.5,
        );
      }
    }
  });
});
