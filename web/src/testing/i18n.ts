import { OverlayContainer } from '@angular/cdk/overlay';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { MatMenuTrigger } from '@angular/material/menu';
import { By } from '@angular/platform-browser';

import { LANGUAGE_STORAGE_KEY, LanguageService } from '../app/core/i18n/language';
import en from '../i18n/en.json';
import pl from '../i18n/pl.json';

export type TestLanguage = 'en' | 'pl';

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

export function isTranslationIn(lang: TestLanguage, text: string | null | undefined): boolean {
  return translationValues(lang).includes((text ?? '').trim());
}

export function looksLikeTranslationKey(text: string | null | undefined): boolean {
  return /^[A-Za-z0-9_-]+(\.[A-Za-z0-9_-]+)+$/.test((text ?? '').trim());
}

export function textOf(element: Element | null | undefined): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

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

function collapse(text: string | null | undefined): string {
  return (text ?? '').replace(/\s+/g, ' ').trim();
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

const PARAMETER = /\{\{\s*[\w.]+\s*\}\}/;

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

export function textsOf(root: ParentNode, selector: string): string[] {
  return Array.from(root.querySelectorAll(selector), (element) => textOf(element));
}

export function attributesOf(root: ParentNode, selector: string, attribute: string): string[] {
  return Array.from(root.querySelectorAll(selector), (element) =>
    collapse(element.getAttribute(attribute)),
  );
}

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

export async function switchLanguage(
  fixture: ComponentFixture<unknown>,
  language: TestLanguage,
): Promise<void> {
  await TestBed.inject(LanguageService).setLanguage(language);
  fixture.detectChanges();
  await fixture.whenStable();
}

export async function restoreEnglish(): Promise<void> {
  await TestBed.inject(LanguageService).setLanguage('en');
  localStorage.removeItem(LANGUAGE_STORAGE_KEY);
}

export function labelsOf(root: ParentNode, selector: string): string[] {
  return Array.from(root.querySelectorAll(selector), (element) => {
    const clone = element.cloneNode(true) as Element;
    clone.querySelectorAll('mat-icon').forEach((icon) => icon.remove());
    return textOf(clone);
  });
}
