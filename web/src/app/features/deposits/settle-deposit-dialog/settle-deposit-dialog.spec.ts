import { formatDate } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format-money';
import {
  findControl,
  hasControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  dueDeposit,
  dueDepositSettlementPreview,
  settledDeposit,
} from '../testing/deposit-fixtures';
import { SettleDepositDialog } from './settle-deposit-dialog';

// term-deposits-settlement AC-9. The settle dialog opens on a Due deposit, loads its settlement
// preview (GET .../deposits/{assetId}/settlement-preview) and pre-fills the editable settlement date,
// gross interest and tax from it; principal and maturity are shown read-only; net interest and the
// final amount (principal + gross − tax) follow the edits live. Its controls are named after the
// SettleDepositRequest properties (camelCase) so a server 400 keyed on a field lands on it. The
// submit under test is called on the dialog itself and asserted on the raw request the generated
// client hands to `fetch`.
describe('SettleDepositDialog', () => {
  let fixture: ComponentFixture<SettleDepositDialog>;
  let component: SettleDepositDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  function method(input: unknown): string {
    return typeof input === 'string' ? 'GET' : (input as Request).method;
  }

  // The preview GET always answers with `dueDepositSettlementPreview`; every write answers with
  // `writeResponse`.
  async function setup(
    writeResponse: () => Response = () => jsonResponse(settledDeposit),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (method(input) === 'GET') {
        return requestUrl(input).includes('/settlement-preview')
          ? jsonResponse(dueDepositSettlementPreview)
          : jsonResponse({ detail: 'Not found.' }, 404);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [SettleDepositDialog],
      providers: [
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { deposit: dueDeposit } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettleDepositDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    // The form is pre-filled once the preview has loaded.
    await vi.waitFor(() =>
      expect(Number(findControl(form(), 'grossInterest').value)).toBe(
        dueDepositSettlementPreview.grossInterest,
      ),
    );
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

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => method(input) !== 'GET') as Request[];
  }

  function mediumDate(year: number, month: number, day: number): string {
    return formatDate(new Date(year, month - 1, day), 'mediumDate', 'en-US');
  }

  function daysFromToday(days: number): Date {
    const date = new Date();
    date.setHours(0, 0, 0, 0);
    date.setDate(date.getDate() + days);
    return date;
  }

  it('pre-fills the settlement from the preview and shows principal and maturity read-only', async () => {
    await setup();

    const previewCall = fetchSpy.mock.calls
      .map((call: unknown[]) => requestUrl(call[0]))
      .find((url: string) => url.includes('/settlement-preview'));
    expect(previewCall).toContain(
      `/api/portfolio/portfolios/${dueDeposit.portfolioId}/deposits/${dueDeposit.assetId}/settlement-preview`,
    );

    const settledOn = findControl(form(), 'settledOn').value as Date;
    expect(toDateOnly(settledOn)).toBe('2026-04-15');
    expect(Number(findControl(form(), 'grossInterest').value)).toBe(147.95);
    expect(Number(findControl(form(), 'tax').value)).toBe(28.12);

    // Principal and maturity are shown, never inputs the user could type into.
    const text = renderedText(fixture);
    expect(text).toContain(formatMoney(10000, 'PLN'));
    expect(text).toContain(mediumDate(2026, 4, 15));
    expect(hasControl(form(), 'principal')).toBe(false);
    expect(hasControl(form(), 'maturityDate')).toBe(false);
    // Net interest and the final amount of the previewed values.
    expect(text).toContain(formatMoney(119.83, 'PLN'));
    expect(text).toContain(formatMoney(10119.83, 'PLN'));
  });

  it('recomputes net interest and the final amount live as gross and tax change', async () => {
    await setup();

    await fill({ grossInterest: 150, tax: 28.5 });

    const text = renderedText(fixture);
    expect(text).toContain(formatMoney(121.5, 'PLN'));
    expect(text).toContain(formatMoney(10121.5, 'PLN'));
    expect(text).not.toContain(formatMoney(10119.83, 'PLN'));

    await fill({ tax: 0 });
    expect(renderedText(fixture)).toContain(formatMoney(10150, 'PLN'));
    expect(writeRequests()).toEqual([]);
  });

  it.each([
    ['tax above gross', { grossInterest: 100, tax: 100.01 }],
    ['a negative gross', { grossInterest: -1, tax: 0 }],
    ['a negative tax', { tax: -0.01 }],
    ['a settlement date in the future', { settledOn: daysFromToday(1) }],
    ['a settlement date before the start', { settledOn: new Date(2026, 0, 14) }],
  ])('%s blocks submit', async (_case, values) => {
    await setup();

    await fill(values);
    await component['onSubmit']();

    expect(form().invalid).toBe(true);
    expect(writeRequests()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('submit POSTs the edited settlement and closes with true', async () => {
    await setup();
    await fill({ settledOn: new Date(2026, 3, 17), grossInterest: 150, tax: 28.5 });

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${dueDeposit.portfolioId}/deposits/${dueDeposit.assetId}/settle`,
    );
    expect(await request.json()).toEqual({
      settledOn: '2026-04-17',
      grossInterest: 150,
      tax: 28.5,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('submit with the untouched preview POSTs the previewed values', async () => {
    await setup();

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(await request.json()).toEqual({
      settledOn: '2026-04-15',
      grossInterest: 147.95,
      tax: 28.12,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('server 400 field errors land on the matching fields', async () => {
    await setup(() =>
      jsonResponse(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { Tax: ['The tax can not exceed the gross interest.'] },
        },
        400,
      ),
    );

    await component['onSubmit']();

    expect(findControl(form(), 'tax').hasError('server')).toBe(true);
    expect(findControl(form(), 'grossInterest').hasError('server')).toBe(false);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('a 409 lands on the banner', async () => {
    await setup(() =>
      jsonResponse(
        {
          detail: 'This deposit is already settled.',
          errorCode: 'Conflict.DepositAlreadySettled',
        },
        409,
      ),
    );

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(banner?.textContent).toContain('This deposit is already settled.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
