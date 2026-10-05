import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import {
  BOND_PERIOD_STATE,
  bondResponse,
  cashCandidates,
  dueRorBond,
  edoEarlyRedemptionPreview,
  edoEarlySeries,
  edoYearOneSettledBond,
  fundingCashId,
  otherCashId,
  rorEarlySeries,
  rorMonthsSettledBond,
  type BondFixture,
} from '../../../../testing/bond-fixtures';
import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { client as marketDataClient } from '../../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatMoney } from '../../../shared/format';
import {
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { requestMethod, writeRequests } from '../../deposits/testing/deposit-fixtures';
import { EarlyRedeemBondDialog } from './early-redeem-bond-dialog';

const activeTosBond = bondResponse({
  assetId: 'c3c3c3c3-0000-0000-0000-00000000c010',
  name: 'Tos active',
  seriesCode: 'TOS1028',
  type: 3,
  purchaseDate: '2025-10-01',
  bondCount: 10,
  firstPeriodRatePercent: 4.4,
  marginPercent: null,
  earlyRedemptionFeePerBond: 1,
  maturityDate: '2028-10-01',
  fundingAssetId: fundingCashId,
  fundingAssetName: 'Wallet cash',
  periods: [
    {
      index: 1,
      start: '2025-10-01',
      end: '2026-10-01',
      state: BOND_PERIOD_STATE.Upcoming,
      settlement: null,
    },
  ],
});

describe('EarlyRedeemBondDialog', () => {
  let fixture: ComponentFixture<EarlyRedeemBondDialog>;
  let component: EarlyRedeemBondDialog;
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

  async function setup(
    bond: BondFixture = edoYearOneSettledBond,
    series: unknown = edoEarlySeries,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (requestMethod(input) === 'GET') {
        if (url.includes('/early-redemption-preview')) {
          return jsonResponse(edoEarlyRedemptionPreview);
        }
        if (url.includes('/api/marketdata/bond-series/')) {
          return jsonResponse(series);
        }
        if (url.includes('/api/portfolio/transfer-candidates')) {
          return jsonResponse(cashCandidates);
        }
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return jsonResponse({ ...bond, bondCount: 6 });
    });

    await TestBed.configureTestingModule({
      imports: [EarlyRedeemBondDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { bond } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EarlyRedeemBondDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  function form() {
    return component['form'].controls;
  }

  async function pick(date: Date, count?: number): Promise<void> {
    form().date.setValue(date);
    if (count !== undefined) {
      form().bondCount.setValue(count);
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function previewQueries(): URLSearchParams[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => requestUrl(call[0]))
      .filter((url: string) => url.includes('/early-redemption-preview'))
      .map((url: string) => new URL(url).searchParams);
  }

  function lastPreviewQuery(): Record<string, string> {
    return Object.fromEntries(previewQueries().at(-1) ?? []);
  }

  it('defaults to today, the whole holding and the Cash that funded the purchase', async () => {
    await setup();
    await vi.waitFor(() => expect(form().destinationAssetId.value).toBe(fundingCashId));

    const now = new Date();
    expect(form().date.value).toEqual(new Date(now.getFullYear(), now.getMonth(), now.getDate()));
    expect(form().bondCount.value).toBe(10);
  });

  it("prefills the running period's rate from the catalog and refreshes the preview when the date and count change", async () => {
    await setup();

    await pick(new Date(2026, 4, 13));
    await vi.waitFor(() => {
      expect(form().ratePercent.value).toBe(4);
      expect(form().ratePercent.enabled).toBe(true);
      expect(lastPreviewQuery()).toEqual({
        date: '2026-05-13',
        bondCount: '10',
        runningPeriodRatePercent: '4',
      });
    });

    await pick(new Date(2026, 4, 13), 4);
    await vi.waitFor(() =>
      expect(lastPreviewQuery()).toEqual({
        date: '2026-05-13',
        bondCount: '4',
        runningPeriodRatePercent: '4',
      }),
    );

    await pick(new Date(2026, 5, 1), 4);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(lastPreviewQuery()).toEqual({
        date: '2026-06-01',
        bondCount: '4',
        runningPeriodRatePercent: '4',
      });
      const text = renderedText(fixture);
      expect(text).toContain(formatMoney(edoEarlyRedemptionPreview.interestDue, 'PLN'));
      expect(text).toContain(formatMoney(edoEarlyRedemptionPreview.fee, 'PLN'));
      expect(text).toContain(formatMoney(edoEarlyRedemptionPreview.tax, 'PLN'));
      expect(text).toContain(formatMoney(edoEarlyRedemptionPreview.proceeds, 'PLN'));
    });
  });

  it('keeps a fixed rate read-only from the terms and sends no running-period rate', async () => {
    await setup(activeTosBond);

    await pick(new Date(2026, 4, 1), 3);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(lastPreviewQuery()).toEqual({ date: '2026-05-01', bondCount: '3' });
    });

    expect(form().ratePercent.value).toBe(4.4);
    expect(form().ratePercent.disabled).toBe(true);
    const rateInput = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '[data-testid="early-redeem-rate"]',
    );
    expect(rateInput?.disabled || rateInput?.readOnly).toBe(true);
  });

  it('notes that a capitalising bond pays the fee from the interest, capped at it', async () => {
    await setup();
    await switchLanguage(fixture, 'pl');

    await pick(new Date(2026, 4, 13));

    expect(renderedText(fixture)).toContain('Opłata potrącana z odsetek, nie wyższa niż odsetki');
  });

  it('notes the full fee for a coupon bond past its first period', async () => {
    await setup(rorMonthsSettledBond, rorEarlySeries);
    await switchLanguage(fixture, 'pl');

    await pick(new Date(2026, 8, 25), 20);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(form().ratePercent.value).toBe(3.75);
      expect(renderedText(fixture)).toContain(
        'Pełna opłata — wcześniejsze kupony zostały już wypłacone',
      );
    });
    expect(lastPreviewQuery()).toEqual({
      date: '2026-09-25',
      bondCount: '20',
      runningPeriodRatePercent: '3.75',
    });
  });

  it('with an unsettled earlier period points to settling first and loads no preview', async () => {
    await setup(dueRorBond, rorEarlySeries);
    await switchLanguage(fixture, 'pl');

    await pick(new Date(2026, 8, 25));

    const text = renderedText(fixture);
    expect(text).toContain('Najpierw rozlicz wcześniejsze okresy');
    expect(text).toContain('nierozliczone okresy: 3');
    expect(previewQueries().filter((query) => query.get('date') === '2026-09-25')).toEqual([]);

    const settle = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="settle-first-action"]',
    );
    settle!.click();

    expect(dialogRef.close).toHaveBeenCalledWith('settle');
    expect(writeRequests(fetchSpy)).toEqual([]);
  });

  it('redeems the picked count on the picked date to the picked Cash and closes with true', async () => {
    await setup();
    await pick(new Date(2026, 4, 13), 4);
    await vi.waitFor(() => expect(component['previewResource'].hasValue()).toBe(true));
    form().destinationAssetId.setValue(otherCashId);

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${edoYearOneSettledBond.portfolioId}/bonds/${edoYearOneSettledBond.assetId}/early-redemption`,
    );
    expect(await request.json()).toEqual({
      date: '2026-05-13',
      bondCount: 4,
      runningPeriodRatePercent: 4,
      destinationAssetId: otherCashId,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('rejects a count above the holding without asking for a preview', async () => {
    await setup();
    await pick(new Date(2026, 4, 13), 11);
    form().bondCount.markAsTouched();
    fixture.detectChanges();

    expect(renderedText(fixture)).toContain('Enter a whole number from 1 to 10.');
    expect(previewQueries().filter((query) => query.get('bondCount') === '11')).toEqual([]);
  });
});
