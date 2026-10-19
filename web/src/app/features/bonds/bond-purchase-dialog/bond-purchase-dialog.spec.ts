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
import { formatMoney } from '../../../shared/format';
import {
  findControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  pickSelectOption,
  selectOptionLabels,
  selectTriggerState,
} from '../../deposits/testing/deposit-fixtures';
import { assetGroups, blueGroupId, isGroupListRequest } from '../../groups/testing/group-fixtures';
import { BondPurchaseDialog, type BondPurchaseDialogData } from './bond-purchase-dialog';

const bondsAccountId = 'b1111111-1111-1111-1111-111111111111';
const reserveBondsAccountId = 'b2222222-2222-2222-2222-222222222222';

const accountList = [
  {
    id: bondsAccountId,
    name: 'Obligacje IKE',
    type: 1,
    wrapper: 1,
    isArchived: false,
    cashBalance: 5000,
    groupId: blueGroupId,
  },
  {
    id: reserveBondsAccountId,
    name: 'Obligacje zwykle',
    type: 1,
    wrapper: 0,
    isArchived: false,
    cashBalance: 1000,
  },
  {
    id: 'b3333333-3333-3333-3333-333333333333',
    name: 'Old bonds',
    type: 1,
    wrapper: 0,
    isArchived: true,
    cashBalance: 0,
  },
  {
    id: 'b4444444-4444-4444-4444-444444444444',
    name: 'XTB IKE',
    type: 0,
    wrapper: 1,
    isArchived: false,
    cashBalance: 0,
  },
];

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
      if (isGroupListRequest(input)) {
        return jsonResponse(assetGroups);
      }
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
      if (method(input) === 'GET' && /\/api\/portfolio\/accounts(\?|$)/.test(url)) {
        return jsonResponse(accountList);
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
  };

  it('lists the offer for the purchase date and reloads it when the date changes', async () => {
    await setup({ accountId: bondsAccountId });

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
    await setup({ accountId: bondsAccountId });
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
    await setup({ accountId: bondsAccountId, seriesCode: 'EDO1036' });
    await fill({ purchaseDate: new Date(2026, 9, 1) });

    await vi.waitFor(() => expect(valueOf('seriesCode')).toBe('EDO1036'));
    expect(Number(valueOf('type'))).toBe(5);
    expect(Number(valueOf('firstPeriodRatePercent'))).toBe(5.35);
  });

  it('"Wprowadź ręcznie" leaves every series field empty and editable', async () => {
    await setup({ accountId: bondsAccountId });
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

  it('offers only the unarchived Bonds accounts when opened without one', async () => {
    await setup({});

    const labels = await selectOptionLabels(fixture, 'accountId');

    expect(labels).toContain('Obligacje IKE');
    expect(labels).toContain('Obligacje zwykle');
    expect(labels).not.toContain('Old bonds');
    expect(labels).not.toContain('XTB IKE');
  });

  it('create POSTs the purchase to the chosen account and closes with true', async () => {
    await setup({});
    await fill({ ...manualPurchase, accountId: reserveBondsAccountId });

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/bonds`);
    const body = await request.json();
    expect(body).toMatchObject({
      accountId: reserveBondsAccountId,
      name: 'My bond',
      seriesCode: 'EDO1036',
      type: 5,
      purchaseDate: '2026-10-01',
      bondCount: 50,
      purchasePricePerBond: 100,
      firstPeriodRatePercent: 5.35,
      marginPercent: 2,
      earlyRedemptionFeePerBond: 3,
    });
    expect(body).not.toHaveProperty('taxExempt');
    expect(body).not.toHaveProperty('fundingAssetId');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('create preset to an account POSTs to that account', async () => {
    await setup({ accountId: bondsAccountId });
    await fill(manualPurchase);

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.url).toContain(`/api/portfolio/bonds`);
    expect((await request.json()).accountId).toBe(bondsAccountId);
  });

  describe('account cash', () => {
    it('shows the cost taken from the account cash and its balance, with no cash select', async () => {
      await setup({ accountId: bondsAccountId });
      await fill(manualPurchase);

      const money = formatMoney(5000, 'PLN').replace(/\s+/g, ' ');
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(renderedText(fixture).replace(/\s+/g, ' ')).toContain(
          `From the cash of Obligacje IKE: ${money} · balance ${money}`,
        );
      });
      expect(Object.keys(form().controls)).not.toContain('fundingAssetId');
      expect(
        fetchSpy.mock.calls.some((call: unknown[]) =>
          requestUrl(call[0]).includes('/api/portfolio/transfer-candidates'),
        ),
      ).toBe(false);
    });

    it('follows the account picked when none is preset', async () => {
      await setup({});
      await fill({ ...manualPurchase, bondCount: 5, accountId: reserveBondsAccountId });

      await vi.waitFor(() => {
        fixture.detectChanges();
        const text = renderedText(fixture).replace(/\s+/g, ' ');
        expect(text).toContain('From the cash of Obligacje zwykle');
        expect(text).toContain(formatMoney(500, 'PLN').replace(/\s+/g, ' '));
      });
    });

    it('answers an insufficient-funds rejection with the account message and keeps the dialog open', async () => {
      await setup({ accountId: bondsAccountId }, () =>
        jsonResponse(
          { detail: 'Not enough cash.', errorCode: 'Validation.InsufficientFunds' },
          400,
        ),
      );
      await fill({ ...manualPurchase, bondCount: 60 });

      await component['onSubmit']();
      fixture.detectChanges();

      expect(renderedText(fixture)).toContain('Insufficient funds on the account');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('shows the insufficient-funds message in Polish', async () => {
      await setup({ accountId: bondsAccountId }, () =>
        jsonResponse(
          { detail: 'Not enough cash.', errorCode: 'Validation.InsufficientFunds' },
          400,
        ),
      );
      await switchLanguage(fixture, 'pl');
      await fill({ ...manualPurchase, bondCount: 60 });

      await component['onSubmit']();
      fixture.detectChanges();

      expect(renderedText(fixture)).toContain('Brak środków na rachunku');
    });
  });

  describe('group', () => {
    it('prefills the account default', async () => {
      await setup({});
      await fill({ ...manualPurchase, accountId: bondsAccountId });

      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(selectTriggerState(fixture, 'groupId').triggerText).toContain('Emerytura');
      });
      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.url).toContain(`/api/portfolio/bonds`);
      expect((await request.json()).groupId).toBe(blueGroupId);
    });
  });

  describe('edit', () => {
    const bond: BondFixture = bondResponse();

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
      expect(request.url).toContain(`/api/portfolio/bonds/${bond.assetId}`);
      const body = await request.json();
      expect(body.bondCount).toBe(60);
      expect(body).not.toHaveProperty('fundingAssetId');
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });
});
