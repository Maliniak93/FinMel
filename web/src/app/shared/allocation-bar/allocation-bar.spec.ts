import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { formatSignedPoints } from '../format';
import { ALLOCATION_DEVIATION_PP, AllocationBar } from './allocation-bar';

@Component({
  imports: [AllocationBar],
  template: `<app-allocation-bar [rows]="rows" targetLabel="Target" actualLabel="Actual" />`,
})
class Host {
  rows = [
    { label: 'Stocks', color: '#112233', target: 70, actual: 62 },
    { label: 'Bonds', color: '#445566', target: 30, actual: 38 },
    { label: 'Cash', color: '#778899', target: 50, actual: 54 },
  ];
}

function normalized(css: string): string {
  const probe = document.createElement('span');
  probe.style.backgroundColor = css;
  return probe.style.backgroundColor;
}

describe('AllocationBar', () => {
  async function render() {
    await TestBed.configureTestingModule({ providers: [provideI18nTesting()] }).compileComponents();
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('outline vs fill', async () => {
    const element = await render();
    const targets = Array.from(element.querySelectorAll<HTMLElement>('.allocation-bar__target'));
    const actuals = Array.from(element.querySelectorAll<HTMLElement>('.allocation-bar__actual'));
    expect(targets).toHaveLength(3);
    expect(actuals).toHaveLength(3);

    ['#112233', '#445566', '#778899'].forEach((color, index) => {
      expect(actuals[index].style.backgroundColor).toBe(normalized(color));
      expect(targets[index].style.backgroundColor).toBe('');
      expect(
        normalized(targets[index].style.borderColor || targets[index].style.outlineColor),
      ).toBe(normalized(color));
    });
    expect(element.textContent).toContain('Target');
    expect(element.textContent).toContain('Actual');
  });

  it('highlights from 5 pp', async () => {
    expect(ALLOCATION_DEVIATION_PP).toBe(5);
    const element = await render();
    const deviations = Array.from(
      element.querySelectorAll<HTMLElement>('.allocation-bar__deviation'),
    );
    expect(deviations).toHaveLength(3);

    expect(deviations[0].textContent).toContain(formatSignedPoints(-8));
    expect(deviations[1].textContent).toContain(formatSignedPoints(8));
    expect(deviations[2].textContent).toContain(formatSignedPoints(4));
    expect(deviations[0].classList).toContain('allocation-bar__deviation--warning');
    expect(deviations[1].classList).toContain('allocation-bar__deviation--warning');
    expect(deviations[2].classList).not.toContain('allocation-bar__deviation--warning');
  });
});
