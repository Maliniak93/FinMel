import { OverlayContainer } from '@angular/cdk/overlay';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { MatMenuTrigger } from '@angular/material/menu';
import { By } from '@angular/platform-browser';

import en from '../i18n/en.json';
import pl from '../i18n/pl.json';

// Shared i18n helpers for the specs (i18n foundation, #131). Test-only: nothing in the app imports
// this file. Kept free of Vitest globals so it also type-checks under tsconfig.app.json.
//
// Specs assert Polish text without pinning a translation's wording or its key: a rendered string
// is "Polish" when it is one of the values in the real pl.json and differs from the English text
// the same element showed before the switch.

export type TestLanguage = 'en' | 'pl';

// Flattens a nested translation file into dotted keys (`shell.nav.dashboard`), the same shape
// Transloco keeps at runtime. A non-string leaf is kept as-is so a spec can flag it.
export function flattenTranslations(tree: unknown, prefix = ''): Record<string, unknown> {
  const flat: Record<string, unknown> = {};
  if (tree === null || typeof tree !== 'object' || Array.isArray(tree)) {
    return flat;
  }
  for (const [key, value] of Object.entries(tree as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      Object.assign(flat, flattenTranslations(value, path));
    } else {
      flat[path] = value;
    }
  }
  return flat;
}

export const TRANSLATIONS: Readonly<Record<TestLanguage, Record<string, unknown>>> = {
  en: flattenTranslations(en),
  pl: flattenTranslations(pl),
};

export function translationValues(lang: TestLanguage): string[] {
  return Object.values(TRANSLATIONS[lang]).filter(
    (value): value is string => typeof value === 'string',
  );
}

// The trimmed text is a whole value of that language's file.
export function isTranslationIn(lang: TestLanguage, text: string | null | undefined): boolean {
  return translationValues(lang).includes((text ?? '').trim());
}

// A dotted, space-free token such as `shell.nav.dashboard` — what the pipe prints for a missing key.
export function looksLikeTranslationKey(text: string | null | undefined): boolean {
  return /^[A-Za-z0-9_-]+(\.[A-Za-z0-9_-]+)+$/.test((text ?? '').trim());
}

// An element's visible text with whitespace (including the non-breaking spaces Intl emits)
// collapsed to single spaces.
export function textOf(element: Element | null | undefined): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

// The items of the (only) Material menu rendered by the fixture — the language switch. Opens it
// first; the items live in the CDK overlay container, not under the fixture's element.
export async function openLanguageMenu(fixture: ComponentFixture<unknown>): Promise<HTMLElement[]> {
  const triggerElement = fixture.debugElement.query(By.directive(MatMenuTrigger));
  if (!triggerElement) {
    throw new Error('No language menu (a MatMenuTrigger) is rendered by this component.');
  }
  triggerElement.injector.get(MatMenuTrigger).openMenu();
  fixture.detectChanges();
  await fixture.whenStable();

  return Array.from(
    TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelectorAll<HTMLElement>('.mat-mdc-menu-item'),
  );
}

// Picks a language the way the user does: open the menu, click the item labelled `label`
// ("English" / "Polski"), then let the switch settle.
export async function pickLanguageFromMenu(
  fixture: ComponentFixture<unknown>,
  label: 'English' | 'Polski',
): Promise<void> {
  const items = await openLanguageMenu(fixture);
  const item = items.find((candidate) => textOf(candidate).includes(label));
  if (!item) {
    throw new Error(
      `The language menu has no "${label}" item; it offers: ${items.map(textOf).join(', ')}.`,
    );
  }
  item.click();
  fixture.detectChanges();
  await fixture.whenStable();
}
