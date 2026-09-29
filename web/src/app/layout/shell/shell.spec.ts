import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

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

describe('Shell', () => {
  let component: Shell;
  let fixture: ComponentFixture<Shell>;
  let themeService: ThemeService;

  beforeEach(async () => {
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
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
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

  // term-deposits: a "Deposits" nav item leads to the cross-portfolio Deposits page.
  it('offers a Deposits nav item linking to /deposits', () => {
    const link = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('mat-nav-list a'),
    ).find((a) => (a.textContent ?? '').includes('Deposits'));

    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/deposits');
  });

  // i18n foundation (#131) AC-4: the toolbar language menu switches the running app — no reload.
  it('switches the navigation to Polish without a reload', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const navLabels = () =>
      Array.from(element.querySelectorAll('mat-nav-list a [matListItemTitle]'), textOf);

    const englishLabels = navLabels();
    expect(englishLabels).toEqual(['Dashboard', 'Portfolios', 'Deposits', 'Settings']);

    // Each language is offered under its own name, whatever the active language.
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
    // Same component instance: the switch re-rendered in place.
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
    // Toggle navigation, theme, log out (the language switch may add its own).
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
