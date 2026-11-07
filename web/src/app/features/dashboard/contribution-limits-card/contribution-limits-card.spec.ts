import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { formatMoney } from '../../../shared/format';
import { accountResponse, jsonResponse, renderCard } from '../testing/dashboard-fixtures';
import { ContributionLimitsCard } from './contribution-limits-card';

const IKE_ID = '11111111-1111-1111-1111-111111111111';
const IKZE_ID = '22222222-2222-2222-2222-222222222222';
const ARCHIVED_ID = '33333333-3333-3333-3333-333333333333';
const REGULAR_ID = '44444444-4444-4444-4444-444444444444';
const YEAR = new Date().getFullYear();

function contribution(contributedPln: number, limitPln: number | null) {
  return { year: YEAR, contributedPln, limitPln };
}

describe('ContributionLimitsCard', () => {
  afterEach(async () => {
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  it('shows remaining and exceeded', async () => {
    const { fixture } = await renderCard(ContributionLimitsCard, {
      accounts: jsonResponse([
        accountResponse({
          id: IKE_ID,
          name: 'IKE Broker',
          wrapper: 1,
          contribution: contribution(26000, 28260),
        }),
        accountResponse({
          id: IKZE_ID,
          name: 'IKZE Broker',
          wrapper: 2,
          contribution: contribution(11544, 11304),
        }),
        accountResponse({
          id: ARCHIVED_ID,
          name: 'IKE Stare',
          wrapper: 1,
          isArchived: true,
          contribution: contribution(100, 28260),
        }),
        accountResponse({
          id: REGULAR_ID,
          name: 'Zwykłe konto',
          wrapper: 0,
          contribution: null,
        }),
      ]),
    });

    await switchLanguage(fixture, 'pl');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('IKE Broker');
    expect(text).toContain(`Pozostało ${formatMoney(2260)}`);
    expect(text).toContain('IKZE Broker');
    expect(text).toContain(`Przekroczono o ${formatMoney(240)}`);
    expect(text).not.toContain('IKE Stare');
    expect(text).not.toContain('Zwykłe konto');
  });

  it('hides without limits', async () => {
    const { fixture } = await renderCard(ContributionLimitsCard, {
      accounts: jsonResponse([
        accountResponse({
          id: IKE_ID,
          name: 'IKE Broker',
          wrapper: 1,
          contribution: contribution(26000, null),
        }),
        accountResponse({ id: REGULAR_ID, name: 'Zwykłe konto', wrapper: 0, contribution: null }),
      ]),
    });

    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent?.trim() ?? '').toBe('');
    expect(element.querySelector('mat-card')).toBeNull();
  });
});
