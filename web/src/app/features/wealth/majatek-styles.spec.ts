import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

import { mediaBlock, readStylesheet, rulesFor } from '../../../testing/stylesheet';

const MAJATEK_DIRS = [
  'src/app/features/wealth',
  'src/app/features/accounts',
  'src/app/features/cash',
  'src/app/shared/summary-strip',
  'src/app/shared/gain',
];

function scssFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      return scssFiles(path);
    }
    return entry.name.endsWith('.scss') ? [path] : [];
  });
}

describe('Majątek stylesheets', () => {
  it('row menu icons are 16 px and the cash place heading takes its size from the section title', () => {
    const wealthAll = readStylesheet('src/app/features/wealth/wealth-all/wealth-all.scss');
    expect(rulesFor(wealthAll, /mat-icon/)).toMatch(/font-size:\s*var\(--sk-text-lg\)/);

    const cash = readStylesheet('src/app/features/cash/cash.scss');
    expect(rulesFor(cash, /cash-place__name/)).not.toMatch(/font-size|font-family/);
  });

  it('every wealth table is ruled with a Razem top rule and 16 px icons', () => {
    const shared = readStylesheet('src/app/shared/_ruled-table.scss');
    expect(rulesFor(shared, /header-cell/)).toMatch(/border-bottom:\s*1px solid/);
    expect(rulesFor(shared, /mat-mdc-cell/)).toMatch(/border-bottom:\s*1px solid/);
    expect(rulesFor(shared, /footer-cell/)).toMatch(/border-top:\s*1px solid/);
    expect(rulesFor(shared, /mat-icon/)).toMatch(/font-size:\s*var\(--sk-text-lg\)/);

    const tables = [
      'accounts/accounts.scss',
      'accounts/account-detail/account-detail.scss',
      'bank-accounts/bank-accounts.scss',
      'deposits/deposits.scss',
      'bonds/my-bonds/my-bonds.scss',
      'securities/securities-table/securities-table.scss',
      'metals/metals.scss',
      'transactions/transactions.scss',
    ];
    for (const table of tables) {
      expect(readStylesheet(`src/app/features/${table}`), table).toContain('ruled.ruled-table');
    }
  });

  it('uses outline-variant rules, spacing tokens and no colour literal', () => {
    for (const dir of MAJATEK_DIRS) {
      for (const file of scssFiles(resolve(process.cwd(), dir))) {
        const css = readFileSync(file, 'utf8');
        expect(css, file).not.toMatch(/#[0-9a-fA-F]{3,6}\b|rgb\(/);
        for (const [declaration] of css.matchAll(/border(?:-[a-z]+)?:\s*[^;]*solid[^;]*;/g)) {
          expect(declaration, file).toContain('var(--mat-sys-outline-variant)');
        }
      }
    }

    const strip = readStylesheet('src/app/shared/summary-strip/summary-strip.scss');
    expect(strip).toMatch(/var\(--sk-space-3\)/);
    expect(strip).toMatch(/var\(--sk-space-5\)/);

    const gain = readStylesheet('src/app/shared/gain/gain.scss');
    expect(gain).toMatch(/var\(--sk-space-2\)/);
  });

  it('summary strip cells are equal grid tracks with 14 px phone values', () => {
    const strip = readStylesheet('src/app/shared/summary-strip/summary-strip.scss');
    const list = rulesFor(strip, /summary-strip__list/);
    expect(list).toMatch(/display:\s*grid/);
    expect(list).toMatch(/grid-auto-flow:\s*column/);
    expect(list).toMatch(/grid-auto-columns:\s*minmax\(0,\s*1fr\)/);
    expect(rulesFor(strip, /summary-strip__item/)).not.toMatch(/\bflex\s*:/);

    const phone = mediaBlock(strip, '(width < 600px)');
    expect(rulesFor(phone, /summary-strip__value/)).toMatch(/font-size:\s*var\(--sk-text-base\)/);
    expect(rulesFor(phone, /summary-strip__label/)).toMatch(/font-size:\s*var\(--sk-text-xs\)/);
  });

  it('wealth tables keep name, value and menu on a phone', () => {
    const accounts = readStylesheet('src/app/features/accounts/accounts.scss');
    const accountsPhone = mediaBlock(accounts, '(width < 600px)');
    for (const column of ['type', 'wrapper', 'cash', 'securities']) {
      expect(rulesFor(accountsPhone, new RegExp(`mat-column-${column}\\b`)), column).toMatch(
        /display:\s*none/,
      );
    }
    for (const column of ['name', 'value', 'actions']) {
      expect(rulesFor(accountsPhone, new RegExp(`mat-column-${column}\\b`)), column).not.toMatch(
        /display:\s*none/,
      );
    }

    const wealthAll = readStylesheet('src/app/features/wealth/wealth-all/wealth-all.scss');
    const wealthPhone = mediaBlock(wealthAll, '(width < 600px)');
    for (const selector of [/wealth-all__table/, /\bthead\b/, /\btbody\b/]) {
      expect(rulesFor(wealthPhone, selector), String(selector)).toMatch(/display:\s*block/);
    }
    const rows = rulesFor(wealthPhone, /\btr\b|__row/);
    expect(rows).toMatch(/display:\s*grid/);
    expect(rows).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)\s+auto\s+48px/);
    expect(rulesFor(wealthPhone, /col--place/)).toMatch(/display:\s*none/);
    expect(rulesFor(wealthPhone, /col--group/)).toMatch(/display:\s*none/);
    expect(rulesFor(wealthPhone, /td\.wealth-all__col--status(?!:empty)/)).not.toMatch(
      /display:\s*none/,
    );
    expect(rulesFor(wealthPhone, /thead th\.wealth-all__col--status/)).toMatch(/display:\s*none/);
  });
});
