import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { provideRouter, Router } from '@angular/router';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import {
  jsonResponse,
  requestUrl,
} from '../../features/assets/asset-form/testing/asset-form-fixtures';
import {
  labelsOf,
  looksLikeTranslationKey,
  openLanguageMenu,
  pickLanguageFromMenu,
  textOf,
} from '../../../testing/i18n';
import { stubViewport, type ViewportStub } from '../../../testing/match-media';
import { AuthService } from '../../core/auth/auth';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { THEME_STORAGE_KEY, ThemeService } from '../../core/theme/theme';
import { Shell } from './shell';

type LayoutName = 'full' | 'rail' | 'bottom';

const WIDTHS: Record<LayoutName, number> = { full: 1280, rail: 1000, bottom: 600 };
const LAYOUTS: LayoutName[] = ['full', 'rail', 'bottom'];
const ASSET_PATHS = [
  'accounts',
  'bank-accounts',
  'cash',
  'deposits',
  'bonds',
  'securities',
  'metals',
];

function todoItem(name: string): Record<string, unknown> {
  return {
    kind: 'OverdueIncome',
    name,
    date: '2026-11-10',
    month: '2026-11',
    amount: 800,
    currency: 'PLN',
    daysOverdue: 5,
  };
}

let viewport: ViewportStub;
let fetchItems: Record<string, unknown>[] = [];

async function mount(layout: LayoutName): Promise<ComponentFixture<Shell>> {
  viewport = stubViewport(WIDTHS[layout]);
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) =>
    requestUrl(input).includes('/planning/todo')
      ? jsonResponse({ items: fetchItems, paymentLeadDays: 7 })
      : jsonResponse({ detail: 'Not found.' }, 404),
  );
  await TestBed.configureTestingModule({
    imports: [Shell],
    providers: [
      provideRouter([{ path: '**', children: [] }]),
      provideI18nTesting(),
      { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
    ],
  }).compileComponents();
  const fixture = TestBed.createComponent(Shell);
  await settle(fixture);

  return fixture;
}

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

function root(fixture: ComponentFixture<unknown>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function buttonByLabel(scope: ParentNode, label: string, exact = true): HTMLButtonElement | null {
  return (
    Array.from(scope.querySelectorAll<HTMLButtonElement>('button[aria-label]')).find((button) => {
      const value = button.getAttribute('aria-label')!;

      return exact ? value === label : value.startsWith(label);
    }) ?? null
  );
}

function moreButton(element: HTMLElement): HTMLButtonElement {
  return Array.from(
    element.querySelectorAll<HTMLButtonElement>('[data-testid="bottom-nav"] button'),
  ).find((button) => textOf(button).includes('More'))!;
}

function linkHrefs(scope: ParentNode): (string | null)[] {
  return Array.from(scope.querySelectorAll('a'), (a) => a.getAttribute('href'));
}

beforeEach(() => {
  localStorage.clear();
  fetchItems = [];
});

afterEach(async () => {
  vi.restoreAllMocks();
  viewport?.restore();
  await TestBed.inject(LanguageService).setLanguage('en');
  localStorage.clear();
  document.documentElement.lang = 'en';
  document.documentElement.removeAttribute('data-theme');
});

describe.each(LAYOUTS)('Shell planning badge (%s layout)', (layout) => {
  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  function badgeOf(element: HTMLElement): Element | null {
    return element
      .querySelector('a[href="/planning"]')!
      .querySelector('[data-testid="todo-badge"]');
  }

  it('shows the todo count as a badge at Planning when it is above zero', async () => {
    fetchItems = [todoItem('800+'), todoItem('Wypłata żona'), todoItem('Premia')];
    const fixture = await mount(layout);

    const badge = badgeOf(root(fixture));
    expect(badge).not.toBeNull();
    expect(textOf(badge)).toBe('3');
  });

  it('shows no badge at Planning when the todo list is empty', async () => {
    const fixture = await mount(layout);

    expect(badgeOf(root(fixture))).toBeNull();
  });

  it('follows the second shell when a new session starts in the same tab', async () => {
    fetchItems = [todoItem('800+'), todoItem('Wypłata żona')];
    const first = await mount(layout);
    expect(textOf(badgeOf(root(first)))).toBe('2');
    first.destroy();

    fetchItems = [todoItem('Premia')];
    const second = TestBed.createComponent(Shell);
    await settle(second);

    expect(textOf(badgeOf(root(second)))).toBe('1');
  });
});

describe('Shell full layout', () => {
  it('shows the sidebar with kickers, Settings above the footer and no toolbar', async () => {
    const element = root(await mount('full'));

    expect(element.querySelector('mat-sidenav')).not.toBeNull();
    expect(element.querySelector('[class*="toolbar"]')).toBeNull();
    expect(element.querySelector('[data-testid="bottom-nav"]')).toBeNull();

    const sequence = labelsOf(element, '[data-testid="nav-section"], [data-testid="nav-list"] a');
    expect(sequence).toEqual([
      'Overview',
      'Dashboard',
      'Planning',
      'Wealth',
      'Investment accounts',
      'Bank accounts',
      'Cash',
      'Deposits',
      'Bonds',
      'Stocks & ETFs',
      'Precious metals',
      'Groups',
    ]);

    const pinned = element.querySelector<HTMLElement>('[data-testid="nav-pinned"]')!;
    const footer = element.querySelector<HTMLElement>('[data-testid="nav-footer"]')!;
    expect(linkHrefs(pinned)).toEqual(['/settings']);
    expect(textOf(pinned)).toContain('Settings');
    expect(pinned.compareDocumentPosition(footer) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    expect(buttonByLabel(footer, 'Theme:', false)).not.toBeNull();
    expect(buttonByLabel(footer, 'Log out')).not.toBeNull();
    const language = buttonByLabel(footer, 'Language')!;
    expect(textOf(language)).toBe('EN');
  });

  it('keeps the section kickers out of the focus order', async () => {
    const element = root(await mount('full'));
    const kickers = Array.from(
      element.querySelectorAll<HTMLElement>('[data-testid="nav-section"]'),
    );

    expect(kickers).toHaveLength(2);
    for (const kicker of kickers) {
      expect(kicker.closest('a, button')).toBeNull();
      expect(kicker.querySelector('a, button, [tabindex]')).toBeNull();
    }
  });
});

describe('Shell rail layout', () => {
  it('lists the entries with short captions and full accessible names', async () => {
    const element = root(await mount('rail'));

    expect(element.querySelector('mat-sidenav')).not.toBeNull();
    expect(element.querySelector('[class*="toolbar"]')).toBeNull();
    expect(element.querySelector('[data-testid="nav-section"]')).toBeNull();

    const links = Array.from(
      element.querySelectorAll<HTMLAnchorElement>('mat-sidenav [data-testid="nav-list"] a'),
    );
    expect(links.map((a) => a.getAttribute('href'))).toEqual([
      '/dashboard',
      '/planning',
      '/accounts',
      '/bank-accounts',
      '/cash',
      '/deposits',
      '/bonds',
      '/securities',
      '/metals',
      '/groups',
    ]);
    expect(links.map((a) => a.getAttribute('aria-label'))).toEqual([
      'Dashboard',
      'Planning',
      'Investment accounts',
      'Bank accounts',
      'Cash',
      'Deposits',
      'Bonds',
      'Stocks & ETFs',
      'Precious metals',
      'Groups',
    ]);
    expect(labelsOf(element, 'mat-sidenav [data-testid="nav-list"] a')).toEqual([
      'Dashboard',
      'Planning',
      'Accounts',
      'Banks',
      'Cash',
      'Deposits',
      'Bonds',
      'Stocks & ETFs',
      'Metals',
      'Groups',
    ]);

    const pinned = element.querySelector<HTMLElement>('[data-testid="nav-pinned"]')!;
    expect(linkHrefs(pinned)).toEqual(['/settings']);
    expect(pinned.querySelector('a')!.getAttribute('aria-label')).toBe('Settings');
    expect(element.querySelectorAll('mat-sidenav a')).toHaveLength(11);

    const footer = element.querySelector<HTMLElement>('[data-testid="nav-footer"]')!;
    expect(footer.querySelectorAll('button')).toHaveLength(3);
    expect(textOf(buttonByLabel(footer, 'Language'))).not.toBe('EN');
    expect(buttonByLabel(footer, 'Log out')).not.toBeNull();
  });
});

describe('Shell bottom layout', () => {
  it('shows a top bar and a bottom nav without a sidenav', async () => {
    const element = root(await mount('bottom'));

    expect(element.querySelector('mat-sidenav')).toBeNull();
    expect(element.querySelector('[class*="toolbar"]')).toBeNull();

    const top = element.querySelector<HTMLElement>('[data-testid="top-bar"]')!;
    expect(textOf(top)).toContain('Skarbiec');
    expect(linkHrefs(top)).toEqual(['/settings']);
    expect(buttonByLabel(top, 'Theme:', false)).not.toBeNull();
    expect(buttonByLabel(top, 'Language')).not.toBeNull();
    expect(buttonByLabel(top, 'Log out')).not.toBeNull();

    const bottom = element.querySelector<HTMLElement>('[data-testid="bottom-nav"]')!;
    expect(bottom.tagName).toBe('NAV');
    expect(bottom.getAttribute('aria-label')).toBe('Main navigation');
    expect(labelsOf(bottom, 'a, button')).toEqual(['Dashboard', 'Planning', 'Groups', 'More']);
    expect(linkHrefs(bottom)).toEqual(['/dashboard', '/planning', '/groups']);
  });
});

describe('Shell more sheet', () => {
  function sheet(): HTMLElement | null {
    return TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelector<HTMLElement>('mat-bottom-sheet-container');
  }

  it('opens a Wealth sheet with the seven asset links, navigates and closes on choice', async () => {
    const fixture = await mount('bottom');
    const element = root(fixture);

    moreButton(element).click();
    await settle(fixture);

    const opened = sheet()!;
    expect(opened).not.toBeNull();
    expect(textOf(opened)).toContain('Wealth');
    expect(linkHrefs(opened)).toEqual(ASSET_PATHS.map((path) => `/${path}`));

    const bonds = Array.from(opened.querySelectorAll('a')).find(
      (a) => a.getAttribute('href') === '/bonds',
    )!;
    bonds.click();
    await settle(fixture);
    await new Promise((resolve) => setTimeout(resolve, 50));
    await settle(fixture);

    expect(TestBed.inject(Router).url).toBe('/bonds');
    expect(sheet()).toBeNull();
    const more = moreButton(element);
    expect(more.getAttribute('aria-current')).toBe('page');
    expect(more.classList.contains('shell__nav-link--active')).toBe(true);
  });

  it('does not highlight More on a route outside the seven', async () => {
    const fixture = await mount('bottom');
    await TestBed.inject(Router).navigateByUrl('/dashboard');
    await settle(fixture);

    expect(moreButton(root(fixture)).hasAttribute('aria-current')).toBe(false);
  });
});

describe.each(LAYOUTS)('Shell active item (%s layout)', (layout) => {
  let fixture: ComponentFixture<Shell>;

  beforeEach(async () => {
    fixture = await mount(layout);
  });

  function expectedFor(path: string): string | null {
    return layout === 'bottom' && ASSET_PATHS.includes(path) ? null : `/${path}`;
  }

  async function activeHrefs(url: string): Promise<(string | null)[]> {
    await TestBed.inject(Router).navigateByUrl(url);
    await settle(fixture);
    const element = root(fixture);
    const byClass = Array.from(element.querySelectorAll('.shell__nav-link--active'), (a) =>
      a.getAttribute('href'),
    );
    const byAria = Array.from(element.querySelectorAll('[aria-current="page"]'), (a) =>
      a.getAttribute('href'),
    );
    expect(byAria).toEqual(byClass);

    return byClass;
  }

  it('highlights the origin item on the transactions route', async () => {
    expect(await activeHrefs('/assets/a1/transactions?from=cash')).toEqual([expectedFor('cash')]);
    expect(await activeHrefs('/assets/a1/transactions?from=metals')).toEqual([
      expectedFor('metals'),
    ]);
    expect(await activeHrefs('/assets/a1/transactions?from=accounts')).toEqual([
      expectedFor('accounts'),
    ]);
    expect(await activeHrefs('/assets/a1/transactions')).toEqual([]);
    expect(await activeHrefs('/assets/a1/transactions?from=nowhere')).toEqual([]);
  });

  it('keeps highlighting Rachunki on account detail', async () => {
    expect(await activeHrefs('/accounts/acc-1')).toEqual([expectedFor('accounts')]);
  });

  it('highlights Dashboard on its route', async () => {
    expect(await activeHrefs('/dashboard')).toEqual(['/dashboard']);
  });
});

describe.each(LAYOUTS)('Shell actions (%s layout)', (layout) => {
  let fixture: ComponentFixture<Shell>;

  beforeEach(async () => {
    fixture = await mount(layout);
  });

  it('theme cycles system, light, dark with its label', async () => {
    const element = root(fixture);
    const themeService = TestBed.inject(ThemeService);
    const theme = () => buttonByLabel(element, 'Theme:', false)!;

    expect(themeService.preference()).toBe('system');
    expect(theme().getAttribute('aria-label')).toBe(
      'Theme: System (following device) — click for Light',
    );

    theme().click();
    await settle(fixture);
    expect(themeService.preference()).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
    expect(theme().getAttribute('aria-label')).toBe('Theme: Light — click for Dark');

    theme().click();
    await settle(fixture);
    expect(themeService.preference()).toBe('dark');
    expect(theme().getAttribute('aria-label')).toBe('Theme: Dark — click for System');

    theme().click();
    await settle(fixture);
    expect(themeService.preference()).toBe('system');
  });

  it('log out calls AuthService.logout', () => {
    const logout = vi.spyOn(TestBed.inject(AuthService), 'logout').mockResolvedValue(undefined);

    buttonByLabel(root(fixture), 'Log out')!.click();

    expect(logout).toHaveBeenCalledTimes(1);
  });

  it('switches to Polish without reload', async () => {
    const element = root(fixture);
    const items = (await openLanguageMenu(fixture)).map(textOf);
    expect(items.some((item) => item.includes('Polski'))).toBe(true);

    await pickLanguageFromMenu(fixture, 'Polski');

    const theme = buttonByLabel(element, 'Motyw:', false);
    expect(theme).not.toBeNull();
    expect(looksLikeTranslationKey(theme!.getAttribute('aria-label'))).toBe(false);
    expect(buttonByLabel(element, 'Log out')).toBeNull();
    expect(document.documentElement.lang).toBe('pl');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('pl');
    if (layout !== 'bottom') {
      const names = Array.from(element.querySelectorAll('mat-sidenav a'), (a) =>
        (a.getAttribute('aria-label') ?? textOf(a.querySelector('.shell__nav-label'))).trim(),
      );
      expect(names).toContain('Rachunki inwestycyjne');
      expect(names).toContain('Ustawienia');
    }
  });
});
