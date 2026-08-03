import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNativeDateAdapter } from '@angular/material/core';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { AssetResponse } from '../../../api/portfolio';
import { AssetFormDialog, type AssetFormDialogData } from './asset-form-dialog';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const portfolioId = '22222222-2222-2222-2222-222222222222';

const existingAsset: AssetResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  portfolioId,
  assetClass: 2,
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  manualValue: 1000,
  manualValueDate: '2020-06-15',
  transactionCount: 0,
};

describe('AssetFormDialog', () => {
  let fixture: ComponentFixture<AssetFormDialog>;
  let component: AssetFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(data: AssetFormDialogData): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');

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

  it('creates with the form values and closes with true on success', async () => {
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingAsset, name: 'New asset' }, 201));

    component['form'].controls.name.setValue('New asset');
    component['form'].controls.manualValue.setValue(500);

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/portfolios/${portfolioId}/assets`);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('pre-fills the form from an existing asset and updates via PUT on success', async () => {
    await setup({ portfolioId, asset: existingAsset });
    fetchSpy.mockResolvedValue(jsonResponse(existingAsset));

    expect(component['form'].controls.name.value).toBe('Apple');
    expect(component['form'].controls.currency.value).toBe('USD');
    expect(component['form'].controls.manualValue.value).toBe(1000);
    expect(component['form'].controls.manualValueDate.value).toEqual(new Date(2020, 5, 15));

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(existingAsset.id);
    const body = await request.json();
    expect(body.manualValueDate).toBe('2020-06-15');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('does not submit an invalid form', async () => {
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(jsonResponse(existingAsset, 201));

    component['form'].controls.name.setValue('');

    await component['onSubmit']();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces a validation error on the matching field, without closing', async () => {
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(
      jsonResponse(
        {
          detail: 'Validation failed.',
          errors: { Name: ['The Name field is required.'] },
        },
        400,
      ),
    );

    component['form'].controls.name.setValue('Something');

    await component['onSubmit']();

    expect(component['form'].controls.name.hasError('server')).toBe(true);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces a generic server error as the form banner', async () => {
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(
      jsonResponse(
        { detail: 'Amount must not be negative.', errorCode: 'Money.NegativeAmount' },
        400,
      ),
    );

    component['form'].controls.name.setValue('Something');

    await component['onSubmit']();

    expect(component['formError']()).toBe('Amount must not be negative.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup({ portfolioId });

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
