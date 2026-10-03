import { OverlayContainer } from '@angular/cdk/overlay';
import { TestBed, type ComponentFixture } from '@angular/core/testing';

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

export function menuItemLabel(item: Element): string {
  const clone = item.cloneNode(true) as Element;
  clone.querySelectorAll('mat-icon').forEach((icon) => icon.remove());
  return (clone.textContent ?? '').replace(/\s+/g, ' ').trim();
}

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
