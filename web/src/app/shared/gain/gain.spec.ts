import { ComponentFixture, TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney, formatPercent, formatSignedMoney } from '../format';
import { Gain } from './gain';

type Inputs = Partial<{
  amount: number | null;
  percent: number | null;
  currency: string;
  dashWithoutPercent: boolean;
}>;

const collapse = (text: string) => text.replace(/\s+/g, ' ').trim();

describe('Gain', () => {
  let fixture: ComponentFixture<Gain>;
  let element: HTMLElement;

  async function render(inputs: Inputs): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [Gain],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Gain);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    await fixture.whenStable();
    element = fixture.nativeElement as HTMLElement;
  }

  const text = () => collapse(element.textContent ?? '');
  const has = (cls: string) => element.querySelector(`.${cls}`) !== null;

  it('renders a gain with the signed amount and the percent in brackets, no icon', async () => {
    await render({ amount: 960, percent: 14.16 });

    expect(text()).toContain(collapse(formatSignedMoney(960, 'PLN')));
    expect(
      [`(${formatPercent(14.16)})`, `(+${formatPercent(14.16)})`].some((part) =>
        text().includes(part),
      ),
    ).toBe(true);
    expect(has('gain--positive')).toBe(true);
    expect(element.querySelector('mat-icon')).toBeNull();
  });

  it('renders a loss in the negative tone with a minus and no icon', async () => {
    await render({ amount: -500, percent: -3.7 });

    expect(text()).toContain(collapse(formatSignedMoney(-500, 'PLN')));
    expect(text()).toContain(collapse(formatPercent(-3.7)));
    expect(has('gain--negative')).toBe(true);
    expect(element.querySelector('mat-icon')).toBeNull();
  });

  it('renders a zero gain neutral, with no sign and no colour', async () => {
    await render({ amount: 0.004, percent: 0 });

    expect(text()).toContain(collapse(formatMoney(0, 'PLN')));
    expect(has('gain--zero')).toBe(true);
    expect(has('gain--positive')).toBe(false);
    expect(has('gain--negative')).toBe(false);
    expect(text()).not.toMatch(/[+−-]/);
  });

  it('renders a missing cost as a dash', async () => {
    await render({ amount: null, percent: null });

    expect(text()).toBe('—');
  });

  it('renders an amount without a percent in its own currency', async () => {
    await render({ amount: 50, percent: null, currency: 'EUR' });

    expect(text()).toContain(collapse(formatSignedMoney(50, 'EUR')));
    expect(text()).not.toContain('%');
  });

  it('renders a dash in place of a missing percent when asked to', async () => {
    await render({ amount: 50, percent: null, dashWithoutPercent: true });

    expect(text()).toContain(collapse(formatSignedMoney(50, 'PLN')));
    expect(text()).toContain('(—)');
  });
});
