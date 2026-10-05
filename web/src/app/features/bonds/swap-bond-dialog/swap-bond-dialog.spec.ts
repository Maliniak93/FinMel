import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import {
  cashCandidates,
  fundingCashId,
  settledTosBond,
  swapOffer,
  tosRedemptionPreview,
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
import { requestMethod, writeRequests } from '../../deposits/testing/deposit-fixtures';
import { SwapBondDialog } from './swap-bond-dialog';

describe('SwapBondDialog', () => {
  let fixture: ComponentFixture<SwapBondDialog>;
  let component: SwapBondDialog;
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
    bond: BondFixture = settledTosBond,
    preview: typeof tosRedemptionPreview = tosRedemptionPreview,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (requestMethod(input) === 'GET') {
        if (url.endsWith('/redemption-preview')) {
          return jsonResponse(preview);
        }
        if (url.includes('/api/marketdata/bond-series')) {
          return jsonResponse(swapOffer);
        }
        if (url.includes('/api/portfolio/transfer-candidates')) {
          return jsonResponse(cashCandidates);
        }
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return jsonResponse(bond, 201);
    });

    await TestBed.configureTestingModule({
      imports: [SwapBondDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { bond } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SwapBondDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    await vi.waitFor(() => {
      expect(component['offerResource'].hasValue()).toBe(true);
      expect(component['previewResource'].hasValue()).toBe(true);
      expect(component['candidatesResource'].hasValue()).toBe(true);
    });
  }

  async function pickSeries(code: string): Promise<void> {
    component['form'].controls.series.setValue(code);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function setCount(count: number): Promise<void> {
    component['form'].controls.bondCount.setValue(count);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function seriesRequests(): URL[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => requestUrl(input).includes('/api/marketdata/bond-series'))
      .map((input: unknown) => new URL(requestUrl(input)));
  }

  function textAt(testId: string): string {
    return (
      (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`)
        ?.textContent ?? ''
    ).replace(/\s+/g, ' ');
  }

  function money(amount: number): string {
    return formatMoney(amount, 'PLN').replace(/\s+/g, ' ');
  }

  it('lists the series on sale on the maturity date, leaving out those without a swap price', async () => {
    await setup();

    expect(seriesRequests()[0].searchParams.get('onSaleOn')).toBe(settledTosBond.maturityDate);
    expect(component['offerResource'].value()?.map((series) => series.code)).toEqual(['EDO0939']);
  });

  it('prefills the swap price, first-period rate, margin, default fee and name from the picked series', async () => {
    await setup();

    await pickSeries('EDO0939');

    expect(component['form'].getRawValue()).toEqual(
      expect.objectContaining({
        name: 'EDO0939',
        seriesCode: 'EDO0939',
        type: 5,
        swapPricePerBond: 99.9,
        firstPeriodRatePercent: 5.35,
        marginPercent: 2,
        earlyRedemptionFeePerBond: 3,
      }),
    );
  });

  it('defaults the count to the whole holding and recomputes the cost and leftover when it changes', async () => {
    await setup();
    await pickSeries('EDO0939');

    expect(component['form'].controls.bondCount.value).toBe(10);
    expect(textAt('swap-proceeds')).toContain(money(1111.69));
    expect(textAt('swap-cost')).toContain(money(999));
    expect(textAt('swap-leftover')).toContain(money(112.69));

    await setCount(6);

    expect(textAt('swap-cost')).toContain(money(599.4));
    expect(textAt('swap-leftover')).toContain(money(512.29));
  });

  it('requires a Cash account while the leftover is above 0', async () => {
    await setup({ ...settledTosBond, fundingAssetId: null, fundingAssetName: null });
    await pickSeries('EDO0939');

    await component['onSubmit']();
    fixture.detectChanges();

    expect(writeRequests(fetchSpy)).toEqual([]);
    expect(renderedText(fixture)).toContain('Pick the cash account that receives the leftover.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('blocks a swap that costs more than the redemption pays out', async () => {
    await setup(settledTosBond, { ...tosRedemptionPreview, proceeds: 999.81 });
    await pickSeries('EDO0939');
    component['form'].controls.swapPricePerBond.setValue(100);
    fixture.detectChanges();
    await fixture.whenStable();

    await component['onSubmit']();
    fixture.detectChanges();

    expect(writeRequests(fetchSpy)).toEqual([]);
    expect(renderedText(fixture)).toContain('The swap costs more than the redemption pays out.');
  });

  it('swaps the picked count into the new series with the leftover to the funding Cash, then closes with true', async () => {
    await setup();
    await pickSeries('EDO0939');
    await setCount(6);

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${settledTosBond.portfolioId}/bonds/${settledTosBond.assetId}/swap`,
    );
    expect(await request.json()).toEqual({
      bondCount: 6,
      newBond: {
        name: 'EDO0939',
        seriesCode: 'EDO0939',
        type: 5,
        swapPricePerBond: 99.9,
        firstPeriodRatePercent: 5.35,
        marginPercent: 2,
        earlyRedemptionFeePerBond: 3,
      },
      destinationAssetId: fundingCashId,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });
});
