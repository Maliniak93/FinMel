import { ComponentFixture, TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { Money } from './money';

type Inputs = Partial<{
  amount: number | null | undefined;
  currency: string;
  kind: 'value' | 'change';
  estimatePln: number | null;
  rateMissing: boolean;
  stale: boolean;
}>;

describe('Money', () => {
  let fixture: ComponentFixture<Money>;
  let element: HTMLElement;

  async function render(inputs: Inputs): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [Money],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Money);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    await fixture.whenStable();
    element = fixture.nativeElement as HTMLElement;
  }

  const text = () => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
  const icons = () => [...element.querySelectorAll('mat-icon')].map((i) => i.textContent?.trim());
  const tag = () => element.querySelector('.money__tag');
  const has = (cls: string) => element.querySelector(`.${cls}`) !== null;

  it('renders a value', async () => {
    await render({ amount: 12345.67 });

    expect(text()).toBe('PLN 12,345.67');
    expect(icons()).toEqual([]);
    expect(tag()).toBeNull();
    expect(has('money--positive')).toBe(false);
    expect(has('money--negative')).toBe(false);
  });

  it('renders zero neutral', async () => {
    await render({ amount: 0 });

    expect(text()).toBe('PLN 0.00');
    expect(has('money--zero')).toBe(true);
    expect(icons()).toEqual([]);
  });

  it('renders a positive change up', async () => {
    await render({ amount: 41208.1, kind: 'change' });

    expect(text()).toContain('+PLN 41,208.10');
    expect(has('money--positive')).toBe(true);
    expect(icons()).toEqual(['trending_up']);
  });

  it('renders a negative change down', async () => {
    await render({ amount: -1220, kind: 'change' });

    expect(text()).toContain('−PLN 1,220.00');
    expect(has('money--negative')).toBe(true);
    expect(icons()).toEqual(['trending_down']);
  });

  it('renders a change rounding to zero neutral', async () => {
    await render({ amount: 0.004, kind: 'change' });

    expect(text()).toBe('PLN 0.00');
    expect(has('money--zero')).toBe(true);
    expect(has('money--positive')).toBe(false);
    expect(icons()).toEqual([]);
  });

  it('renders a negative balance with the minus and no trend icon', async () => {
    await render({ amount: -1220 });

    expect(text()).toContain('−PLN 1,220.00');
    expect(icons()).toEqual([]);
    expect(has('money--positive')).toBe(false);
  });

  it('renders a foreign amount with its PLN estimate', async () => {
    await render({ amount: 420, currency: 'EUR', estimatePln: 1820 });

    expect(text()).toBe('EUR 420.00 ≈ PLN 1,820.00');
    expect(tag()).toBeNull();
  });

  it('renders a foreign amount without a rate with a tag and no estimate', async () => {
    await render({ amount: 200, currency: 'CHF', rateMissing: true });

    expect(text()).toContain('CHF 200.00');
    expect(text()).not.toContain('≈');
    expect(tag()?.textContent?.trim()).toBe('Rate unavailable');
  });

  it('renders a stale value with a tag', async () => {
    await render({ amount: 100, stale: true });

    expect(text()).toContain('PLN 100.00');
    expect(tag()?.textContent?.trim()).toBe('Stale');
  });

  it('renders no value as a dash', async () => {
    await render({ amount: null });

    expect(text()).toBe('—');
    expect(tag()).toBeNull();
    expect(icons()).toEqual([]);
  });
});
