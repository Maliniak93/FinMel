import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import {
  BOND_PERIOD_STATE,
  cashCandidates,
  dueEdoBond,
  dueRorBond,
  edoSeries,
  fundingCashId,
  rorPreviewResponse,
  rorSeries,
  type BondFixture,
} from '../../../../testing/bond-fixtures';
import { restoreEnglish } from '../../../../testing/i18n';
import { client as marketDataClient } from '../../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatMoney } from '../../../shared/format';
import {
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { requestMethod } from '../../deposits/testing/deposit-fixtures';
import { SettleBondInterestDialog } from './settle-bond-interest-dialog';

describe('SettleBondInterestDialog', () => {
  let fixture: ComponentFixture<SettleBondInterestDialog>;
  let component: SettleBondInterestDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(bond: BondFixture = dueRorBond, series: unknown = rorSeries): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (requestMethod(input) === 'GET') {
        if (url.includes('/api/marketdata/bond-series/')) {
          return jsonResponse(series);
        }
        if (url.includes('/api/portfolio/transfer-candidates')) {
          return jsonResponse(cashCandidates);
        }
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return url.endsWith('/interest-settlements/preview')
        ? jsonResponse(rorPreviewResponse)
        : jsonResponse({ settlementIds: ['x'] }, 201);
    });

    await TestBed.configureTestingModule({
      imports: [SettleBondInterestDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { bond } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettleBondInterestDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    const lastDueIndex = Math.max(
      ...bond.periods
        .filter((period) => period.state === BOND_PERIOD_STATE.Due)
        .map((period) => period.index),
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(rateInput(lastDueIndex)).not.toBeNull();
    });
  }

  function rateInput(periodIndex: number): HTMLInputElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      `[data-testid="period-rate-${periodIndex}"]`,
    );
  }

  async function typeRate(periodIndex: number, value: string): Promise<void> {
    const input = rateInput(periodIndex)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function settleRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter(
        (input: unknown) =>
          requestMethod(input) === 'POST' && requestUrl(input).endsWith('/interest-settlements'),
      ) as Request[];
  }

  function previewRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter(
        (input: unknown) =>
          requestMethod(input) === 'POST' &&
          requestUrl(input).endsWith('/interest-settlements/preview'),
      ) as Request[];
  }

  it('prefills the rates from the bond series by period index and leaves an unpublished one empty and required', async () => {
    await setup();

    expect(fetchSpy.mock.calls.map((call: unknown[]) => requestUrl(call[0]))).toEqual(
      expect.arrayContaining([expect.stringContaining('/api/marketdata/bond-series/ROR0627')]),
    );
    expect(Number(rateInput(2)!.value)).toBe(3.75);
    expect(rateInput(3)!.value).toBe('');
    expect(rateInput(3)!.required).toBe(true);
    const first = rateInput(1);
    expect(first === null || first.readOnly || first.disabled).toBe(true);
    expect(renderedText(fixture)).toContain('4');
  });

  it('a missing rate blocks submit', async () => {
    await setup();

    await component['onSubmit']();

    expect(settleRequests()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('defaults the Cash picker to the bond funding asset and settles the due periods with the typed rate', async () => {
    await setup();
    await typeRate(3, '3.75');

    await component['onSubmit']();

    const [request] = settleRequests();
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${dueRorBond.portfolioId}/bonds/${dueRorBond.assetId}/interest-settlements`,
    );
    expect(await request.json()).toEqual({
      periods: [
        { periodIndex: 1 },
        { periodIndex: 2, ratePercent: 3.75 },
        { periodIndex: 3, ratePercent: 3.75 },
      ],
      destinationAssetId: fundingCashId,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('previews through PreviewBondInterest and shows the totals', async () => {
    await setup();
    await typeRate(3, '3.75');

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(previewRequests().length).toBeGreaterThan(0);
      const text = renderedText(fixture);
      expect(text).toContain(formatMoney(rorPreviewResponse.totals.net, 'PLN'));
      expect(text).toContain(formatMoney(rorPreviewResponse.totals.tax, 'PLN'));
    });
    expect(settleRequests()).toEqual([]);
  });

  it('a capitalising bond sends no destination', async () => {
    await setup(dueEdoBond, edoSeries);

    await component['onSubmit']();

    const [request] = settleRequests();
    expect(await request.json()).toEqual({ periods: [{ periodIndex: 1 }] });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
