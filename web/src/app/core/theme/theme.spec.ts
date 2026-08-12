import { TestBed } from '@angular/core/testing';

import { THEME_STORAGE_KEY, ThemeService } from './theme';

function resetDocument(): void {
  localStorage.clear();
  document.documentElement.removeAttribute('data-theme');
  document.documentElement.style.removeProperty('color-scheme');
}

function colorScheme(): string {
  return document.documentElement.style.getPropertyValue('color-scheme');
}

describe('ThemeService', () => {
  beforeEach(() => {
    resetDocument();
    TestBed.configureTestingModule({});
  });

  afterEach(resetDocument);

  it('defaults to system with no stored preference and no attribute/inline style on <html>', () => {
    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
    expect(colorScheme()).toBe('');
  });

  it('restores a stored preference on construction', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'dark');

    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(colorScheme()).toBe('dark');
  });

  it('falls back to system for an invalid stored value', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'purple');

    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
    expect(colorScheme()).toBe('');
  });

  it('setPreference persists the choice and updates the document attribute and inline style', () => {
    const service = TestBed.inject(ThemeService);

    service.setPreference('light');

    expect(service.preference()).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(colorScheme()).toBe('light');

    service.setPreference('dark');

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    expect(colorScheme()).toBe('dark');
  });

  it('setPreference back to system clears the document attribute and inline style', () => {
    const service = TestBed.inject(ThemeService);

    service.setPreference('dark');
    service.setPreference('system');

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
    expect(colorScheme()).toBe('');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('system');
  });

  it('cycle() rotates system -> light -> dark -> system', () => {
    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('system');

    service.cycle();
    expect(service.preference()).toBe('light');

    service.cycle();
    expect(service.preference()).toBe('dark');

    service.cycle();
    expect(service.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
    expect(colorScheme()).toBe('');
  });
});
