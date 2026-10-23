import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { GoalProgress } from './goal-progress';

@Component({
  imports: [GoalProgress],
  template: `
    <app-goal-progress id="zero" [ratio]="0" fill="var(--sk-g-blue)" />
    <app-goal-progress
      id="mid"
      [ratio]="0.56"
      fill="var(--sk-g-blue)"
      [mark]="0.8"
      tone="warning"
    />
    <app-goal-progress id="full" [ratio]="1" fill="var(--sk-g-blue)" tone="done" />
    <app-goal-progress id="over" [ratio]="1.3" fill="var(--sk-g-blue)" />
  `,
})
class Host {}

describe('GoalProgress', () => {
  it('clamps the fill, exposes aria-valuenow and shows tone icons and mark', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const bar = (id: string) => element.querySelector(`#${id}`) as HTMLElement;
    const progress = (id: string) => bar(id).querySelector('[role="progressbar"]') as HTMLElement;
    const fill = (id: string) => bar(id).querySelector('.goal-progress__fill') as HTMLElement;

    const expected = {
      zero: ['0', '0%'],
      mid: ['56', '56%'],
      full: ['100', '100%'],
      over: ['100', '100%'],
    };
    for (const [id, [now, width]] of Object.entries(expected)) {
      expect(progress(id).getAttribute('aria-valuenow'), id).toBe(now);
      expect(fill(id).style.width, id).toBe(width);
    }

    expect(bar('full').querySelector('mat-icon')?.textContent?.trim()).toBe('check');
    expect(bar('mid').querySelector('mat-icon')?.textContent?.trim()).toBe('warning');
    expect(bar('zero').querySelector('mat-icon')).toBeNull();

    const mark = bar('mid').querySelector('.goal-progress__mark') as HTMLElement;
    expect(mark.style.left).toBe('80%');
    expect(bar('zero').querySelector('.goal-progress__mark')).toBeNull();
  });
});
