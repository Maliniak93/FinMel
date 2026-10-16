import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { bankTerms } from '../../../../testing/bank-account-fixtures';
import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { client as reportingClient } from '../../../api/reporting/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../../shared/format';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
import { CashBankTransferDialog } from '../../cash/cash-bank-transfer-dialog/cash-bank-transfer-dialog';
import { depositResponse } from '../../deposits/testing/deposit-fixtures';
import { AccountTransferDialog } from '../account-transfer-dialog/account-transfer-dialog';
import { CashOperationDialog } from '../cash-operation-dialog/cash-operation-dialog';
import { ContributionLimitDialog } from '../contribution-limit-dialog/contribution-limit-dialog';
import { AccountDetail } from './account-detail';

const accountId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const cashAssetId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const etfAssetId = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee';
const depositId = '11111111-1111-1111-1111-111111111111';
const withdrawId = '22222222-2222-2222-2222-222222222222';
const transferLegId = '33333333-3333-3333-3333-333333333333';
const transferId = '44444444-4444-4444-4444-444444444444';
const incomeDepositId = '55555555-5555-5555-5555-555555555555';

const account = {
  id: accountId,
  name: 'XTB IKE',
  type: 0,
  wrapper: 1,
  isArchived: false,
  cashAssetId,
  cashBalance: 2000,
};

const holdings = [
  {
    id: etfAssetId,
    accountId,
    assetClass: 3,
    valuationMode: 0,
    name: 'World ETF',
    currency: 'PLN',
    quantity: 12,
    transactionCount: 1,
    isArchived: false,
  },
];

const transactions = {
  items: [
    {
      id: depositId,
      assetId: cashAssetId,
      type: 2,
      quantity: 5000,
      unitPrice: 1,
      currency: 'PLN',
      date: '2026-10-12',
    },
    {
      id: withdrawId,
      assetId: cashAssetId,
      type: 3,
      quantity: 1000,
      unitPrice: 1,
      currency: 'PLN',
      date: '2026-10-13',
    },
    {
      id: transferLegId,
      assetId: cashAssetId,
      type: 3,
      quantity: 2000,
      unitPrice: 1,
      currency: 'PLN',
      date: '2026-10-15',
      transfer: {
        transferId,
        manual: true,
        counterpartAssetId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
        counterpartAssetName: 'Obligacje IKE',
        counterpartAccountName: 'Obligacje IKE',
        direction: 1,
      },
    },
    {
      id: incomeDepositId,
      assetId: cashAssetId,
      type: 2,
      quantity: 9143.27,
      unitPrice: 1,
      currency: 'PLN',
      date: '2026-10-16',
      recurringIncomeName: 'Wypłata Dominik',
    },
  ],
  page: 1,
  pageSize: 100,
  totalCount: 4,
};

const fundedDeposit = depositResponse({
  assetId: '77777777-7777-7777-7777-777777777777',
  name: 'Autumn deposit',
  sourceAccountId: accountId,
  sourceAccountName: 'mBank',
  returnAccountId: accountId,
  returnAccountName: 'mBank',
});

const dashboard = {
  netWorthPln: 5000,
  asOf: '2026-10-15',
  isStale: false,
  byAssetClass: [],
  byAccount: [
    {
      accountId,
      valuePln: 5000,
      cashPln: 2000,
      percentage: 100,
      snapshotDate: '2026-10-15',
      isStale: false,
    },
  ],
};

describe('AccountDetail', () => {
  let fixture: ComponentFixture<AccountDetail>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(accountOverrides: Record<string, unknown> = {}): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      const url = requestUrl(input);
      if (request.method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      if (url.includes(`/api/portfolio/assets/${cashAssetId}/transactions`)) {
        return jsonResponse(transactions);
      }
      if (url.includes('/api/portfolio/assets') && url.includes(`accountId=${accountId}`)) {
        return jsonResponse(holdings);
      }
      if (url.includes('/api/portfolio/deposits')) {
        return jsonResponse([fundedDeposit]);
      }
      if (url.includes(`/api/portfolio/accounts/${accountId}`)) {
        return jsonResponse({ ...account, ...accountOverrides });
      }
      if (url.includes('/api/reporting/dashboard')) {
        return jsonResponse(dashboard);
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [AccountDetail],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AccountDetail);
    fixture.componentRef.setInput('id', accountId);
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(): string {
    return (root().textContent ?? '').replace(/\s+/g, ' ');
  }

  function action(label: RegExp): HTMLButtonElement {
    const button = Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      label.test((b.textContent ?? '').trim()),
    );
    if (!button) {
      throw new Error(`No button matching ${label}.`);
    }
    return button;
  }

  function historyRow(id: string): HTMLElement {
    const row = root().querySelector<HTMLElement>(`[data-transaction-id="${id}"]`);
    if (!row) {
      throw new Error(`No history row for ${id}.`);
    }
    return row;
  }

  function rowButton(row: HTMLElement, label: RegExp): HTMLButtonElement | undefined {
    return Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      label.test(`${b.getAttribute('aria-label') ?? ''} ${b.textContent ?? ''}`),
    );
  }

  function getCalls(fragment: string): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        (typeof call[0] === 'string' ? 'GET' : (call[0] as Request).method) === 'GET' &&
        requestUrl(call[0]).includes(fragment),
    ).length;
  }

  it('shows the cash from the account, the total from the dashboard and securities as the difference', async () => {
    await setup();

    expect(text()).toContain('XTB IKE');
    expect(text()).toContain(formatMoney(2000, 'PLN').replace(/\s+/g, ' '));
    expect(text()).toContain(formatMoney(3000, 'PLN').replace(/\s+/g, ' '));
    expect(text()).toContain(formatMoney(5000, 'PLN').replace(/\s+/g, ' '));
  });

  it('lists the deposits a bank account funds or receives and links to the deposits page', async () => {
    await setup({ type: 2, wrapper: 0, currency: 'PLN', bank: bankTerms() });

    const request = fetchSpy.mock.calls
      .map((call: unknown[]) => requestUrl(call[0]))
      .find((url: string) => url.includes('/api/portfolio/deposits'));
    expect(request).toBeDefined();
    expect(new URL(request!).searchParams.get('accountId')).toBe(accountId);
    expect(text()).toContain('Autumn deposit');
    expect(root().querySelector('a[href="/deposits"]')).not.toBeNull();
  });

  it('does not look for deposits on a brokerage account', async () => {
    await setup();

    expect(getCalls('/api/portfolio/deposits')).toBe(0);
  });

  it('lists the holdings and links each to its transactions', async () => {
    await setup();

    const link = root().querySelector<HTMLAnchorElement>(
      `a[href="/assets/${etfAssetId}/transactions"]`,
    );
    expect(link?.textContent).toContain('World ETF');
  });

  it('shows the cash history with the counterpart of a transfer and offers edit only on deposits and withdrawals', async () => {
    await setup();

    expect(rowButton(historyRow(depositId), /edit/i)).toBeDefined();
    expect(rowButton(historyRow(depositId), /delete/i)).toBeDefined();
    expect(rowButton(historyRow(withdrawId), /edit/i)).toBeDefined();
    expect(rowButton(historyRow(withdrawId), /delete/i)).toBeDefined();
    const transferRow = historyRow(transferLegId);
    expect(transferRow.textContent).toContain('Obligacje IKE');
    expect(rowButton(transferRow, /edit/i)).toBeUndefined();
    expect(rowButton(transferRow, /delete/i)).toBeDefined();
  });

  it('labels a settlement deposit with the income name and offers no edit or delete', async () => {
    await setup();

    const incomeRow = historyRow(incomeDepositId);
    expect(incomeRow.textContent).toContain('Income · Wypłata Dominik');
    expect(rowButton(incomeRow, /edit/i)).toBeUndefined();
    expect(rowButton(incomeRow, /delete/i)).toBeUndefined();
  });

  it('Deposit and Withdraw open the cash operation dialog and reload after it saves', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const accountReadsBefore = getCalls(`/api/portfolio/accounts/${accountId}`);

    action(/^deposit$/i).click();
    await fixture.whenStable();
    action(/^withdraw$/i).click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(2);
    const [[depositDialog, depositConfig], [withdrawDialog, withdrawConfig]] = dialog.open.mock
      .calls as [[unknown, { data: unknown }], [unknown, { data: unknown }]];
    expect(depositDialog).toBe(CashOperationDialog);
    expect(depositConfig.data).toMatchObject({ cashAssetId, kind: 'deposit' });
    expect(withdrawDialog).toBe(CashOperationDialog);
    expect(withdrawConfig.data).toMatchObject({ cashAssetId, kind: 'withdraw' });
    await vi.waitFor(() =>
      expect(getCalls(`/api/portfolio/accounts/${accountId}`)).toBeGreaterThan(accountReadsBefore),
    );
  });

  it('Transfer opens the account transfer dialog for this account', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(undefined) });

    action(/^transfer$/i).click();
    await fixture.whenStable();

    const [dialogType, config] = dialog.open.mock.calls[0] as [
      unknown,
      { data: { account: unknown } },
    ];
    expect(dialogType).toBe(AccountTransferDialog);
    expect(config.data.account).toMatchObject({ id: accountId, cashAssetId });
  });

  it('a bank account offers the ATM withdrawal and the bank deposit from its side', async () => {
    await setup({ type: 2, wrapper: 0, currency: 'PLN', bank: bankTerms() });
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const accountReadsBefore = getCalls(`/api/portfolio/accounts/${accountId}`);

    action(/^atm withdrawal$/i).click();
    await fixture.whenStable();
    action(/^deposit to bank$/i).click();
    await fixture.whenStable();

    const [[atmDialog, atmConfig], [depositDialog, depositConfig]] = dialog.open.mock.calls as [
      [unknown, { data: unknown }],
      [unknown, { data: unknown }],
    ];
    expect(atmDialog).toBe(CashBankTransferDialog);
    expect(atmConfig.data).toMatchObject({
      direction: 'atm',
      anchor: 'bank',
      anchorAssetId: cashAssetId,
      currency: 'PLN',
    });
    expect(depositDialog).toBe(CashBankTransferDialog);
    expect(depositConfig.data).toMatchObject({ direction: 'bankDeposit', anchor: 'bank' });
    await vi.waitFor(() =>
      expect(getCalls(`/api/portfolio/accounts/${accountId}`)).toBeGreaterThan(accountReadsBefore),
    );
  });

  it('a brokerage account has no ATM withdrawal or bank deposit', async () => {
    await setup();

    expect(() => action(/^atm withdrawal$/i)).toThrow();
    expect(() => action(/^deposit to bank$/i)).toThrow();
  });

  it('deleting a transfer asks for confirmation, calls DELETE /transfers/{id} and reloads', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const historyReadsBefore = getCalls(`/api/portfolio/assets/${cashAssetId}/transactions`);

    rowButton(historyRow(transferLegId), /delete/i)!.click();
    await fixture.whenStable();

    expect(dialog.open.mock.calls[0][0]).toBe(ConfirmDialog);
    const deleteCall = fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .find((request: Request) => request.method === 'DELETE');
    expect(deleteCall?.url).toContain(`/api/portfolio/transfers/${transferId}`);
    await vi.waitFor(() =>
      expect(getCalls(`/api/portfolio/assets/${cashAssetId}/transactions`)).toBeGreaterThan(
        historyReadsBefore,
      ),
    );
  });

  it('an archived account shows the badge and disables Deposit, Withdraw and Transfer', async () => {
    await setup({ isArchived: true });

    expect(text()).toMatch(/archived/i);
    expect(action(/^deposit$/i).disabled).toBe(true);
    expect(action(/^withdraw$/i).disabled).toBe(true);
    expect(action(/^transfer$/i).disabled).toBe(true);
  });

  it('an IKE account shows the contributions of the year against its limit in a bar', async () => {
    await setup({
      wrapper: 1,
      contribution: { year: 2026, contributedPln: 12000, limitPln: 26000 },
    });
    await switchLanguage(fixture, 'pl');

    expect(text()).toContain('Wpłacono 12 000,00 / 26 000,00 zł w 2026');
    expect(root().querySelector('[role="progressbar"]')).not.toBeNull();
  });

  it('an IKE account without a limit shows only the sum of the year and no bar', async () => {
    await setup({
      wrapper: 1,
      contribution: { year: 2026, contributedPln: 12000, limitPln: null },
    });
    await switchLanguage(fixture, 'pl');

    expect(text()).toContain('Wpłacono 12 000,00 zł w 2026');
    expect(root().querySelector('[role="progressbar"]')).toBeNull();
  });

  it('Limit wpłat opens the contribution limit dialog for this account and year', async () => {
    await setup({
      wrapper: 1,
      contribution: { year: 2026, contributedPln: 12000, limitPln: 26000 },
    });
    await switchLanguage(fixture, 'pl');
    dialog.open.mockReturnValue({ afterClosed: () => of(undefined) });

    action(/limit wpłat/i).click();
    await fixture.whenStable();

    const [dialogType, config] = dialog.open.mock.calls[0] as [unknown, { data: unknown }];
    expect(dialogType).toBe(ContributionLimitDialog);
    expect(config.data).toMatchObject({ accountId, year: 2026, limitPln: 26000 });
  });
});
