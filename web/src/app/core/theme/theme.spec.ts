import { TestBed } from '@angular/core/testing';

import { stubMatchMedia, type MatchMediaStub } from '../../../testing/match-media';
import { THEME_STORAGE_KEY, ThemeService } from './theme';

function resetDocument(): void {
  localStorage.clear();
  document.documentElement.removeAttribute('data-theme');
  document.documentElement.style.removeProperty('color-scheme');
}

function colorScheme(): string {
  return document.documentElement.style.getPropertyValue('color-scheme');
}

function dataTheme(): string | null {
  return document.documentElement.getAttribute('data-theme');
}

describe('ThemeService', () => {
  let media: MatchMediaStub;

  beforeEach(() => {
    resetDocument();
    media = stubMatchMedia(false);
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    media.restore();
    resetDocument();
  });

  it('light and dark persist', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'dark');
    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('dark');
    expect(dataTheme()).toBe('dark');
    expect(colorScheme()).toBe('');

    service.setPreference('light');

    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
    expect(dataTheme()).toBe('light');
    expect(colorScheme()).toBe('');

    service.setPreference('dark');

    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    expect(dataTheme()).toBe('dark');
  });

  it('system resolves to the device scheme', () => {
    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('system');
    expect(dataTheme()).toBe('light');
    expect(colorScheme()).toBe('');

    service.setPreference('dark');
    service.setPreference('system');

    expect(dataTheme()).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('system');
  });

  it('system resolves to dark on a dark device', () => {
    media.restore();
    media = stubMatchMedia(true);

    TestBed.inject(ThemeService);

    expect(dataTheme()).toBe('dark');
  });

  it('system follows device changes', () => {
    const service = TestBed.inject(ThemeService);
    expect(dataTheme()).toBe('light');

    media.setDark(true);
    expect(dataTheme()).toBe('dark');

    media.setDark(false);
    expect(dataTheme()).toBe('light');

    service.setPreference('light');
    media.setDark(true);
    expect(dataTheme()).toBe('light');
  });

  it('invalid value falls back to system', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'purple');

    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('system');
    expect(dataTheme()).toBe('light');
    expect(colorScheme()).toBe('');
  });

  it('cycle() rotates system → light → dark → system', () => {
    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('system');

    service.cycle();
    expect(service.preference()).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');

    service.cycle();
    expect(service.preference()).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');

    service.cycle();
    expect(service.preference()).toBe('system');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('system');
    expect(dataTheme()).toBe('light');
  });
});
