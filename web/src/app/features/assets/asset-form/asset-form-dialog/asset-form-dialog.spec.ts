import type { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';

import {
  attributesOf,
  labelsOf,
  polishProblems,
  restoreEnglish,
  switchLanguage,
} from '../../../../../testing/i18n';
import { client as marketDataClient } from '../../../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../../../api/portfolio/client.gen';
import { BondPurchaseDialog } from '../../../bonds/bond-purchase-dialog/bond-purchase-dialog';
import { DepositFormDialog } from '../../../deposits/deposit-form-dialog/deposit-form-dialog';
import { SavingsAccountFormDialog } from '../../../deposits/savings-account-form-dialog/savings-account-form-dialog';
import { ASSET_CLASS, ASSET_CLASSES } from '../../asset-class';
import { AssetTypePicker } from '../asset-type-picker/asset-type-picker';
import { CashAssetForm } from '../forms/cash-asset-form/cash-asset-form';
import { GoldAssetForm } from '../forms/gold-asset-form/gold-asset-form';
import { ManualAssetForm } from '../forms/manual-asset-form/manual-asset-form';
import { SecurityAssetForm } from '../forms/security-asset-form/security-asset-form';
import {
  cashAsset,
  cryptoAsset,
  cryptoInstrumentDetails,
  findControl,
  hasControl,
  instrumentId,
  jsonResponse,
  portfolioId,
  renderedText,
  requestUrl,
  showValidationErrors,
  toggleFirstTransaction,
} from '../testing/asset-form-fixtures';
import { INSTRUMENT_REQUIRED_MESSAGE } from '../blocks/instrument-picker/instrument-picker';
import { AssetFormDialog, type AssetFormDialogData } from './asset-form-dialog';
import { provideI18nTesting } from '../../../../core/i18n/testing';

type AssetFormComponent = CashAssetForm | SecurityAssetForm | GoldAssetForm | ManualAssetForm;

const FORM_COMPONENTS: readonly Type<AssetFormComponent>[] = [
  CashAssetForm,
  SecurityAssetForm,
  GoldAssetForm,
  ManualAssetForm,
];

const EXPECTED_FORM: Record<number, Type<AssetFormComponent>> = {
  [ASSET_CLASS.Cash]: CashAssetForm,
  [ASSET_CLASS.Stock]: SecurityAssetForm,
  [ASSET_CLASS.Etf]: SecurityAssetForm,
  [ASSET_CLASS.Crypto]: SecurityAssetForm,
  [ASSET_CLASS.PreciousMetal]: GoldAssetForm,
  [ASSET_CLASS.RealEstate]: ManualAssetForm,
  [ASSET_CLASS.Other]: ManualAssetForm,
};

describe('AssetFormDialog', () => {
  let fixture: ComponentFixture<AssetFormDialog>;
  let component: AssetFormDialog;
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
    data: AssetFormDialogData,
    fetchImplementation?: (input: unknown) => Promise<Response>,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    if (fetchImplementation) {
      fetchSpy.mockImplementation(fetchImplementation);
    }

    await TestBed.configureTestingModule({
      imports: [AssetFormDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssetFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  function picker(): HTMLElement | null {
    return fixture.debugElement.query(By.directive(AssetTypePicker))?.nativeElement ?? null;
  }

  function renderedForms(): Type<AssetFormComponent>[] {
    return FORM_COMPONENTS.filter((type) => fixture.debugElement.query(By.directive(type)));
  }

  function activeForm(): AssetFormComponent {
    const [type] = renderedForms();
    if (!type) {
      throw new Error('No asset form rendered.');
    }
    return fixture.debugElement.query(By.directive(type)).componentInstance as AssetFormComponent;
  }

  async function pickTile(assetClass: number): Promise<void> {
    const tiles = picker()?.querySelectorAll<HTMLButtonElement>('button') ?? [];
    tiles[ASSET_CLASSES.findIndex((c) => c.value === assetClass)].click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function goBack(): Promise<void> {
    const back = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((button) => /back/i.test(button.getAttribute('aria-label') ?? ''));
    if (!back) {
      throw new Error('No back button (aria-label containing "back") rendered.');
    }
    back.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  describe('create', () => {
    it('create shows picker then the form for the chosen class', async () => {
      await setup({ portfolioId });

      expect(picker()).not.toBeNull();
      expect(picker()!.querySelectorAll('button')).toHaveLength(10);
      expect(renderedForms()).toEqual([]);

      for (const { value } of ASSET_CLASSES.filter(
        (c) =>
          c.value !== ASSET_CLASS.Deposit &&
          c.value !== ASSET_CLASS.Savings &&
          c.value !== ASSET_CLASS.Bond,
      )) {
        await pickTile(value);

        expect(picker()).toBeNull();
        expect(renderedForms()).toEqual([EXPECTED_FORM[value]]);
        expect(activeForm().assetClass()).toBe(value);

        await goBack();
      }

      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('the Deposit tile opens DepositFormDialog preset to the current portfolio', async () => {
      await setup({ portfolioId });
      const matDialog = fixture.debugElement.injector.get(MatDialog);
      const open = vi
        .spyOn(matDialog, 'open')
        .mockReturnValue({ afterClosed: () => of(true) } as unknown as MatDialogRef<unknown>);

      await pickTile(ASSET_CLASS.Deposit);

      expect(renderedForms()).toEqual([]);
      expect(fixture.debugElement.query(By.directive(CashAssetForm))).toBeNull();
      expect(open).toHaveBeenCalledWith(
        DepositFormDialog,
        expect.objectContaining({ data: expect.objectContaining({ portfolioId }) }),
      );
      await vi.waitFor(() => expect(dialogRef.close).toHaveBeenCalledWith(true));
      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('the Bond tile opens BondPurchaseDialog preset to the current portfolio', async () => {
      await setup({ portfolioId });
      const matDialog = fixture.debugElement.injector.get(MatDialog);
      const open = vi
        .spyOn(matDialog, 'open')
        .mockReturnValue({ afterClosed: () => of(true) } as unknown as MatDialogRef<unknown>);

      await pickTile(ASSET_CLASS.Bond);

      expect(renderedForms()).toEqual([]);
      expect(fixture.debugElement.query(By.directive(SecurityAssetForm))).toBeNull();
      expect(open).toHaveBeenCalledWith(
        BondPurchaseDialog,
        expect.objectContaining({ data: expect.objectContaining({ portfolioId }) }),
      );
      await vi.waitFor(() => expect(dialogRef.close).toHaveBeenCalledWith(true));
      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('the Savings account tile opens SavingsAccountFormDialog preset to the current portfolio', async () => {
      await setup({ portfolioId });
      const matDialog = fixture.debugElement.injector.get(MatDialog);
      const open = vi
        .spyOn(matDialog, 'open')
        .mockReturnValue({ afterClosed: () => of(true) } as unknown as MatDialogRef<unknown>);

      await pickTile(ASSET_CLASS.Savings);

      expect(renderedForms()).toEqual([]);
      expect(fixture.debugElement.query(By.directive(CashAssetForm))).toBeNull();
      expect(open).toHaveBeenCalledWith(
        SavingsAccountFormDialog,
        expect.objectContaining({ data: expect.objectContaining({ portfolioId }) }),
      );
      await vi.waitFor(() => expect(dialogRef.close).toHaveBeenCalledWith(true));
      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('back returns to the picker', async () => {
      await setup({ portfolioId });

      await pickTile(ASSET_CLASS.Cash);
      findControl(activeForm().form, 'name').setValue('Checking account');

      await goBack();

      expect(picker()).not.toBeNull();
      expect(renderedForms()).toEqual([]);
      expect(fetchSpy).not.toHaveBeenCalled();
      expect(dialogRef.close).not.toHaveBeenCalled();

      await pickTile(ASSET_CLASS.Cash);
      expect(findControl(activeForm().form, 'name').value).toBe('');
    });

    it('POSTs the body the chosen form builds and closes with true on success', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(jsonResponse(cashAsset, 201));

      await pickTile(ASSET_CLASS.Cash);
      findControl(activeForm().form, 'name').setValue('Checking account');

      await component['onSubmit']();

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const request = fetchSpy.mock.calls[0][0] as Request;
      expect(request.method).toBe('POST');
      expect(request.url).toContain(`/portfolios/${portfolioId}/assets`);
      expect(await request.json()).toEqual({
        assetClass: 0,
        name: 'Checking account',
        currency: 'PLN',
        initialTransaction: null,
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('sends nothing while a security form has no instrument selected', async () => {
      await setup({ portfolioId });

      await pickTile(ASSET_CLASS.Etf);
      findControl(activeForm().form, 'name').setValue('World ETF');

      await component['onSubmit']();

      expect(fetchSpy).not.toHaveBeenCalled();
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('an invalid initial transaction blocks submission', async () => {
      await setup({ portfolioId });

      await pickTile(ASSET_CLASS.Cash);
      findControl(activeForm().form, 'name').setValue('Checking account');
      await toggleFirstTransaction(fixture);
      findControl(activeForm().form, 'quantity').setValue(-5);

      await component['onSubmit']();

      expect(fetchSpy).not.toHaveBeenCalled();
    });
  });

  describe('edit', () => {
    it('edit opens the form of the stored class', async () => {
      await setup({ portfolioId, asset: cryptoAsset }, async (input) =>
        requestUrl(input).includes('/instruments/')
          ? jsonResponse(cryptoInstrumentDetails)
          : jsonResponse(cryptoAsset),
      );

      expect(picker()).toBeNull();
      expect(renderedForms()).toEqual([SecurityAssetForm]);
      expect(activeForm().assetClass()).toBe(ASSET_CLASS.Crypto);
      expect(hasControl(activeForm().form, 'assetClass')).toBe(false);
      expect(renderedText(fixture)).not.toContain('Asset class');
      expect(findControl(activeForm().form, 'name').value).toBe('Bitcoin');
      expect(
        fetchSpy.mock.calls.some((call: unknown[]) =>
          requestUrl(call[0]).includes(`/instruments/${instrumentId}`),
        ),
      ).toBe(true);
      expect(renderedText(fixture)).toContain('bitcoin');

      const callsBefore = fetchSpy.mock.calls.length;
      await component['onSubmit']();

      const request = fetchSpy.mock.calls[callsBefore][0] as Request;
      expect(request.method).toBe('PUT');
      expect(request.url).toContain(`/portfolios/${portfolioId}/assets/${cryptoAsset.id}`);
      const body = await request.json();
      expect(body).not.toHaveProperty('initialTransaction');
      expect(body).toEqual({
        assetClass: 5,
        name: 'Bitcoin',
        currency: 'PLN',
        instrumentId,
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('is not shown / not sent when editing', async () => {
      await setup({ portfolioId, asset: cashAsset });

      expect(renderedForms()).toEqual([CashAssetForm]);
      expect((fixture.nativeElement as HTMLElement).querySelector('mat-checkbox')).toBeNull();

      fetchSpy.mockResolvedValue(jsonResponse(cashAsset));
      await component['onSubmit']();

      const request = fetchSpy.mock.calls[0][0] as Request;
      expect(request.method).toBe('PUT');
      const body = await request.json();
      expect(body).not.toHaveProperty('initialTransaction');
      expect(body).toEqual({ assetClass: 0, name: 'Checking account', currency: 'PLN' });
    });
  });

  describe('routes server field errors to the active form', () => {
    it('a top-level field error lands on the active form control', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(
        jsonResponse(
          { detail: 'Validation failed.', errors: { Name: ['The Name field is required.'] } },
          400,
        ),
      );

      await pickTile(ASSET_CLASS.Cash);
      findControl(activeForm().form, 'name').setValue('Something');

      await component['onSubmit']();

      expect(findControl(activeForm().form, 'name').hasError('server')).toBe(true);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('the currency-locked error lands on the currency control when editing', async () => {
      await setup({ portfolioId, asset: { ...cashAsset, transactionCount: 1 } });
      fetchSpy.mockResolvedValue(
        jsonResponse(
          {
            detail: 'Validation failed.',
            errors: { Currency: ["The currency can't change once the asset has transactions."] },
          },
          400,
        ),
      );

      findControl(activeForm().form, 'currency').setValue('EUR');

      await component['onSubmit']();

      expect(findControl(activeForm().form, 'currency').hasError('server')).toBe(true);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('a nested InitialTransaction.* error lands on the first-transaction sub-form', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(
        jsonResponse(
          {
            detail: 'Validation failed.',
            errors: { 'InitialTransaction.Quantity': ['The field Quantity must be non-negative.'] },
          },
          400,
        ),
      );

      await pickTile(ASSET_CLASS.Cash);
      findControl(activeForm().form, 'name').setValue('Checking account');
      await toggleFirstTransaction(fixture);
      findControl(activeForm().form, 'quantity').setValue(5);

      await component['onSubmit']();

      expect(findControl(activeForm().form, 'quantity').hasError('server')).toBe(true);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('anything else lands on the banner', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(
        jsonResponse({ detail: 'Something odd happened.', errorCode: 'Unexpected.Whatever' }, 400),
      );

      await pickTile(ASSET_CLASS.RealEstate);
      findControl(activeForm().form, 'name').setValue('Apartment');
      findControl(activeForm().form, 'manualValue').setValue(650000);

      await component['onSubmit']();
      fixture.detectChanges();
      await fixture.whenStable();

      const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
      expect(banner?.textContent).toContain('Something odd happened.');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });

  it('closes with false on cancel', async () => {
    await setup({ portfolioId });

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  describe('in Polish', () => {
    it('renders the type picker step in Polish', async () => {
      await setup({ portfolioId });
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'h2'),
        ...labelsOf(element, 'mat-dialog-actions button'),
      ];

      const english = texts();
      expect(english).toEqual(['New asset', 'Cancel']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the form step in Polish', async () => {
      await setup({ portfolioId });
      await pickTile(ASSET_CLASS.Cash);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'h2'),
        ...attributesOf(element, 'h2 button', 'aria-label'),
        ...labelsOf(element, 'mat-dialog-actions button'),
      ];

      const english = texts();
      expect(english).toEqual(['New asset', 'Back to asset types', 'Cancel', 'Create']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the edit title and save button in Polish', async () => {
      await setup({ portfolioId, asset: cashAsset });
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'h2'),
        ...labelsOf(element, 'mat-dialog-actions button'),
      ];

      const english = texts();
      expect(english).toEqual(['Edit asset', 'Cancel', 'Save']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('shows the missing-instrument message in Polish', async () => {
      await setup({ portfolioId });
      await pickTile(ASSET_CLASS.Stock);
      findControl(activeForm().form, 'name').setValue('Apple');
      await switchLanguage(fixture, 'pl');

      await component['onSubmit']();
      fixture.detectChanges();
      await fixture.whenStable();

      const banner = labelsOf(fixture.nativeElement as HTMLElement, '[role="alert"]');
      expect(banner).toHaveLength(1);
      expect(polishProblems([INSTRUMENT_REQUIRED_MESSAGE], banner)).toEqual([]);
      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('shows a client-side validation message from a form step in Polish', async () => {
      await setup({ portfolioId });
      await pickTile(ASSET_CLASS.Other);
      await showValidationErrors(fixture, activeForm().form);
      const errors = () => labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');

      const english = errors();
      expect(english).toEqual(['Name is required.']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, errors())).toEqual([]);
    });
  });
});
