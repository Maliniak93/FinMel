import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

import { readStylesheet, rulesFor } from '../../../testing/stylesheet';

const MAJATEK_DIRS = [
  'src/app/features/wealth',
  'src/app/features/accounts',
  'src/app/features/cash',
  'src/app/shared/summary-strip',
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
  it('row menu icons are 16 px and the cash place heading is 22 px', () => {
    const wealthAll = readStylesheet('src/app/features/wealth/wealth-all/wealth-all.scss');
    expect(rulesFor(wealthAll, /mat-icon/)).toMatch(/font-size:\s*16px/);

    const cash = readStylesheet('src/app/features/cash/cash.scss');
    expect(rulesFor(cash, /cash-place__name/)).toMatch(/font-size:\s*22px/);
  });

  it('every wealth table is ruled with a Razem top rule and 16 px icons', () => {
    const shared = readStylesheet('src/app/shared/_ruled-table.scss');
    expect(rulesFor(shared, /header-cell/)).toMatch(/border-bottom:\s*1px solid/);
    expect(rulesFor(shared, /mat-mdc-cell/)).toMatch(/border-bottom:\s*1px solid/);
    expect(rulesFor(shared, /footer-cell/)).toMatch(/border-top:\s*1px solid/);
    expect(rulesFor(shared, /mat-icon/)).toMatch(/font-size:\s*16px/);

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
    expect(strip).toMatch(/var\(--sk-space-4\)/);
  });
});
