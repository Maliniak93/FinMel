import { Injectable, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

export const THEME_STORAGE_KEY = 'skarbiec-theme';
const THEME_ATTRIBUTE = 'data-theme';
const CYCLE: readonly ThemePreference[] = ['system', 'light', 'dark'];

function readStoredPreference(): ThemePreference {
  const stored = localStorage.getItem(THEME_STORAGE_KEY);
  return stored === 'light' || stored === 'dark' ? stored : 'system';
}

// Display preference, not a credential — localStorage is fine here (ADR-005's "no localStorage"
// rule is about auth tokens). Applied synchronously by an inline script in index.html too, before
// Angular bootstraps, so a hard reload never flashes the previous choice while the app is
// loading.
//
// `system` clears both: `styles.scss` pins `color-scheme: light dark` on the bare `html`
// selector, so the browser follows the OS preference on its own, live, with no JS involved.
// `light`/`dark` write `color-scheme` directly onto <html>'s inline `style` — not a stylesheet
// rule — because an inline style wins the cascade unconditionally regardless of load timing;
// production builds inline only a "critical" CSS subset into <head> and load the rest async, so
// an equal-specificity override rule sitting in that async remainder would apply a beat after
// first paint and flash. Every Material system variable (`--mat-sys-*`) resolves its
// `light-dark()` against whatever `color-scheme` is in effect here. `data-theme` is set too, as a
// plain hook for anything (tests, future bespoke dark-mode CSS) that wants to key off the
// explicit choice rather than the resolved `color-scheme`.
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

  // system -> light -> dark -> system, for a single toggle button in the shell toolbar.
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
