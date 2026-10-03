import { Injectable, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

export const THEME_STORAGE_KEY = 'skarbiec-theme';
const THEME_ATTRIBUTE = 'data-theme';
const CYCLE: readonly ThemePreference[] = ['system', 'light', 'dark'];

function readStoredPreference(): ThemePreference {
  const stored = localStorage.getItem(THEME_STORAGE_KEY);
  return stored === 'light' || stored === 'dark' ? stored : 'system';
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly preference = signal<ThemePreference>(readStoredPreference());

  constructor() {
    this.applyToDocument(this.preference());
  }

  setPreference(preference: ThemePreference): void {
    this.preference.set(preference);
    localStorage.setItem(THEME_STORAGE_KEY, preference);
    this.applyToDocument(preference);
  }

  cycle(): void {
    const next = CYCLE[(CYCLE.indexOf(this.preference()) + 1) % CYCLE.length];
    this.setPreference(next);
  }

  private applyToDocument(preference: ThemePreference): void {
    const html = document.documentElement;
    if (preference === 'system') {
      html.removeAttribute(THEME_ATTRIBUTE);
      html.style.removeProperty('color-scheme');
    } else {
      html.setAttribute(THEME_ATTRIBUTE, preference);
      html.style.setProperty('color-scheme', preference);
    }
  }
}
