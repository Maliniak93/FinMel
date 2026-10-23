import { Injectable, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

export const THEME_STORAGE_KEY = 'skarbiec-theme';
const THEME_ATTRIBUTE = 'data-theme';
const DARK_QUERY = '(prefers-color-scheme: dark)';
const CYCLE: readonly ThemePreference[] = ['system', 'light', 'dark'];

function readStoredPreference(): ThemePreference {
  const stored = localStorage.getItem(THEME_STORAGE_KEY);
  return stored === 'light' || stored === 'dark' ? stored : 'system';
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly preference = signal<ThemePreference>(readStoredPreference());
  private readonly deviceScheme = window.matchMedia(DARK_QUERY);

  constructor() {
    this.deviceScheme.addEventListener('change', () => this.applyToDocument());
    this.applyToDocument();
  }

  setPreference(preference: ThemePreference): void {
    this.preference.set(preference);
    localStorage.setItem(THEME_STORAGE_KEY, preference);
    this.applyToDocument();
  }

  cycle(): void {
    const next = CYCLE[(CYCLE.indexOf(this.preference()) + 1) % CYCLE.length];
    this.setPreference(next);
  }

  private applyToDocument(): void {
    const preference = this.preference();
    const resolved =
      preference === 'system' ? (this.deviceScheme.matches ? 'dark' : 'light') : preference;
    document.documentElement.setAttribute(THEME_ATTRIBUTE, resolved);
  }
}
