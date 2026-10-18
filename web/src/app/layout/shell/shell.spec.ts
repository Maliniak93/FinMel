import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import {
  jsonResponse,
  requestUrl,
} from '../../features/assets/asset-form/testing/asset-form-fixtures';
import {
  isTranslationIn,
  looksLikeTranslationKey,
  openLanguageMenu,
  pickLanguageFromMenu,
  textOf,
} from '../../../testing/i18n';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { THEME_STORAGE_KEY, ThemeService } from '../../core/theme/theme';
import { Shell } from './shell';

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

describe('Shell planning badge', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function createShell(items: Record<string, unknown>[]): Promise<HTMLElement> {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input) =>
        requestUrl(input).includes('/planning/todo')
          ? jsonResponse({ items, paymentLeadDays: 7 })
          : jsonResponse({ detail: 'Not found.' }, 404),
      );
    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([]), provideI18nTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  function planningLink(element: HTMLElement): HTMLAnchorElement {
    return Array.from(element.querySelectorAll<HTMLAnchorElement>('mat-nav-list a')).find(
      (a) => a.getAttribute('href') === '/planning',
    )!;
  }

  it('shows the todo count as a badge at Planning when it is above zero', async () => {
    const element = await createShell([
      todoItem('800+'),
      todoItem('Wypłata żona'),
      todoItem('Premia'),
    ]);

    const badge = planningLink(element).querySelector('[data-testid="todo-badge"]');
    expect(badge).not.toBeNull();
    expect(badge!.textContent!.trim()).toBe('3');
  });

  it('shows no badge at Planning when the todo list is empty', async () => {
    const element = await createShell([]);

    expect(planningLink(element).querySelector('[data-testid="todo-badge"]')).toBeNull();
  });

  it('follows the second shell when a new session starts in the same tab', async () => {
    let items = [todoItem('800+'), todoItem('Wypłata żona')];
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => jsonResponse({ items, paymentLeadDays: 7 }));
    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([]), provideI18nTesting()],
    }).compileComponents();

    const first = TestBed.createComponent(Shell);
    await first.whenStable();
    first.detectChanges();
    await first.whenStable();
    first.detectChanges();
    const firstBadge = planningLink(first.nativeElement).querySelector(
      '[data-testid="todo-badge"]',
    );
    expect(firstBadge!.textContent!.trim()).toBe('2');
    first.destroy();

    items = [todoItem('Premia')];
    const second = TestBed.createComponent(Shell);
    await second.whenStable();
    second.detectChanges();
    await second.whenStable();
    second.detectChanges();

    const secondBadge = planningLink(second.nativeElement).querySelector(
      '[data-testid="todo-badge"]',
    );
    expect(secondBadge!.textContent!.trim()).toBe('1');
  });
});

describe('Shell', () => {
  let component: Shell;
  let fixture: ComponentFixture<Shell>;
  let themeService: ThemeService;

  beforeEach(async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => jsonResponse({ items: [] }));

    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.style.removeProperty('color-scheme');

    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([]), provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Shell);
    component = fixture.componentInstance;
    themeService = TestBed.inject(ThemeService);
    await fixture.whenStable();
  });

  afterEach(async () => {
    vi.restoreAllMocks();
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.clear();
    document.documentElement.lang = 'en';
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.style.removeProperty('color-scheme');
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('cycles the theme and reflects it on <html> when the toggle is clicked', () => {
    expect(themeService.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);

    const toggle: HTMLButtonElement = fixture.nativeElement.querySelectorAll('button')[1];
    toggle.click();
    fixture.detectChanges();

    expect(themeService.preference()).toBe('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(document.documentElement.style.getPropertyValue('color-scheme')).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
  });

  it('offers a "Deposits" nav item linking to /deposits', () => {
    const link = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('mat-nav-list a'),
    ).find((a) => (a.textContent ?? '').includes('Deposits'));

    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/deposits');
  });

  it('offers a "Cash" nav item linking to /cash', () => {
    const link = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('mat-nav-list a'),
    ).find((a) => (a.textContent ?? '').includes('Cash'));

    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/cash');
  });

  it('offers a "Gotówka" nav item linking to /cash in Polish', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const link = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('mat-nav-list a'),
    ).find((a) => (a.textContent ?? '').includes('Gotówka'));

    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/cash');
  });

  it('offers a "Cash" nav item right after Precious metals', async () => {
    const links = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
          'mat-nav-list a',
        ),
      );
    const english = links().map((a) => a.getAttribute('href'));
    expect(english.indexOf('/cash')).toBe(english.indexOf('/metals') + 1);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const polish = links();
    const link = polish.find((a) => a.getAttribute('href') === '/cash');
    expect(link?.textContent).toContain('Gotówka');
    expect(polish[polish.indexOf(link!) - 1].textContent).toContain('Metale szlachetne');
  });

  it('offers a "Stocks & ETFs" nav item linking to /securities right after Deposits, "Akcje i ETF" in Polish', async () => {
    const links = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
          'mat-nav-list a',
        ),
      );
    const english = links().map((a) => a.getAttribute('href'));
    expect(english.indexOf('/securities')).toBe(english.indexOf('/deposits') + 1);
    expect(links().find((a) => a.getAttribute('href') === '/securities')?.textContent).toContain(
      'Stocks & ETFs',
    );

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    expect(links().find((a) => a.getAttribute('href') === '/securities')?.textContent).toContain(
      'Akcje i ETF',
    );
  });

  it('offers a "Rachunki" nav item linking to /accounts in place of "Portfele"', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('mat-nav-list a'),
    );
    const accounts = links.find((a) => (a.textContent ?? '').includes('Rachunki'));

    expect(accounts).toBeDefined();
    expect(accounts!.getAttribute('href')).toBe('/accounts');
    expect(links.some((a) => (a.textContent ?? '').includes('Portfele'))).toBe(false);
    expect(links.some((a) => a.getAttribute('href') === '/portfolios')).toBe(false);
  });

  it('offers a "Konta bankowe" nav item linking to /bank-accounts right after Rachunki', async () => {
    const links = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
          'mat-nav-list a',
        ),
      );
    const english = links().map((a) => a.getAttribute('href'));
    expect(english.indexOf('/bank-accounts')).toBe(english.indexOf('/accounts') + 1);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const link = links().find((a) => (a.textContent ?? '').includes('Konta bankowe'));
    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/bank-accounts');
  });

  it('offers a "Lokaty" nav item linking to /deposits right after Konta bankowe', async () => {
    const links = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
          'mat-nav-list a',
        ),
      );
    const english = links().map((a) => a.getAttribute('href'));
    expect(english.indexOf('/deposits')).toBe(english.indexOf('/bank-accounts') + 1);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const polish = links();
    const link = polish.find((a) => a.getAttribute('href') === '/deposits');
    expect(link?.querySelector('[matListItemTitle]')?.textContent?.trim()).toBe('Lokaty');
    expect(polish[polish.indexOf(link!) - 1].textContent).toContain('Konta bankowe');
  });

  it('offers an "Obligacje" nav item linking to /bonds', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const link = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('mat-nav-list a'),
    ).find((a) => (a.textContent ?? '').includes('Obligacje'));

    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/bonds');
  });

  it('offers a "Metale szlachetne" nav item linking to /metals right after Bonds', async () => {
    const links = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
          'mat-nav-list a',
        ),
      );
    const english = links().map((a) => a.getAttribute('href'));
    expect(english.indexOf('/metals')).toBe(english.indexOf('/bonds') + 1);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const link = links().find((a) => (a.textContent ?? '').includes('Metale szlachetne'));
    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/metals');
    expect(link!.querySelector('mat-icon')?.textContent?.trim()).toBe('diamond');
  });

  it('offers an "Alokacja" nav item linking to /allocation right after the dashboard', async () => {
    const links = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
          'mat-nav-list a',
        ),
      );
    const english = links().map((a) => a.getAttribute('href'));
    expect(english.indexOf('/allocation')).toBe(english.indexOf('/dashboard') + 1);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const link = links().find((a) => (a.textContent ?? '').includes('Alokacja'));
    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/allocation');
  });

  it('switches the navigation to Polish without a reload', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const navLabels = () =>
      Array.from(element.querySelectorAll('mat-nav-list a [matListItemTitle]'), textOf);

    const englishLabels = navLabels();
    expect(englishLabels).toEqual([
      'Dashboard',
      'Allocation',
      'Accounts',
      'Bank accounts',
      'Deposits',
      'Stocks & ETFs',
      'Bonds',
      'Precious metals',
      'Cash',
      'Planning',
      'Settings',
    ]);

    const items = (await openLanguageMenu(fixture)).map(textOf);
    expect(items.some((item) => item.includes('English'))).toBe(true);
    expect(items.some((item) => item.includes('Polski'))).toBe(true);

    await pickLanguageFromMenu(fixture, 'Polski');

    const polishLabels = navLabels();
    expect(polishLabels).toHaveLength(englishLabels.length);
    polishLabels.forEach((label, index) => {
      expect(label).not.toBe(englishLabels[index]);
      expect(looksLikeTranslationKey(label)).toBe(false);
      expect(isTranslationIn('pl', label), `"${label}" is not a pl.json value`).toBe(true);
    });
    expect(document.documentElement.lang).toBe('pl');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('pl');
    expect(fixture.componentInstance).toBe(component);

    const itemsInPolish = (await openLanguageMenu(fixture)).map(textOf);
    expect(itemsInPolish.some((item) => item.includes('English'))).toBe(true);
    expect(itemsInPolish.some((item) => item.includes('Polski'))).toBe(true);
  });

  it('translates the theme and log-out button labels', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const ariaLabels = () =>
      Array.from(element.querySelectorAll('mat-toolbar button[aria-label]'), (button) =>
        button.getAttribute('aria-label'),
      );
    const english = ariaLabels();
    expect(english.length).toBeGreaterThanOrEqual(3);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const polish = ariaLabels();
    expect(polish).toHaveLength(english.length);
    polish.forEach((label, index) => {
      expect(label).not.toBe(english[index]);
      expect(looksLikeTranslationKey(label)).toBe(false);
    });
  });
});
