import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { CashPlaceResponse } from '../../../api/portfolio';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse, renderedText } from '../../assets/asset-form/testing/asset-form-fixtures';
import { pickSelectOption } from '../../deposits/testing/deposit-fixtures';
import { assetGroups, blueGroupId, isGroupListRequest } from '../../groups/testing/group-fixtures';
import { CashPlaceFormDialog, type CashPlaceFormDialogData } from './cash-place-form-dialog';

const place: CashPlaceResponse = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Safe',
  isArchived: false,
  balances: [
    { currency: 'PLN', cashAssetId: '11111111-1111-1111-1111-111111111111', balance: 0 },
    { currency: 'USD', cashAssetId: '22222222-2222-2222-2222-222222222222', balance: 0 },
  ],
};

describe('CashPlaceFormDialog', () => {
  let fixture: ComponentFixture<CashPlaceFormDialog>;
  let component: CashPlaceFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(
    data: CashPlaceFormDialogData,
    writeResponse: () => Response = () => jsonResponse({ id: 'x' }, 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input) =>
        isGroupListRequest(input) ? jsonResponse(assetGroups) : writeResponse(),
      );

    await TestBed.configureTestingModule({
      imports: [CashPlaceFormDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CashPlaceFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function writes(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method !== 'GET');
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('creates a place with the chosen currencies and closes with true', async () => {
    await setup({ mode: 'create' });
    component['form'].controls.name.setValue('  Safe ');
    component['toggleCurrency']('USD', true);

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(request.url).toContain('/api/portfolio/cash-places');
    expect(await request.json()).toEqual({
      name: 'Safe',
      currencies: ['PLN', 'USD'],
      groupId: null,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('sends groupId', async () => {
    await setup({ mode: 'create' });
    component['form'].controls.name.setValue('Safe');
    await pickSelectOption(fixture, 'groupId', 'Emerytura');

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(await request.json()).toEqual({
      name: 'Safe',
      currencies: ['PLN'],
      groupId: blueGroupId,
    });
  });

  it('defaults to PLN and refuses a place with no currency or no name', async () => {
    await setup({ mode: 'create' });
    expect(component['selectedCurrencies']()).toEqual(['PLN']);

    await component['onSubmit']();
    expect(writes()).toEqual([]);

    component['form'].controls.name.setValue('Safe');
    component['toggleCurrency']('PLN', false);
    await component['onSubmit']();
    await settle();

    expect(writes()).toEqual([]);
    expect(renderedText(fixture)).toContain('Choose at least one currency.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('shows a duplicate name inline and keeps the dialog open', async () => {
    await setup({ mode: 'create' }, () =>
      jsonResponse(
        { title: 'Conflict', status: 409, detail: 'Taken.', errorCode: 'Conflict.DuplicateName' },
        409,
      ),
    );
    component['form'].controls.name.setValue('Safe');

    await component['onSubmit']();
    await settle();

    expect(renderedText(fixture)).toContain('A cash place with this name already exists.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('renames through PUT', async () => {
    await setup({ mode: 'rename', place });
    expect(component['form'].controls.name.value).toBe('Safe');
    component['form'].controls.name.setValue('Vault');

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(`/api/portfolio/cash-places/${place.id}`);
    expect(await request.json()).toEqual({ name: 'Vault', groupId: null });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('offers only the currencies the place lacks and adds one', async () => {
    await setup({ mode: 'addCurrency', place });
    expect(component['missingCurrencies']().map((currency) => currency.code)).toEqual(['EUR']);

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/cash-places/${place.id}/currencies`);
    expect(await request.json()).toEqual({ currency: 'EUR' });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('shows a duplicate currency inline', async () => {
    await setup({ mode: 'addCurrency', place }, () =>
      jsonResponse(
        {
          title: 'Conflict',
          status: 409,
          detail: 'Held.',
          errorCode: 'Conflict.DuplicateCurrency',
        },
        409,
      ),
    );

    await component['onSubmit']();
    await settle();

    expect(renderedText(fixture)).toContain('The place already holds this currency.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup({ mode: 'create' });

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
