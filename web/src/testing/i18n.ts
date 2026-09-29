import { OverlayContainer } from '@angular/cdk/overlay';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { MatMenuTrigger } from '@angular/material/menu';
import { By } from '@angular/platform-browser';

import { LANGUAGE_STORAGE_KEY, LanguageService } from '../app/core/i18n/language';
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

// --- Polish rendering assertions for the feature screens (#132) ---------------------------------

function collapse(text: string | null | undefined): string {
  return (text ?? '').replace(/\s+/g, ' ').trim();
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

const PARAMETER = /\{\{\s*[\w.]+\s*\}\}/;

// The text is a whole value of that language's file, where a value with `{{ parameters }}` matches
// any text in the parameter's place ("Stan na {{ date }}" matches "Stan na 9 sie 2026").
export function matchesTranslation(lang: TestLanguage, text: string | null | undefined): boolean {
  const collapsed = collapse(text);
  return translationValues(lang).some((value) => {
    if (!PARAMETER.test(value)) {
      return collapse(value) === collapsed;
    }
    const pattern = collapse(value)
      .split(new RegExp(PARAMETER, 'g'))
      .map(escapeRegExp)
      .join('[\\s\\S]*?');
    return new RegExp(`^${pattern}$`).test(collapsed);
  });
}

// Every element matching `selector` under `root`: its whitespace-collapsed text.
export function textsOf(root: ParentNode, selector: string): string[] {
  return Array.from(root.querySelectorAll(selector), (element) => textOf(element));
}

// Every element matching `selector` under `root`: one attribute's value ('' when absent).
export function attributesOf(root: ParentNode, selector: string, attribute: string): string[] {
  return Array.from(root.querySelectorAll(selector), (element) =>
    collapse(element.getAttribute(attribute)),
  );
}

// What is wrong with a list of texts read after switching to Polish, as messages (empty = all
// Polish): each one must differ from the English text at the same position (unless that word is
// spelled the same in Polish, listed in `cognates`), must not be a raw translation key, and must be
// a value of the real pl.json.
export function polishProblems(
  english: readonly string[],
  polish: readonly string[],
  cognates: readonly string[] = [],
): string[] {
  if (english.length !== polish.length) {
    return [
      `expected ${english.length} texts (${english.join(' | ')}) but found ${polish.length} (${polish.join(' | ')})`,
    ];
  }
  const problems: string[] = [];
  polish.forEach((text, index) => {
    if (text === english[index] && !cognates.includes(text)) {
      problems.push(`"${text}" is still English`);
    } else if (looksLikeTranslationKey(text)) {
      problems.push(`"${text}" is a raw translation key`);
    } else if (!matchesTranslation('pl', text)) {
      problems.push(`"${text}" is not a pl.json value`);
    }
  });
  return problems;
}

// Switches the active language the way the language menu does and lets the view settle.
export async function switchLanguage(
  fixture: ComponentFixture<unknown>,
  language: TestLanguage,
): Promise<void> {
  await TestBed.inject(LanguageService).setLanguage(language);
  fixture.detectChanges();
  await fixture.whenStable();
}

// For `afterEach`: specs share one worker (isolate: false), so Polish must never leak into the next
// file.
export async function restoreEnglish(): Promise<void> {
  await TestBed.inject(LanguageService).setLanguage('en');
  localStorage.removeItem(LANGUAGE_STORAGE_KEY);
}

// Like `textsOf`, without the ligature text of any `<mat-icon>` inside ("add New portfolio" → "New
// portfolio").
export function labelsOf(root: ParentNode, selector: string): string[] {
  return Array.from(root.querySelectorAll(selector), (element) => {
    const clone = element.cloneNode(true) as Element;
    clone.querySelectorAll('mat-icon').forEach((icon) => icon.remove());
    return textOf(clone);
  });
}
