import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { SplitBar } from './split-bar';

@Component({
  imports: [SplitBar],
  template: `<app-split-bar [segments]="segments" />`,
})
class Host {
  segments = [
    { key: 'stock', label: 'Stock', value: 6000, color: 'rgb(1, 2, 3)' },
    { key: 'cash', label: 'Cash', value: 3900, color: 'rgb(4, 5, 6)' },
    { key: 'etf', label: 'ETF', value: 100, color: 'rgb(7, 8, 9)' },
  ];
}

describe('SplitBar', () => {
  async function render() {
    await TestBed.configureTestingModule({ providers: [provideI18nTesting()] }).compileComponents();
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('draws one coloured segment per entry under an image role with a text alternative', async () => {
    const element = await render();
    const bar = element.querySelector('app-split-bar [role="img"]') as HTMLElement;

    expect(bar).not.toBeNull();
    const alternative = bar.getAttribute('aria-label') ?? '';
    for (const label of ['Stock', 'Cash', 'ETF']) {
      expect(alternative).toContain(label);
    }

    const segments = [...bar.querySelectorAll<HTMLElement>('.split-bar__segment')];
    expect(segments).toHaveLength(3);
    expect(segments.map((segment) => segment.style.backgroundColor)).toEqual([
      'rgb(1, 2, 3)',
      'rgb(4, 5, 6)',
      'rgb(7, 8, 9)',
    ]);
  });
});
