import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { labelsOf, polishProblems, restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import {
  findControl,
  hasControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  nestAccount,
  paidOutDeposit,
  requestMethod,
  settledDeposit,
  settledDepositFinalAmount,
  writeRequests,
} from '../testing/deposit-fixtures';
import { PayOutDepositDialog } from './pay-out-deposit-dialog';

describe('PayOutDepositDialog', () => {
  let fixture: ComponentFixture<PayOutDepositDialog>;
  let component: PayOutDepositDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    writeResponse: () => Response = () => jsonResponse(paidOutDeposit),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (requestMethod(input) === 'GET') {
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [PayOutDepositDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { deposit: settledDeposit } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PayOutDepositDialog);
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

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function daysFromToday(days: number): Date {
    const date = new Date();
    date.setHours(0, 0, 0, 0);
    date.setDate(date.getDate() + days);
    return date;
  }

  it('shows the whole balance as a read-only amount', async () => {
    await setup();

    if (hasControl(form(), 'amount')) {
      const amount = findControl(form(), 'amount');
      expect(amount.disabled).toBe(true);
      expect(Number(amount.value)).toBe(settledDepositFinalAmount);
    } else {
      expect(renderedText(fixture)).toContain(
        formatMoney(settledDepositFinalAmount, settledDeposit.currency),
      );
    }
  });

  it('names the return account instead of asking for a destination', async () => {
    await setup();

    expect(renderedText(fixture)).toContain(nestAccount.name);
    expect(hasControl(form(), 'destinationAssetId')).toBe(false);
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-select')).toBeNull();
    expect(
      fetchSpy.mock.calls.some((call: unknown[]) =>
        requestUrl(call[0]).includes('/api/portfolio/transfer-candidates'),
      ),
    ).toBe(false);
  });

  it('defaults the date to today', async () => {
    await setup();

    const date = findControl(form(), 'date').value as Date;
    expect(toDateOnly(date)).toBe(toDateOnly(daysFromToday(0)));
  });

  it.each([
    ['a date before the settlement date', { date: new Date(2026, 3, 16) }],
    ['a date in the future', { date: daysFromToday(1) }],
  ])('%s blocks submit', async (_case, values) => {
    await setup();

    await fill(values);
    await component['onSubmit']();

    expect(form().invalid).toBe(true);
    expect(writeRequests(fetchSpy)).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('the settlement date itself is a valid payout date', async () => {
    await setup();

    await fill({ date: new Date(2026, 3, 17) });

    expect(form().valid).toBe(true);
  });

  it('submit POSTs only the date and closes with true', async () => {
    await setup();
    await fill({ date: new Date(2026, 3, 18) });

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/deposits/${settledDeposit.assetId}/payout`);
    expect(await request.json()).toEqual({ date: '2026-04-18' });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('a server 409 is shown and the dialog stays open', async () => {
    const detail = 'This deposit is already paid out.';
    await setup(() =>
      jsonResponse({ status: 409, detail, errorCode: 'Conflict.DepositAlreadyPaidOut' }, 409),
    );

    await component['onSubmit']();
    await render();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(banner?.textContent).toContain(detail);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  describe('in Polish', () => {
    function errors(): string[] {
      return labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');
    }

    it('shows validation in Polish', async () => {
      await setup();
      const scenarios: [string, Record<string, unknown>][] = [
        ["Date can't be before the settlement date.", { date: new Date(2026, 3, 16) }],
        ["Date can't be in the future.", { date: daysFromToday(1) }],
      ];
      const read = async (values: Record<string, unknown>) => {
        await fill(values);
        await component['onSubmit']();
        await render();
        return errors();
      };

      const english: string[][] = [];
      for (const [message, values] of scenarios) {
        english.push(await read(values));
        expect(english.at(-1)).toEqual([message]);
      }

      await switchLanguage(fixture, 'pl');

      for (const [index, [, values]] of scenarios.entries()) {
        expect(polishProblems(english[index], await read(values))).toEqual([]);
      }
    });
  });
});
