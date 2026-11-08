import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

function htmlFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      return htmlFiles(path);
    }
    return entry.name.endsWith('.html') ? [path] : [];
  });
}

function scssFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      return scssFiles(path);
    }
    return entry.name.endsWith('.scss') ? [path] : [];
  });
}

const FEATURES = resolve(process.cwd(), 'src/app/features');
const PAGES = [
  'dashboard/dashboard.html',
  'wealth/wealth-all/wealth-all.html',
  'accounts/accounts.html',
  'bank-accounts/bank-accounts.html',
  'cash/cash.html',
  'deposits/deposits.html',
  'securities/securities.html',
  'bonds/bonds.html',
  'metals/metals.html',
  'accounts/account-detail/account-detail.html',
  'transactions/transactions.html',
  'groups/groups.html',
  'planning/planning.html',
  'settings/settings.html',
];

describe('page headers', () => {
  it('no feature template has its own h1', () => {
    const outside = /[\\/](auth|not-found)[\\/]/;
    for (const file of htmlFiles(FEATURES).filter((path) => !outside.test(path))) {
      expect(readFileSync(file, 'utf8'), file).not.toMatch(/<h1[\s>]/);
    }
  });

  it('every signed-in page template uses app-page-header', () => {
    for (const page of PAGES) {
      expect(readFileSync(join(FEATURES, page), 'utf8'), page).toContain('<app-page-header');
    }
  });

  it('the wealth layout has the tab bar and no heading', () => {
    const html = readFileSync(join(FEATURES, 'wealth/wealth-layout.html'), 'utf8');

    expect(html).not.toMatch(/<h1[\s>]/);
    expect(html).not.toContain('wealth__title');
  });

  it('the per-card title classes are gone from templates and stylesheets', () => {
    const old =
      /dashboard-page__card-title|dashboard-page__todo-head|accounts-card__title|cash-card__title|month-card__title|upcoming-card__title|todo-card__title/;
    const root = resolve(process.cwd(), 'src/app');
    for (const file of [...htmlFiles(root), ...scssFiles(root)]) {
      expect(readFileSync(file, 'utf8'), file).not.toMatch(old);
    }
  });

  it('the dashboard cards, cash places and assets panel use sk-section-title', () => {
    const dashboard = join(FEATURES, 'dashboard');
    const html = (path: string) => readFileSync(join(dashboard, path), 'utf8');

    expect(html('dashboard.html')).toContain('sk-section-title');
    for (const card of ['upcoming', 'todo', 'accounts', 'cash', 'month']) {
      expect(html(`${card}-card/${card}-card.html`), card).toContain('sk-section-title');
    }
    expect(readFileSync(join(FEATURES, 'cash/cash.html'), 'utf8')).toContain('sk-section-title');
    expect(readFileSync(join(FEATURES, 'planning/planning.html'), 'utf8')).toContain(
      'sk-section-title',
    );
  });

  it('every h2 of the section panels carries sk-section-title', () => {
    const panels = [
      'groups/group-assets-panel/group-assets-panel.html',
      'accounts/account-detail/account-detail.html',
      'settings/settings.html',
      'settings/contribution-limits/contribution-limits.html',
      'securities/watchlist-table/watchlist-table.html',
    ];
    for (const panel of panels) {
      const headings = readFileSync(join(FEATURES, panel), 'utf8').match(/<h2[\s>][^>]*>?/g) ?? [];

      expect(headings.length, panel).toBeGreaterThan(0);
      for (const heading of headings) {
        expect(heading, panel).toContain('sk-section-title');
      }
    }
  });
});
