import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatMoney } from '../../../shared/format';
import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { ContributionLimitWarning } from './contribution-limit-warning';

const warning = {
  accountId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  accountName: 'IKE Broker',
  wrapper: 1,
  year: 2026,
  limitPln: 28260,
  contributedPln: 26000,
  plannedPln: 3000,
  exceededByPln: 740,
};

const otherWarning = {
  ...warning,
  accountId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  accountName: 'IKZE Broker',
  wrapper: 2,
  limitPln: 11304,
  contributedPln: 11000,
  plannedPln: 1000,
  exceededByPln: 696,
};

async function render(warnings: unknown[]) {
  await TestBed.configureTestingModule({
    imports: [ContributionLimitWarning],
    providers: [provideI18nTesting()],
  }).compileComponents();
  const fixture = TestBed.createComponent(ContributionLimitWarning);
  fixture.componentRef.setInput('warnings', warnings);
  await fixture.whenStable();
  return fixture;
}

describe('ContributionLimitWarning', () => {
  afterEach(async () => {
    await restoreEnglish();
  });

  it('renders each warning', async () => {
    const fixture = await render([warning, otherWarning]);

    await switchLanguage(fixture, 'pl');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('IKE Broker');
    expect(text).toContain('2026');
    expect(text).toContain(formatMoney(740));
    expect(text).toContain('IKZE Broker');
    expect(text).toContain(formatMoney(696));
    expect(text).toContain('przekroczą limit');
  });

  it('renders nothing without warnings', async () => {
    const fixture = await render([]);

    expect(((fixture.nativeElement as HTMLElement).textContent ?? '').trim()).toBe('');
  });
});
