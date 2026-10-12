import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { restoreEnglish, translationValues } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import {
  findControl,
  jsonResponse,
  renderedText,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { CashOperationDialog, type CashOperationDialogData } from './cash-operation-dialog';

const cashAssetId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

describe('CashOperationDialog', () => {
  let fixture: ComponentFixture<CashOperationDialog>;
  let component: CashOperationDialog;
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
    data: CashOperationDialogData = { cashAssetId, kind: 'deposit' },
    writeResponse: () => Response = () => jsonResponse({ id: 'tx' }, 201),
  ): Promise<void> {
    // The dates the specs fill must not be in the future, whatever the real clock says.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20));
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => writeResponse());

    await TestBed.configureTestingModule({
      imports: [CashOperationDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CashOperationDialog);
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

  it.each([
    ['an empty amount', { amount: null }],
    ['an amount of 0', { amount: 0 }],
  ])('%s blocks submit', async (_case, values) => {
    await setup();

    await fill(values);
    await component['onSubmit']();

    expect(form().invalid).toBe(true);
    expect(writes()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('a deposit POSTs a Deposit of the amount at unit price 1 on the cash asset and closes with true', async () => {
    await setup({ cashAssetId, kind: 'deposit' });
    await fill({ amount: 5000, date: new Date(2026, 9, 12) });

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/assets/${cashAssetId}/transactions`);
    expect(await request.json()).toEqual({
      type: 2,
      quantity: 5000,
      unitPrice: 1,
      date: '2026-10-12',
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('a withdrawal POSTs a Withdraw', async () => {
    await setup({ cashAssetId, kind: 'withdraw' });
    await fill({ amount: 1000, date: new Date(2026, 9, 13) });

    await component['onSubmit']();

    const [request] = writes();
    expect(await request.json()).toEqual({
      type: 3,
      quantity: 1000,
      unitPrice: 1,
      date: '2026-10-13',
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('a server 400 InsufficientFunds shows the message inline and keeps the dialog open', async () => {
    await setup({ cashAssetId, kind: 'withdraw' }, () =>
      jsonResponse(
        {
          title: 'Validation Failed',
          status: 400,
          detail: 'The balance would drop below zero.',
          errorCode: 'Validation.InsufficientFunds',
        },
        400,
      ),
    );
    await fill({ amount: 100 });

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(renderedText(fixture)).toMatch(/insufficient funds/i);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('has the Polish inline message "Brak środków na rachunku"', async () => {
    await setup();

    expect(translationValues('pl')).toContain('Brak środków na rachunku');
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
