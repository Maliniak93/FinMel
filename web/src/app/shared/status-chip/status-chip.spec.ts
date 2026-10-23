import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { StatusChip } from './status-chip';

@Component({
  imports: [StatusChip],
  template: `
    <app-status-chip id="positive" label="Done" icon="check" tone="positive" />
    <app-status-chip id="negative" label="Late" icon="error" tone="negative" />
    <app-status-chip id="warning" label="Careful" icon="warning" tone="warning" />
    <app-status-chip id="neutral" label="Plain" icon="info" tone="neutral" />
    <app-status-chip id="uncertain" label="Maybe" icon="help" tone="neutral" [uncertain]="true" />
  `,
})
class Host {}

function has(root: HTMLElement, selector: string): boolean {
  return root.matches(selector) || root.querySelector(selector) !== null;
}

describe('StatusChip', () => {
  it('shows icon and word per tone, dashed only when uncertain', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const chip = (id: string) => element.querySelector(`#${id}`) as HTMLElement;

    const expected: Record<string, [string, string]> = {
      positive: ['check', 'Done'],
      negative: ['error', 'Late'],
      warning: ['warning', 'Careful'],
      neutral: ['info', 'Plain'],
      uncertain: ['help', 'Maybe'],
    };
    for (const [id, [icon, word]] of Object.entries(expected)) {
      expect(chip(id).querySelector('mat-icon')?.textContent?.trim()).toBe(icon);
      expect(chip(id).textContent).toContain(word);
    }
    for (const tone of ['positive', 'negative', 'warning', 'neutral']) {
      expect(has(chip(tone), `.status-chip--${tone}`), tone).toBe(true);
    }

    expect(has(chip('uncertain'), '.status-chip--uncertain')).toBe(true);
    expect(has(chip('positive'), '.status-chip--uncertain')).toBe(false);
  });
});
