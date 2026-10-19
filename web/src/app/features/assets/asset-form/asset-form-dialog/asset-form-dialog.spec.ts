import type { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';

import {
  attributesOf,
  labelsOf,
  polishProblems,
  restoreEnglish,
  switchLanguage,
} from '../../../../../testing/i18n';
import { client as marketDataClient } from '../../../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../../../api/portfolio/client.gen';
import { ASSET_CLASS } from '../../asset-class';
import { CashAssetForm } from '../forms/cash-asset-form/cash-asset-form';
import { SecurityAssetForm } from '../forms/security-asset-form/security-asset-form';
import {
  cashAsset,
  etfSearchResult,
  findControl,
  hasControl,
  instrumentId,
  jsonResponse,
  pickInstrument,
  renderedText,
  requestUrl,
  showValidationErrors,
  stockAsset,
  stockInstrumentDetails,
  toggleFirstTransaction,
} from '../testing/asset-form-fixtures';
import {
  pickSelectOption,
  selectOptionLabels,
  selectTriggerState,
} from '../../../deposits/testing/deposit-fixtures';
import {
  assetGroups,
  blueGroupId,
  greenGroupId,
  isGroupListRequest,
} from '../../../groups/testing/group-fixtures';
import { INSTRUMENT_REQUIRED_MESSAGE } from '../blocks/instrument-picker/instrument-picker';
import { AssetFormDialog, type AssetFormDialogData } from './asset-form-dialog';
import { provideI18nTesting } from '../../../../core/i18n/testing';

type AssetFormComponent = CashAssetForm | SecurityAssetForm;

const FORM_COMPONENTS: readonly Type<AssetFormComponent>[] = [CashAssetForm, SecurityAssetForm];

const EXPECTED_FORM: Record<number, Type<AssetFormComponent>> = {
  [ASSET_CLASS.Cash]: CashAssetForm,
  [ASSET_CLASS.Stock]: SecurityAssetForm,
  [ASSET_CLASS.Etf]: SecurityAssetForm,
};

const xtbRegularId = 'a1111111-1111-1111-1111-111111111111';
const xtbIkeId = 'a2222222-2222-2222-2222-222222222222';

const accounts = [
  {
    id: xtbRegularId,
    name: 'XTB zwykly',
    type: 0,
    wrapper: 0,
    isArchived: false,
    groupId: greenGroupId,
  },
  { id: xtbIkeId, name: 'XTB IKE', type: 0, wrapper: 1, isArchived: false, groupId: blueGroupId },
  {
    id: 'a3333333-3333-3333-3333-333333333333',
    name: 'Obligacje IKE',
    type: 1,
    wrapper: 1,
    isArchived: false,
  },
  {
    id: 'a4444444-4444-4444-4444-444444444444',
    name: 'Old broker',
    type: 0,
    wrapper: 0,
    isArchived: true,
  },
];

const withAccounts = async (): Promise<Response> => jsonResponse(accounts);

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
    fetchImplementation: (input: unknown) => Promise<Response> = withAccounts,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input) =>
        isGroupListRequest(input) ? jsonResponse(assetGroups) : fetchImplementation(input),
      );

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

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => typeof request !== 'string' && request.method !== 'GET');
  }

  describe('create', () => {
    for (const assetClass of [ASSET_CLASS.Cash, ASSET_CLASS.Stock, ASSET_CLASS.Etf]) {
      it(`renders the form of class ${assetClass}`, async () => {
        await setup({ assetClass });

        expect(renderedForms()).toEqual([EXPECTED_FORM[assetClass]]);
        expect(activeForm().assetClass()).toBe(assetClass);
      });
    }

    it('does not load accounts for a Cash asset', async () => {
      await setup({ assetClass: ASSET_CLASS.Cash });

      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('POSTs a Cash body without an account and closes with the created id', async () => {
      await setup({ assetClass: ASSET_CLASS.Cash }, async () => jsonResponse(cashAsset, 201));
      findControl(activeForm().form, 'name').setValue('Checking account');

      await component['onSubmit']();

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const request = fetchSpy.mock.calls[0][0] as Request;
      expect(request.method).toBe('POST');
      expect(request.url).toContain(`/api/portfolio/assets`);
      expect(await request.json()).toEqual({
        assetClass: 0,
        name: 'Checking account',
        currency: 'PLN',
        initialTransaction: null,
      });
      expect(dialogRef.close).toHaveBeenCalledWith(cashAsset.id);
    });

    it('POSTs a security with the chosen account', async () => {
      await setup({ assetClass: ASSET_CLASS.Etf }, async (input) =>
        typeof input !== 'string' && (input as Request).method === 'POST'
          ? jsonResponse(stockAsset, 201)
          : jsonResponse(accounts),
      );
      await pickSelectOption(fixture, 'accountId', 'XTB IKE');
      findControl(activeForm().form, 'name').setValue('World ETF');
      await pickInstrument(fixture, etfSearchResult);

      await component['onSubmit']();

      const [post] = writeRequests();
      expect(post.url).toContain(`/api/portfolio/assets`);
      expect(await post.json()).toEqual(
        expect.objectContaining({ accountId: xtbIkeId, assetClass: ASSET_CLASS.Etf }),
      );
      expect(dialogRef.close).toHaveBeenCalledWith(stockAsset.id);
    });

    it('sends nothing while a security has no account chosen', async () => {
      await setup({ assetClass: ASSET_CLASS.Etf });
      findControl(activeForm().form, 'name').setValue('World ETF');
      await pickInstrument(fixture, etfSearchResult);

      await component['onSubmit']();

      expect(writeRequests()).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('sends nothing while a security form has no instrument selected', async () => {
      await setup({ assetClass: ASSET_CLASS.Etf });
      await pickSelectOption(fixture, 'accountId', 'XTB IKE');
      findControl(activeForm().form, 'name').setValue('World ETF');

      await component['onSubmit']();

      expect(writeRequests()).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('an invalid initial transaction blocks submission', async () => {
      await setup({ assetClass: ASSET_CLASS.Cash });
      findControl(activeForm().form, 'name').setValue('Checking account');
      await toggleFirstTransaction(fixture);
      findControl(activeForm().form, 'quantity').setValue(-5);

      await component['onSubmit']();

      expect(fetchSpy).not.toHaveBeenCalled();
    });
  });

  describe('opened from a watched instrument', () => {
    const watchedFetch = async (input: unknown): Promise<Response> => {
      if (typeof input !== 'string' && (input as Request).method === 'POST') {
        return jsonResponse(stockAsset, 201);
      }
      if (requestUrl(input).includes(`/api/marketdata/instruments/${instrumentId}`)) {
        return jsonResponse(stockInstrumentDetails);
      }
      return jsonResponse(accounts);
    };

    it('preselects the instrument from data', async () => {
      await setup({ assetClass: ASSET_CLASS.Stock, instrumentId }, watchedFetch);

      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(activeForm().assetClass()).toBe(ASSET_CLASS.Stock);
        expect(findControl(activeForm().form, 'instrument').value).toEqual(
          expect.objectContaining({ id: instrumentId, ticker: 'AAPL' }),
        );
      });
    });

    it('closes with the created id', async () => {
      await setup({ assetClass: ASSET_CLASS.Stock, instrumentId }, watchedFetch);
      await pickSelectOption(fixture, 'accountId', 'XTB IKE');
      findControl(activeForm().form, 'name').setValue('Apple');
      await vi.waitFor(() => fixture.detectChanges());

      await component['onSubmit']();

      expect(dialogRef.close).toHaveBeenCalledWith(stockAsset.id);
    });
  });

  describe('account picker', () => {
    it('offers only unarchived Brokerage accounts for a Stock', async () => {
      await setup({ assetClass: ASSET_CLASS.Stock });

      const labels = await selectOptionLabels(fixture, 'accountId');

      expect(labels).toContain('XTB zwykly');
      expect(labels).toContain('XTB IKE');
      expect(labels).not.toContain('Obligacje IKE');
      expect(labels).not.toContain('Old broker');
    });

    it('shows no account picker for a Cash asset', async () => {
      await setup({ assetClass: ASSET_CLASS.Cash });

      expect(
        (fixture.nativeElement as HTMLElement).querySelector('[formcontrolname="accountId"]'),
      ).toBeNull();
    });
  });

  describe('edit', () => {
    it('edit opens the form of the stored class', async () => {
      await setup({ assetClass: stockAsset.assetClass, asset: stockAsset }, async (input) =>
        requestUrl(input).includes('/instruments/')
          ? jsonResponse(stockInstrumentDetails)
          : jsonResponse(stockAsset),
      );

      expect(renderedForms()).toEqual([SecurityAssetForm]);
      expect(activeForm().assetClass()).toBe(ASSET_CLASS.Stock);
      expect(hasControl(activeForm().form, 'assetClass')).toBe(false);
      expect(renderedText(fixture)).not.toContain('Asset class');
      expect(
        (fixture.nativeElement as HTMLElement).querySelector('[formcontrolname="accountId"]'),
      ).toBeNull();
      expect(findControl(activeForm().form, 'name').value).toBe('Apple');
      expect(
        fetchSpy.mock.calls.some((call: unknown[]) =>
          requestUrl(call[0]).includes(`/instruments/${instrumentId}`),
        ),
      ).toBe(true);
      expect(renderedText(fixture)).toContain('AAPL');

      const callsBefore = fetchSpy.mock.calls.length;
      await component['onSubmit']();

      const request = fetchSpy.mock.calls[callsBefore][0] as Request;
      expect(request.method).toBe('PUT');
      expect(request.url).toContain(`/api/portfolio/assets/${stockAsset.id}`);
      const body = await request.json();
      expect(body).not.toHaveProperty('initialTransaction');
      expect(body).toEqual({
        assetClass: 2,
        name: 'Apple',
        currency: 'USD',
        instrumentId,
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('is not shown / not sent when editing', async () => {
      await setup({ assetClass: cashAsset.assetClass, asset: cashAsset });

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

  describe('group', () => {
    const chooseAccountFetch = async (input: unknown): Promise<Response> =>
      typeof input !== 'string' && (input as Request).method === 'POST'
        ? jsonResponse(stockAsset, 201)
        : jsonResponse(accounts);

    it('prefills the account default', async () => {
      await setup({ assetClass: ASSET_CLASS.Etf }, chooseAccountFetch);
      await pickSelectOption(fixture, 'accountId', 'XTB IKE');
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(selectTriggerState(fixture, 'groupId').triggerText).toContain('Emerytura');
      });
      findControl(activeForm().form, 'name').setValue('World ETF');
      await pickInstrument(fixture, etfSearchResult);

      await component['onSubmit']();

      const [post] = writeRequests();
      expect(await post.json()).toEqual(
        expect.objectContaining({ accountId: xtbIkeId, groupId: blueGroupId }),
      );
    });

    it('prefills the default of a preset account', async () => {
      await setup({ assetClass: ASSET_CLASS.Etf, accountId: xtbIkeId }, chooseAccountFetch);
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(selectTriggerState(fixture, 'groupId').triggerText).toContain('Emerytura');
      });
      findControl(activeForm().form, 'name').setValue('World ETF');
      await pickInstrument(fixture, etfSearchResult);

      await component['onSubmit']();

      const [post] = writeRequests();
      expect(await post.json()).toEqual(
        expect.objectContaining({ accountId: xtbIkeId, groupId: blueGroupId }),
      );
    });

    it('keeps a manual choice', async () => {
      await setup({ assetClass: ASSET_CLASS.Etf }, chooseAccountFetch);
      await pickSelectOption(fixture, 'accountId', 'XTB IKE');
      await pickSelectOption(fixture, 'groupId', 'No group');
      await pickSelectOption(fixture, 'accountId', 'XTB zwykly');
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(selectTriggerState(fixture, 'groupId').triggerText).toContain('No group');
      });
      findControl(activeForm().form, 'name').setValue('World ETF');
      await pickInstrument(fixture, etfSearchResult);

      await component['onSubmit']();

      const [post] = writeRequests();
      expect(await post.json()).toEqual(
        expect.objectContaining({ accountId: xtbRegularId, groupId: null }),
      );
    });

    describe('on edit', () => {
      const groupedStock = { ...stockAsset, groupId: blueGroupId };
      const editFetch = async (input: unknown): Promise<Response> =>
        requestUrl(input).includes('/instruments/')
          ? jsonResponse(stockInstrumentDetails)
          : jsonResponse(stockAsset);

      it('assigns after a changed group', async () => {
        await setup({ assetClass: groupedStock.assetClass, asset: groupedStock }, editFetch);
        await vi.waitFor(() => {
          fixture.detectChanges();
          expect(selectTriggerState(fixture, 'groupId').triggerText).toContain('Emerytura');
        });
        await pickSelectOption(fixture, 'groupId', 'Poduszka');

        await component['onSubmit']();

        const [update, assignment] = writeRequests();
        expect(update.method).toBe('PUT');
        expect(update.url).toContain(`/api/portfolio/assets/${stockAsset.id}`);
        expect(assignment.method).toBe('PUT');
        expect(assignment.url).toContain('/api/portfolio/asset-groups/assignment');
        expect(await assignment.json()).toEqual({
          assetIds: [stockAsset.id],
          groupId: greenGroupId,
        });
        expect(dialogRef.close).toHaveBeenCalledWith(true);
      });

      it('skips assignment when unchanged', async () => {
        await setup({ assetClass: groupedStock.assetClass, asset: groupedStock }, editFetch);
        await vi.waitFor(() => {
          fixture.detectChanges();
          expect(selectTriggerState(fixture, 'groupId').triggerText).toContain('Emerytura');
        });

        await component['onSubmit']();

        const writes = writeRequests();
        expect(writes).toHaveLength(1);
        expect(writes[0].method).toBe('PUT');
        expect(writes[0].url).toContain(`/api/portfolio/assets/${stockAsset.id}`);
      });
    });
  });

  describe('routes server field errors to the active form', () => {
    it('a top-level field error lands on the active form control', async () => {
      await setup({ assetClass: ASSET_CLASS.Cash });
      fetchSpy.mockResolvedValue(
        jsonResponse(
          { detail: 'Validation failed.', errors: { Name: ['The Name field is required.'] } },
          400,
        ),
      );
      findControl(activeForm().form, 'name').setValue('Something');

      await component['onSubmit']();

      expect(findControl(activeForm().form, 'name').hasError('server')).toBe(true);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('the currency-locked error lands on the currency control when editing', async () => {
      await setup({
        assetClass: cashAsset.assetClass,
        asset: { ...cashAsset, transactionCount: 1 },
      });
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
      await setup({ assetClass: ASSET_CLASS.Cash });
      fetchSpy.mockResolvedValue(
        jsonResponse(
          {
            detail: 'Validation failed.',
            errors: { 'InitialTransaction.Quantity': ['The field Quantity must be non-negative.'] },
          },
          400,
        ),
      );
      findControl(activeForm().form, 'name').setValue('Checking account');
      await toggleFirstTransaction(fixture);
      findControl(activeForm().form, 'quantity').setValue(5);

      await component['onSubmit']();

      expect(findControl(activeForm().form, 'quantity').hasError('server')).toBe(true);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('anything else lands on the banner', async () => {
      await setup({ assetClass: ASSET_CLASS.Cash });
      fetchSpy.mockResolvedValue(
        jsonResponse({ detail: 'Something odd happened.', errorCode: 'Unexpected.Whatever' }, 400),
      );
      findControl(activeForm().form, 'name').setValue('Checking account');

      await component['onSubmit']();
      fixture.detectChanges();
      await fixture.whenStable();

      const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
      expect(banner?.textContent).toContain('Something odd happened.');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });

  it('closes with false on cancel', async () => {
    await setup({ assetClass: ASSET_CLASS.Cash });

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  describe('in Polish', () => {
    it('renders the create title and buttons in Polish', async () => {
      await setup({ assetClass: ASSET_CLASS.Cash });
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'h2'),
        ...attributesOf(element, 'h2 button', 'aria-label'),
        ...labelsOf(element, 'mat-dialog-actions button'),
      ];

      const english = texts();
      expect(english).toEqual(['New asset', 'Cancel', 'Create']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the edit title and save button in Polish', async () => {
      await setup({ assetClass: cashAsset.assetClass, asset: cashAsset });
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

    it('renders the account picker label in Polish', async () => {
      await setup({ assetClass: ASSET_CLASS.Stock });
      const labels = () => labelsOf(fixture.nativeElement as HTMLElement, 'mat-label');

      const english = labels().filter((label) => label === 'Account');
      expect(english).toEqual(['Account']);

      await switchLanguage(fixture, 'pl');

      expect(labels()).toContain('Rachunek');
    });

    it('shows the missing-instrument message in Polish', async () => {
      await setup({ assetClass: ASSET_CLASS.Stock });
      findControl(activeForm().form, 'name').setValue('Apple');
      await switchLanguage(fixture, 'pl');

      await component['onSubmit']();
      fixture.detectChanges();
      await fixture.whenStable();

      const banner = labelsOf(fixture.nativeElement as HTMLElement, '[role="alert"]');
      expect(banner).toHaveLength(1);
      expect(polishProblems([INSTRUMENT_REQUIRED_MESSAGE], banner)).toEqual([]);
      expect(writeRequests()).toEqual([]);
    });

    it('shows a client-side validation message from a form step in Polish', async () => {
      await setup({ assetClass: ASSET_CLASS.Cash });
      await showValidationErrors(fixture, activeForm().form);
      const errors = () => labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');

      const english = errors();
      expect(english).toEqual(['Name is required.']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, errors())).toEqual([]);
    });
  });
});
