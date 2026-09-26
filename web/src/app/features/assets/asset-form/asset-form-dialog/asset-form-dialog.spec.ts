import type { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';

import { client as marketDataClient } from '../../../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../../../api/portfolio/client.gen';
import { DepositFormDialog } from '../../../deposits/deposit-form-dialog/deposit-form-dialog';
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
  toggleFirstTransaction,
} from '../testing/asset-form-fixtures';
import { AssetFormDialog, type AssetFormDialogData } from './asset-form-dialog';

type AssetFormComponent = CashAssetForm | SecurityAssetForm | GoldAssetForm | ManualAssetForm;

const FORM_COMPONENTS: readonly Type<AssetFormComponent>[] = [
  CashAssetForm,
  SecurityAssetForm,
  GoldAssetForm,
  ManualAssetForm,
];

// Which per-kind form each AssetClass opens (spec #105, Scope → "Form components"). Deposit opens
// no form here: term-deposits hands it to DepositFormDialog.
const EXPECTED_FORM: Record<number, Type<AssetFormComponent>> = {
  [ASSET_CLASS.Cash]: CashAssetForm,
  [ASSET_CLASS.Stock]: SecurityAssetForm,
  [ASSET_CLASS.Etf]: SecurityAssetForm,
  [ASSET_CLASS.Bond]: SecurityAssetForm,
  [ASSET_CLASS.Crypto]: SecurityAssetForm,
  [ASSET_CLASS.PreciousMetal]: GoldAssetForm,
  [ASSET_CLASS.RealEstate]: ManualAssetForm,
  [ASSET_CLASS.Other]: ManualAssetForm,
};

// The shell: picker on create, the stored class's form on edit, and the one place that POSTs/PUTs
// and maps ProblemDetails. The submit under test is called on the shell itself and asserted on the
// raw request the generated client hands to `fetch`.
describe('AssetFormDialog', () => {
  let fixture: ComponentFixture<AssetFormDialog>;
  let component: AssetFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
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
      expect(picker()!.querySelectorAll('button')).toHaveLength(9);
      expect(renderedForms()).toEqual([]);

      for (const { value } of ASSET_CLASSES.filter((c) => c.value !== ASSET_CLASS.Deposit)) {
        await pickTile(value);

        expect(picker()).toBeNull();
        expect(renderedForms()).toEqual([EXPECTED_FORM[value]]);
        expect(activeForm().assetClass()).toBe(value);

        await goBack();
      }

      expect(fetchSpy).not.toHaveBeenCalled();
    });

    // term-deposits AC-16: a Deposit is a term deposit with real terms, created only through the
    // deposit endpoint — its tile opens DepositFormDialog preset to this portfolio, never the cash
    // form, and this dialog closes with the deposit dialog's result so the asset list reloads.
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

    it('back returns to the picker', async () => {
      await setup({ portfolioId });

      await pickTile(ASSET_CLASS.Cash);
      findControl(activeForm().form, 'name').setValue('Checking account');

      await goBack();

      expect(picker()).not.toBeNull();
      expect(renderedForms()).toEqual([]);
      expect(fetchSpy).not.toHaveBeenCalled();
      expect(dialogRef.close).not.toHaveBeenCalled();

      // The abandoned form was discarded, not kept around behind the picker.
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
      // The class is fixed on edit: no class control anywhere, in the form or on screen.
      expect(hasControl(activeForm().form, 'assetClass')).toBe(false);
      expect(renderedText(fixture)).not.toContain('Asset class');
      // Pre-filled, including the instrument fetched through GET instruments/{id}.
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

    // transactions-pln-value-and-fee-removal: a currency change on an asset that already has
    // transactions is a 400 field error on Currency, which lands on the currency control.
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
});
