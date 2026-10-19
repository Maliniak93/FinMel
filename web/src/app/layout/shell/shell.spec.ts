import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

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

  it('offers an "Obligacje" nav item linking to /bonds', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const link = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('mat-nav-list a'),
    ).find((a) => (a.textContent ?? '').includes('Obligacje'));

    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/bonds');
  });

  const sidebarSequence = () =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll(
        '[data-testid="nav-section"], mat-sidenav a [matListItemTitle]',
      ),
      textOf,
    );

  it('groups the nav items under section headings in order', async () => {
    expect(sidebarSequence()).toEqual([
      'Overview',
      'Dashboard',
      'Allocation',
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
      'Settings',
    ]);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    expect(sidebarSequence()).toEqual([
      'Przegląd',
      'Pulpit',
      'Alokacja',
      'Planowanie',
      'Majątek',
      'Rachunki inwestycyjne',
      'Konta bankowe',
      'Gotówka',
      'Lokaty',
      'Obligacje',
      'Akcje i ETF',
      'Metale szlachetne',
      'Grupy',
      'Ustawienia',
    ]);
  });

  it('offers a "Groups" nav item linking to /groups in both languages', async () => {
    const groupsLink = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
          'mat-nav-list a',
        ),
      ).find((a) => a.getAttribute('href') === '/groups');

    expect(groupsLink()?.textContent).toContain('Groups');

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    expect(groupsLink()?.textContent).toContain('Grupy');
  });

  it('pins settings below the nav list', () => {
    const element = fixture.nativeElement as HTMLElement;
    const list = element.querySelector<HTMLElement>('[data-testid="nav-list"]')!;
    const pinned = element.querySelector<HTMLElement>('[data-testid="nav-pinned"]')!;
    const settings = Array.from(element.querySelectorAll<HTMLAnchorElement>('mat-sidenav a')).find(
      (a) => a.getAttribute('href') === '/settings',
    )!;

    expect(list).not.toBeNull();
    expect(pinned).not.toBeNull();
    expect(pinned.contains(settings)).toBe(true);
    expect(list.contains(settings)).toBe(false);
    expect(list.contains(pinned)).toBe(false);
    expect(list.compareDocumentPosition(pinned) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(pinned.querySelectorAll('a')).toHaveLength(1);
    expect(pinned.querySelector('[data-testid="nav-section"]')).toBeNull();
  });

  it('section headings are not focusable', async () => {
    const headings = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          '[data-testid="nav-section"]',
        ),
      );

    expect(headings()).toHaveLength(2);
    for (const heading of headings()) {
      expect(heading.closest('a, button')).toBeNull();
      expect(heading.querySelector('a, button, [tabindex]')).toBeNull();
      expect(heading.hasAttribute('tabindex')).toBe(false);
      expect(heading.hasAttribute('href')).toBe(false);
    }
    expect(headings().map(textOf)).toEqual(['Overview', 'Wealth']);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    expect(headings().map(textOf)).toEqual(['Przegląd', 'Majątek']);
  });

  it('switches the navigation to Polish without a reload', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const navLabels = () =>
      Array.from(element.querySelectorAll('mat-sidenav a [matListItemTitle]'), textOf);

    const englishLabels = navLabels();
    expect(englishLabels).toEqual([
      'Dashboard',
      'Allocation',
      'Planning',
      'Investment accounts',
      'Bank accounts',
      'Cash',
      'Deposits',
      'Bonds',
      'Stocks & ETFs',
      'Precious metals',
      'Groups',
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

describe('Shell active item', () => {
  let fixture: ComponentFixture<Shell>;
  let router: Router;

  beforeEach(async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => jsonResponse({ items: [] }));

    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: '**', children: [] }]), provideI18nTesting()],
    }).compileComponents();

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  async function activeHrefs(url: string): Promise<(string | null)[]> {
    await router.navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        'mat-sidenav a.shell__nav-link--active',
      ),
      (a) => a.getAttribute('href'),
    );
  }

  it('highlights the origin item on the transactions route', async () => {
    expect(await activeHrefs('/assets/a1/transactions?from=cash')).toEqual(['/cash']);
    expect(await activeHrefs('/assets/a1/transactions?from=metals')).toEqual(['/metals']);
    expect(await activeHrefs('/assets/a1/transactions?from=securities')).toEqual(['/securities']);
    expect(await activeHrefs('/assets/a1/transactions?from=accounts')).toEqual(['/accounts']);
    expect(await activeHrefs('/assets/a1/transactions')).toEqual([]);
    expect(await activeHrefs('/assets/a1/transactions?from=nowhere')).toEqual([]);
  });

  it('keeps highlighting Rachunki on account detail', async () => {
    expect(await activeHrefs('/accounts/acc-1')).toEqual(['/accounts']);
  });
});
