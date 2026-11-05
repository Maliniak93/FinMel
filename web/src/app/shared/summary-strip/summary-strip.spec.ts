import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { readStylesheet, rulesFor } from '../../../testing/stylesheet';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../format';
import { SummaryStrip } from './summary-strip';

describe('SummaryStrip', () => {
  it('renders cells and an optional link', async () => {
    await TestBed.configureTestingModule({
      imports: [SummaryStrip],
      providers: [provideI18nTesting(), provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(SummaryStrip);
    fixture.componentRef.setInput('items', [
      { labelKey: 'accounts.summary.total', amount: 5000 },
      {
        labelKey: 'accounts.summary.securities',
        amount: 3000,
        link: { labelKey: 'wealth.action.open', route: '/wealth' },
      },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const cells = element.querySelectorAll('.summary-strip__item');
    expect(cells).toHaveLength(2);
    expect(cells[0].querySelector('.summary-strip__link')).toBeNull();
    const link = cells[1].querySelector<HTMLAnchorElement>('.summary-strip__link');
    expect(link?.getAttribute('href')).toBe('/wealth');
    const value = cells[1].querySelector('.summary-strip__value')!;
    expect(value.compareDocumentPosition(link!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    const scss = readStylesheet('src/app/shared/summary-strip/summary-strip.scss');
    expect(scss).not.toMatch(/border-radius|background/);
    expect(scss).not.toMatch(/[{;\s]border:\s*(?!none)/);
    expect(scss).toMatch(/border-top:\s*1px solid var\(--mat-sys-outline-variant\)/);
    expect(scss).toMatch(/border-bottom:\s*1px solid var\(--mat-sys-outline-variant\)/);
    expect(scss).toMatch(/border-left:\s*1px solid var\(--mat-sys-outline-variant\)/);
    const valueRules = rulesFor(scss, /summary-strip__value/);
    expect(valueRules).toMatch(/font-size:\s*28px/);
    expect(valueRules).toMatch(/brand-family|headline/);
  });

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
