import { OverlayContainer } from '@angular/cdk/overlay';
import { TestBed, type ComponentFixture } from '@angular/core/testing';

// Shared helpers for the specs of pages that archive single assets (asset-archive, #128): the
// "Show archived" slide toggle and a row's Archive / Restore menu items. Test-only: nothing in the
// app imports this file. Kept free of Vitest globals so it also type-checks under tsconfig.app.json.

// Flips the page's "Show archived" slide toggle the way a user does: clicks its switch.
export async function showArchived(fixture: ComponentFixture<unknown>): Promise<void> {
  const toggle = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
    'mat-slide-toggle button',
  );
  if (!toggle) {
    throw new Error('No "Show archived" slide toggle rendered.');
  }
  toggle.click();
  fixture.detectChanges();
  await fixture.whenStable();
}

// A menu item's visible label, without its <mat-icon> ligature: the item's textContent runs the
// two together ("archiveArchive", "unarchiveRestore"), which no word-boundary pattern can split.
// With the icon gone, /\barchive\b/i matches "Archive" and not "Restore".
export function menuItemLabel(item: Element): string {
  const clone = item.cloneNode(true) as Element;
  clone.querySelectorAll('mat-icon').forEach((icon) => icon.remove());
  return (clone.textContent ?? '').replace(/\s+/g, ' ').trim();
}

// Opens a row's actions menu and returns the items it offers (read their labels with menuItemLabel).
export async function rowMenuItems(
  fixture: ComponentFixture<unknown>,
  row: HTMLElement,
): Promise<HTMLElement[]> {
  const trigger = row.querySelector<HTMLButtonElement>('button[aria-label^="Actions for"]');
  if (!trigger) {
    throw new Error('No actions menu on this row.');
  }
  trigger.click();
  fixture.detectChanges();
  await fixture.whenStable();
  return Array.from(
    TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelectorAll<HTMLElement>('.mat-mdc-menu-item'),
  );
}

// Opens a row's actions menu and clicks the item whose label matches `label`, then lets it settle.
export async function clickRowMenuItem(
  fixture: ComponentFixture<unknown>,
  row: HTMLElement,
  label: RegExp,
): Promise<void> {
  const items = await rowMenuItems(fixture, row);
  const item = items.find((candidate) => label.test(menuItemLabel(candidate)));
  if (!item) {
    throw new Error(
      `No menu item matching ${label}; the menu offers: ${items.map(menuItemLabel).join(', ')}.`,
    );
  }
  item.click();
  fixture.detectChanges();
  await fixture.whenStable();
}
