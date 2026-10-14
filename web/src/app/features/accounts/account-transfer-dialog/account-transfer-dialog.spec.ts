import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import {
  findControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { AccountTransferDialog, type AccountTransferDialogData } from './account-transfer-dialog';

const sourceCashId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const targetCashId = 'dddddddd-dddd-dddd-dddd-dddddddddddd';

const data: AccountTransferDialogData = {
  account: {
    id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: 'XTB IKE',
    type: 0,
    wrapper: 1,
    currency: 'PLN',
    isArchived: false,
    cashAssetId: sourceCashId,
    cashBalance: 5000,
  },
};

const candidates = [
  { assetId: sourceCashId, name: 'XTB IKE', balance: 5000 },
  { assetId: targetCashId, name: 'Obligacje IKE', balance: 0 },
];

describe('AccountTransferDialog', () => {
  let fixture: ComponentFixture<AccountTransferDialog>;
  let component: AccountTransferDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    writeResponse: () => Response = () =>
      jsonResponse({ transferId: '44444444-4444-4444-4444-444444444444' }, 201),
    dialogData: AccountTransferDialogData = data,
  ): Promise<void> {
    // The dates the specs fill must not be in the future, whatever the real clock says.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20));
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if ((input as Request).method === 'GET') {
        return requestUrl(input).includes('/api/portfolio/transfer-candidates')
          ? jsonResponse(candidates)
          : jsonResponse({ detail: 'Not found.' }, 404);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [AccountTransferDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: dialogData },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AccountTransferDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function form(): FormGroup {
    return component['form'] as FormGroup;
  }

  async function fill(values: Record<string, unknown>): Promise<void> {
    for (const [name, value] of Object.entries(values)) {
      const control = findControl(form(), name);
      control.setValue(value);
      control.markAsDirty();
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function writes(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method !== 'GET');
  }

  it('loads the account cash candidates in PLN', async () => {
    await setup();

    const url = new URL(
      requestUrl(
        fetchSpy.mock.calls
          .map((call: unknown[]) => call[0])
          .find((input: unknown) => requestUrl(input).includes('/transfer-candidates')),
      ),
    );
    expect(url.searchParams.get('accountCash')).toBe('true');
    expect(url.searchParams.get('currency')).toBe('PLN');
  });

  it.each([
    ['no target account', { targetAssetId: null, amount: 100 }],
    ['an amount of 0', { targetAssetId: targetCashId, amount: 0 }],
    ['an empty amount', { targetAssetId: targetCashId, amount: null }],
  ])('%s blocks submit', async (_case, values) => {
    await setup();

    await fill(values);
    await component['onSubmit']();

    expect(form().invalid).toBe(true);
    expect(writes()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('submit POSTs this account cash as the source and closes with true', async () => {
    await setup();
    await fill({ targetAssetId: targetCashId, amount: 3000, date: new Date(2026, 9, 15) });

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(request.url).toContain('/api/portfolio/transfers');
    expect(await request.json()).toEqual({
      sourceAssetId: sourceCashId,
      targetAssetId: targetCashId,
      amount: 3000,
      date: '2026-10-15',
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('a server 400 InsufficientFunds shows the message inline and keeps the dialog open', async () => {
    await setup(() =>
      jsonResponse(
        {
          title: 'Validation Failed',
          status: 400,
          detail: 'The source balance would drop below zero.',
          errorCode: 'Validation.InsufficientFunds',
        },
        400,
      ),
    );
    await fill({ targetAssetId: targetCashId, amount: 6000 });

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(renderedText(fixture)).toMatch(/insufficient funds/i);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('loads the candidates in the currency of the account', async () => {
    await setup(undefined, { account: { ...data.account, currency: 'USD' } });

    const url = new URL(
      requestUrl(
        fetchSpy.mock.calls
          .map((call: unknown[]) => call[0])
          .find((input: unknown) => requestUrl(input).includes('/transfer-candidates')),
      ),
    );
    expect(url.searchParams.get('currency')).toBe('USD');
  });

  it('a server 400 TransferCurrencyMismatch shows "Różne waluty" and keeps the dialog open', async () => {
    await setup(() =>
      jsonResponse(
        {
          title: 'Validation Failed',
          status: 400,
          detail: 'The accounts hold different currencies.',
          errorCode: 'Validation.TransferCurrencyMismatch',
        },
        400,
      ),
    );
    await switchLanguage(fixture, 'pl');
    await fill({ targetAssetId: targetCashId, amount: 100 });

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(renderedText(fixture)).toContain('Różne waluty');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
