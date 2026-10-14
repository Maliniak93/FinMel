import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { restoreEnglish } from '../../../../testing/i18n';
import { bankAccountResponse, interestPreview } from '../../../../testing/bank-account-fixtures';
import { formatDate, formatMoney } from '../../../shared/format';
import {
  findControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { SettleInterestDialog } from './settle-interest-dialog';

const account = bankAccountResponse({
  id: '22222222-2222-2222-2222-222222222222',
  bank: { interestDue: true, duePeriodCount: 1 },
});

describe('SettleInterestDialog', () => {
  let fixture: ComponentFixture<SettleInterestDialog>;
  let component: SettleInterestDialog;
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
          ? jsonResponse(interestPreview)
          : jsonResponse({ detail: 'Not found.' }, 404);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [SettleInterestDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { account } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettleInterestDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    await vi.waitFor(() =>
      expect(Number(findControl(form(), 'grossInterest').value)).toBe(
        interestPreview.grossInterest,
      ),
    );
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function form(): FormGroup {
    return component['form'] as FormGroup;
  }

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => method(input) !== 'GET') as Request[];
  }

  it('previews the account interest, pre-fills gross and tax and shows the net', async () => {
    await setup();

    const previewUrl = fetchSpy.mock.calls
      .map((call: unknown[]) => requestUrl(call[0]))
      .find((url: string) => url.includes('/interest-preview'));
    expect(previewUrl).toContain(`/api/portfolio/accounts/${account.id}/interest-preview`);
    expect(Number(findControl(form(), 'grossInterest').value)).toBe(42.47);
    expect(Number(findControl(form(), 'tax').value)).toBe(8.07);
    const text = renderedText(fixture);
    expect(text).toContain(formatDate('2026-08-01'));
    expect(text).toContain(formatDate('2026-08-31'));
    expect(text).toContain(formatMoney(34.4, 'PLN'));
  });

  it('submit POSTs the previewed values to the account interest-settlements and closes with true', async () => {
    await setup();

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/accounts/${account.id}/interest-settlements`);
    expect(await request.json()).toEqual({
      periodEnd: '2026-08-31',
      grossInterest: 42.47,
      tax: 8.07,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
