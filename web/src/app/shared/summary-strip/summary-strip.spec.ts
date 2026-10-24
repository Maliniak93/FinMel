import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../format';
import { SummaryStrip } from './summary-strip';

describe('SummaryStrip', () => {
  it('renders each label with its amount and a dash for a missing one', async () => {
    await TestBed.configureTestingModule({
      imports: [SummaryStrip],
      providers: [provideI18nTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(SummaryStrip);
    fixture.componentRef.setInput('items', [
      { labelKey: 'accounts.summary.total', amount: 5000, testId: 'total' },
      { labelKey: 'accounts.summary.securities', amount: null },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const text = (element.textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('Total');
    expect(text).toContain('Securities');
    const total = (element.querySelector('[data-testid="total"]')?.textContent ?? '').replace(
      /\s+/g,
      ' ',
    );
    expect(total).toContain(formatMoney(5000).replace(/\s+/g, ' '));
    expect(text).toContain('—');
  });
});
