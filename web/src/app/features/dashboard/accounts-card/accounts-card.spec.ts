import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { formatMoney, formatPercent } from '../../../shared/format';
import { jsonResponse, accountResponse, renderCard } from '../testing/dashboard-fixtures';
import type { DashboardResponse } from '../../../api/reporting';
import { AccountsCard } from './accounts-card';

const BROKERAGE_ID = '11111111-1111-1111-1111-111111111111';
const BONDS_ID = '33333333-3333-3333-3333-333333333333';
const ARCHIVED_ID = '44444444-4444-4444-4444-444444444444';

const accounts = [
  accountResponse({ id: BROKERAGE_ID, name: 'XTB IKE', type: 0, wrapper: 1 }),
  accountResponse({ id: BONDS_ID, name: 'Obligacje IKE', type: 1, wrapper: 1 }),
  accountResponse({ id: ARCHIVED_ID, name: 'Old brokerage', isArchived: true }),
];

const withOutsideAccounts: DashboardResponse = {
  netWorthPln: 2000,
  asOf: '2026-08-09',
  isStale: false,
  byAssetClass: [],
  byAccount: [
    {
      accountId: BROKERAGE_ID,
      valuePln: 1000,
      snapshotDate: '2026-08-09',
      isStale: false,
      percentage: 50,
    },
    {
      accountId: BONDS_ID,
      valuePln: 600,
      snapshotDate: '2026-08-09',
      isStale: false,
      percentage: 30,
    },
    {
      accountId: null,
      valuePln: 400,
      snapshotDate: '2026-08-09',
      isStale: false,
      percentage: 20,
    },
  ],
  byGroup: [],
  byAsset: [],
};

describe('AccountsCard', () => {
  afterEach(async () => {
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  it('lists accounts with value and share and a row for assets outside accounts', async () => {
    const { fixture } = await renderCard(AccountsCard, {
      accounts: jsonResponse(accounts),
      dashboard: jsonResponse(withOutsideAccounts),
    });
    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';

    expect(text).toContain('XTB IKE');
    expect(text).toContain(formatMoney(1000));
    expect(text).toContain(formatPercent(50));
    expect(text).toContain('Obligacje IKE');
    expect(text).toContain(formatMoney(600));
    expect(text).toContain(formatPercent(30));
    expect(text).toContain(formatMoney(400));
    expect(text).toContain(formatPercent(20));
    expect(text).not.toContain('Old brokerage');
  });

  it('links each row to its account page', async () => {
    const { fixture } = await renderCard(AccountsCard, {
      accounts: jsonResponse(accounts),
      dashboard: jsonResponse(withOutsideAccounts),
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector(`a[href="/accounts/${BROKERAGE_ID}"]`)?.textContent).toContain(
      'XTB IKE',
    );
    expect(element.querySelector(`a[href="/accounts/${BONDS_ID}"]`)?.textContent).toContain(
      'Obligacje IKE',
    );
  });

  it('links a Bank account to bank-accounts/:id', async () => {
    const bankId = '55555555-5555-5555-5555-555555555555';
    const { fixture } = await renderCard(AccountsCard, {
      accounts: jsonResponse([
        accountResponse({ id: BROKERAGE_ID, name: 'XTB IKE', type: 0, wrapper: 1 }),
        accountResponse({ id: bankId, name: 'mBank', type: 2 }),
      ]),
      dashboard: jsonResponse({
        ...withOutsideAccounts,
        byAccount: [
          { ...withOutsideAccounts.byAccount[0] },
          { ...withOutsideAccounts.byAccount[0], accountId: bankId, valuePln: 300 },
        ],
      }),
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector(`a[href="/bank-accounts/${bankId}"]`)?.textContent).toContain(
      'mBank',
    );
    expect(element.querySelector(`a[href="/accounts/${BROKERAGE_ID}"]`)?.textContent).toContain(
      'XTB IKE',
    );
  });

  it('shows the Poza rachunkami row in Polish', async () => {
    const { fixture } = await renderCard(AccountsCard, {
      accounts: jsonResponse(accounts),
      dashboard: jsonResponse(withOutsideAccounts),
    });

    await switchLanguage(fixture, 'pl');

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Poza rachunkami');
  });
});
