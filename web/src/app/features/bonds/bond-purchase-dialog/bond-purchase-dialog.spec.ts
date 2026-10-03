import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as marketDataClient } from '../../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import {
  bondResponse,
  octoberOffer,
  septemberOffer,
  type BondFixture,
} from '../../../../testing/bond-fixtures';
import {
  findControl,
  jsonResponse,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  archivedPortfolio,
  plnCashCandidate,
  reservePortfolio,
  reservePortfolioId,
  savingsPortfolio,
  savingsPortfolioId,
  selectOptionLabels,
  pickSelectOption,
} from '../../deposits/testing/deposit-fixtures';
import { BondPurchaseDialog, type BondPurchaseDialogData } from './bond-purchase-dialog';

describe('BondPurchaseDialog', () => {
  let fixture: ComponentFixture<BondPurchaseDialog>;
  let component: BondPurchaseDialog;
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

  function method(input: unknown): string {
    return typeof input === 'string' ? 'GET' : (input as Request).method;
  }

  async function setup(
    data: BondPurchaseDialogData,
    writeResponse: () => Response = () => jsonResponse(bondResponse(), 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (method(input) === 'GET' && url.includes('/api/marketdata/bond-series')) {
        const onSaleOn = new URL(url).searchParams.get('onSaleOn');
        return jsonResponse(
          onSaleOn === '2026-10-01'
            ? octoberOffer
            : onSaleOn === '2026-09-15'
              ? septemberOffer
              : [],
        );
      }
      if (method(input) === 'GET' && /\/api\/portfolio\/portfolios(\?|$)/.test(url)) {
        return jsonResponse([savingsPortfolio, archivedPortfolio, reservePortfolio]);
      }
      if (method(input) === 'GET' && url.includes('/api/portfolio/transfer-candidates')) {
        const currency = new URL(url).searchParams.get('currency') ?? '';
        return jsonResponse(currency === 'PLN' ? [plnCashCandidate] : []);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [BondPurchaseDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BondPurchaseDialog);
    component = fixture.componentInstance;
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

  function valueOf(name: string): unknown {
    return findControl(form(), name).value;
  }

  function isEmpty(name: string): boolean {
    const value = valueOf(name);
    return value === null || value === undefined || value === '';
  }

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => method(input) !== 'GET') as Request[];
  }

  function offerRequests(): URL[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter(
        (input: unknown) =>
          method(input) === 'GET' && requestUrl(input).includes('/api/marketdata/bond-series'),
      )
      .map((input: unknown) => new URL(requestUrl(input)));
  }

  const manualPurchase = {
    name: 'My bond',
    seriesCode: 'EDO1036',
    type: 5,
    purchaseDate: new Date(2026, 9, 1),
    bondCount: 50,
    purchasePricePerBond: 100,
    firstPeriodRatePercent: 5.35,
    marginPercent: 2,
    earlyRedemptionFeePerBond: 3,
    taxExempt: false,
  };

  it('lists the offer for the purchase date and reloads it when the date changes', async () => {
    await setup({ portfolioId: savingsPortfolioId });

    await fill({ purchaseDate: new Date(2026, 9, 1) });
    expect(offerRequests().at(-1)!.searchParams.get('onSaleOn')).toBe('2026-10-01');
    const october = await selectOptionLabels(fixture, 'series');
    expect(october.some((label) => label.includes('EDO1036'))).toBe(true);
    expect(october.some((label) => label.includes('COI0930'))).toBe(false);

    await fill({ purchaseDate: new Date(2026, 8, 15) });
    expect(offerRequests().at(-1)!.searchParams.get('onSaleOn')).toBe('2026-09-15');
    const september = await selectOptionLabels(fixture, 'series');
    expect(september.some((label) => label.includes('COI0930'))).toBe(true);
    expect(september.some((label) => label.includes('EDO1036'))).toBe(false);
  });

  it('picking a series prefills code, type, price, rate, margin and the default fee', async () => {
    await setup({ portfolioId: savingsPortfolioId });
    await fill({ purchaseDate: new Date(2026, 9, 1) });

    await pickSelectOption(fixture, 'series', 'EDO1036');

    expect(valueOf('seriesCode')).toBe('EDO1036');
    expect(Number(valueOf('type'))).toBe(5);
    expect(Number(valueOf('purchasePricePerBond'))).toBe(100);
    expect(Number(valueOf('firstPeriodRatePercent'))).toBe(5.35);
    expect(Number(valueOf('marginPercent'))).toBe(2);
    expect(Number(valueOf('earlyRedemptionFeePerBond'))).toBe(3);
    expect(valueOf('name')).toBe('EDO1036');

    await pickSelectOption(fixture, 'series', 'ROR1027');

    expect(valueOf('seriesCode')).toBe('ROR1027');
    expect(Number(valueOf('type'))).toBe(1);
    expect(Number(valueOf('marginPercent'))).toBe(0);
    expect(Number(valueOf('earlyRedemptionFeePerBond'))).toBe(0.5);
  });

  it('opened with a series prefills that series', async () => {
    await setup({ portfolioId: savingsPortfolioId, seriesCode: 'EDO1036' });
    await fill({ purchaseDate: new Date(2026, 9, 1) });

    await vi.waitFor(() => expect(valueOf('seriesCode')).toBe('EDO1036'));
    expect(Number(valueOf('type'))).toBe(5);
    expect(Number(valueOf('firstPeriodRatePercent'))).toBe(5.35);
  });

  it('"Wprowadź ręcznie" leaves every series field empty and editable', async () => {
    await setup({ portfolioId: savingsPortfolioId });
    await switchLanguage(fixture, 'pl');
    await fill({ purchaseDate: new Date(2026, 9, 1) });
    await pickSelectOption(fixture, 'series', 'EDO1036');

    await pickSelectOption(fixture, 'series', 'Wprowadź ręcznie');

    for (const name of [
      'seriesCode',
      'type',
      'purchasePricePerBond',
      'firstPeriodRatePercent',
      'marginPercent',
      'earlyRedemptionFeePerBond',
    ]) {
      expect(isEmpty(name), name).toBe(true);
      expect(findControl(form(), name).enabled, name).toBe(true);
    }
  });

  it('offers only the non-archived portfolios when opened without one', async () => {
    await setup({});

    const labels = await selectOptionLabels(fixture, 'portfolioId');

    expect(labels).toContain('Savings');
    expect(labels).toContain('Reserve');
    expect(labels).not.toContain('Old savings');
  });

  it('create POSTs the purchase to the chosen portfolio and closes with true', async () => {
    await setup({});
    await fill({ ...manualPurchase, portfolioId: reservePortfolioId });

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/portfolios/${reservePortfolioId}/bonds`);
    const body = await request.json();
    expect(body).toMatchObject({
      name: 'My bond',
      seriesCode: 'EDO1036',
      type: 5,
      purchaseDate: '2026-10-01',
      bondCount: 50,
      purchasePricePerBond: 100,
      firstPeriodRatePercent: 5.35,
      marginPercent: 2,
      earlyRedemptionFeePerBond: 3,
      taxExempt: false,
    });
    expect(body.fundingAssetId ?? null).toBeNull();
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('create preset to a portfolio POSTs to that portfolio', async () => {
    await setup({ portfolioId: savingsPortfolioId });
    await fill(manualPurchase);

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.url).toContain(`/api/portfolio/portfolios/${savingsPortfolioId}/bonds`);
  });

  describe('funding', () => {
    it('lists the PLN Cash candidates, defaulting to new money', async () => {
      await setup({ portfolioId: savingsPortfolioId });

      expect(valueOf('fundingAssetId') ?? null).toBeNull();
      const candidateRequest = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0])
        .filter((input: unknown) =>
          requestUrl(input).includes('/api/portfolio/transfer-candidates'),
        )
        .map((input: unknown) => new URL(requestUrl(input)))
        .at(-1);
      expect(candidateRequest?.searchParams.get('currency')).toBe('PLN');
      expect(['Cash', '0']).toContain(candidateRequest?.searchParams.get('assetClass'));
      const labels = await selectOptionLabels(fixture, 'fundingAssetId');
      expect(labels[0]).toContain('New money');
      expect(labels.some((label) => label.includes(plnCashCandidate.name))).toBe(true);
    });

    it('an amount above the selected Cash balance blocks submit', async () => {
      await setup({ portfolioId: savingsPortfolioId });
      await fill({
        ...manualPurchase,
        bondCount: 51,
        fundingAssetId: plnCashCandidate.assetId,
      });

      await component['onSubmit']();

      expect(form().invalid).toBe(true);
      expect(writeRequests()).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();

      await fill({ bondCount: 50 });
      await component['onSubmit']();
      expect(writeRequests()).toHaveLength(1);
    });

    it('create POSTs the chosen Cash as fundingAssetId', async () => {
      await setup({ portfolioId: savingsPortfolioId });
      await fill({ ...manualPurchase, fundingAssetId: plnCashCandidate.assetId });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect((await request.json()).fundingAssetId).toBe(plnCashCandidate.assetId);
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });

  describe('edit', () => {
    const bond: BondFixture = bondResponse({ fundingAssetId: plnCashCandidate.assetId });

    it('prefills the form from the bond', async () => {
      await setup({ bond });

      expect(valueOf('name')).toBe('Edo purchase');
      expect(valueOf('seriesCode')).toBe('EDO1036');
      expect(Number(valueOf('bondCount'))).toBe(50);
      expect(Number(valueOf('purchasePricePerBond'))).toBe(100);
      expect(Number(valueOf('earlyRedemptionFeePerBond'))).toBe(3);
    });

    it('PUTs the changed terms to the bond without a funding source and closes with true', async () => {
      await setup({ bond }, () => jsonResponse(bondResponse({ bondCount: 60 })));
      await fill({ bondCount: 60 });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('PUT');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${bond.portfolioId}/bonds/${bond.assetId}`,
      );
      const body = await request.json();
      expect(body.bondCount).toBe(60);
      expect(body).not.toHaveProperty('fundingAssetId');
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });
});
