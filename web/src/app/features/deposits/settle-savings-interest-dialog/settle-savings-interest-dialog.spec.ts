import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { restoreEnglish } from '../../../../testing/i18n';
import { formatDate, formatMoney, formatPercent } from '../../../shared/format';
import {
  findControl,
  hasControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { dueSavingsAccount, savingsInterestPreview } from '../testing/savings-account-fixtures';
import { SettleSavingsInterestDialog } from './settle-savings-interest-dialog';

describe('SettleSavingsInterestDialog', () => {
  let fixture: ComponentFixture<SettleSavingsInterestDialog>;
  let component: SettleSavingsInterestDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  function method(input: unknown): string {
    return typeof input === 'string' ? 'GET' : (input as Request).method;
  }

  async function setup(
    writeResponse: () => Response = () => jsonResponse({ settlementId: 'x' }, 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (method(input) === 'GET') {
        return requestUrl(input).includes('/interest-preview')
          ? jsonResponse(savingsInterestPreview)
          : jsonResponse({ detail: 'Not found.' }, 404);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [SettleSavingsInterestDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { account: dueSavingsAccount } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettleSavingsInterestDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    await vi.waitFor(() =>
      expect(Number(findControl(form(), 'grossInterest').value)).toBe(
        savingsInterestPreview.grossInterest,
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

  it('pre-fills gross and tax from the preview and shows the period, rate and average balance read-only', async () => {
    await setup();

    const previewCall = fetchSpy.mock.calls
      .map((call: unknown[]) => requestUrl(call[0]))
      .find((url: string) => url.includes('/interest-preview'));
    expect(previewCall).toContain(
      `/api/portfolio/portfolios/${dueSavingsAccount.portfolioId}/savings-accounts/${dueSavingsAccount.assetId}/interest-preview`,
    );

    expect(Number(findControl(form(), 'grossInterest').value)).toBe(41.1);
    expect(Number(findControl(form(), 'tax').value)).toBe(7.81);

    const text = renderedText(fixture);
    expect(text).toContain(formatDate('2026-09-01'));
    expect(text).toContain(formatDate('2026-09-30'));
    expect(text).toContain(formatPercent(5));
    expect(text).toContain(formatMoney(10000, 'PLN'));
    expect(hasControl(form(), 'periodStart')).toBe(false);
    expect(hasControl(form(), 'periodEnd')).toBe(false);
    expect(hasControl(form(), 'annualInterestRatePercent')).toBe(false);
    expect(hasControl(form(), 'averageDailyBalance')).toBe(false);
    expect(text).toContain(formatMoney(33.29, 'PLN'));
  });

  it('recomputes the net live as gross and tax change', async () => {
    await setup();

    await fill({ grossInterest: 50, tax: 9.5 });

    const text = renderedText(fixture);
    expect(text).toContain(formatMoney(40.5, 'PLN'));
    expect(text).not.toContain(formatMoney(33.29, 'PLN'));

    await fill({ tax: 0 });
    expect(renderedText(fixture)).toContain(formatMoney(50, 'PLN'));
    expect(writeRequests()).toEqual([]);
  });

  it.each([
    ['tax above gross', { grossInterest: 10, tax: 10.01 }],
    ['a negative gross', { grossInterest: -1, tax: 0 }],
    ['a negative tax', { tax: -0.01 }],
  ])('%s blocks submit', async (_case, values) => {
    await setup();

    await fill(values);
    await component['onSubmit']();

    expect(form().invalid).toBe(true);
    expect(writeRequests()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('submit with the untouched preview POSTs the previewed values and closes with true', async () => {
    await setup();

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${dueSavingsAccount.portfolioId}/savings-accounts/${dueSavingsAccount.assetId}/interest-settlements`,
    );
    expect(await request.json()).toEqual({
      periodEnd: '2026-09-30',
      grossInterest: 41.1,
      tax: 7.81,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('submit POSTs the edited amounts', async () => {
    await setup();
    await fill({ grossInterest: 50, tax: 9.5 });

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(await request.json()).toEqual({
      periodEnd: '2026-09-30',
      grossInterest: 50,
      tax: 9.5,
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
          detail: 'This period is no longer the next one to settle.',
          errorCode: 'Conflict.SavingsInterestPeriodMismatch',
        },
        409,
      ),
    );

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(banner?.textContent).toContain('This period is no longer the next one to settle.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
