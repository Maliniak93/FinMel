import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { AccountResponse } from '../../../api/portfolio';
import { AccountFormDialog, type AccountFormDialogData } from './account-form-dialog';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { restoreEnglish } from '../../../../testing/i18n';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const existingAccount: AccountResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'XTB IKE',
  type: 0,
  wrapper: 1,
  currency: 'PLN',
  isArchived: false,
  cashAssetId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
  cashBalance: 0,
};

describe('AccountFormDialog', () => {
  let fixture: ComponentFixture<AccountFormDialog>;
  let component: AccountFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(data: AccountFormDialogData): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');

    await TestBed.configureTestingModule({
      imports: [AccountFormDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AccountFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('creates with name, type and wrapper and closes with true', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(
      jsonResponse({ ...existingAccount, name: 'Obligacje IKE', type: 1 }, 201),
    );

    component['form'].controls.name.setValue('Obligacje IKE');
    component['form'].controls.type.setValue(1);
    component['form'].controls.wrapper.setValue(1);
    await component['onSubmit']();

    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('POST');
    expect(request.url).toContain('/api/portfolio/accounts');
    expect(JSON.parse(await request.text())).toEqual({
      name: 'Obligacje IKE',
      type: 1,
      wrapper: 1,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('disables type and wrapper on edit and sends only the name', async () => {
    await setup({ account: existingAccount });
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingAccount, name: 'XTB IKE 2' }));

    expect(component['form'].controls.type.disabled).toBe(true);
    expect(component['form'].controls.wrapper.disabled).toBe(true);
    expect(component['form'].controls.name.value).toBe('XTB IKE');

    component['form'].controls.name.setValue('XTB IKE 2');
    await component['onSubmit']();

    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(existingAccount.id);
    expect(JSON.parse(await request.text())).toEqual({ name: 'XTB IKE 2' });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('offers only investment types without a preset', async () => {
    await setup({});

    expect(component['types'].map((type) => type.value)).toEqual([0, 1]);
  });

  it('fixes the type to Bank with the Bank preset', async () => {
    await setup({ type: 2 });

    expect(component['types'].map((type) => type.value)).toEqual([2]);
    expect(component['form'].controls.type.value).toBe(2);
  });

  it('fixes the type to Bank when editing a Bank account', async () => {
    await setup({ account: { ...existingAccount, type: 2, wrapper: 0 } });

    expect(component['types'].map((type) => type.value)).toEqual([2]);
    expect(component['form'].controls.type.value).toBe(2);
    expect(component['form'].controls.type.disabled).toBe(true);
  });

  it('keeps type and wrapper enabled on create', async () => {
    await setup({});

    expect(component['form'].controls.type.enabled).toBe(true);
    expect(component['form'].controls.wrapper.enabled).toBe(true);
  });

  it('does not submit an empty name', async () => {
    await setup({});

    component['form'].controls.name.setValue('');
    await component['onSubmit']();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces a duplicate-name conflict on the name field, without closing', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(
      jsonResponse(
        {
          detail: 'An account with this name already exists.',
          errorCode: 'Conflict.DuplicateName',
        },
        409,
      ),
    );

    component['form'].controls.name.setValue('XTB IKE');
    await component['onSubmit']();

    expect(component['form'].controls.name.hasError('server')).toBe(true);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup({});

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('shows currency, variant, bank and rate for Bank and hides the wrapper', async () => {
    await setup({});
    const element = fixture.nativeElement as HTMLElement;
    const has = (name: string) => element.querySelector(`[formControlName="${name}"]`) !== null;
    expect(has('wrapper')).toBe(true);
    expect(has('currency')).toBe(false);

    component['form'].controls.type.setValue(2);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(has('currency')).toBe(true);
    expect(has('variant')).toBe(true);
    expect(has('bankName')).toBe(true);
    expect(has('annualInterestRatePercent')).toBe(true);
    expect(has('wrapper')).toBe(false);
  });

  it('creates a Bank account with its terms', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingAccount, type: 2 }, 201));

    component['form'].controls.name.setValue('Nest Oszczędnościowe');
    component['form'].controls.type.setValue(2);
    component['form'].controls.currency.setValue('PLN');
    component['form'].controls.variant.setValue(1);
    component['form'].controls.bankName.setValue('Nest Bank');
    component['form'].controls.annualInterestRatePercent.setValue(5);
    await component['onSubmit']();

    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('POST');
    expect(JSON.parse(await request.text())).toMatchObject({
      name: 'Nest Oszczędnościowe',
      type: 2,
      currency: 'PLN',
      variant: 1,
      bankName: 'Nest Bank',
      annualInterestRatePercent: 5,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('on edit of a Bank account disables currency and sends variant, bank and rate', async () => {
    const bank = {
      ...existingAccount,
      name: 'mBank',
      type: 2,
      wrapper: 0,
      currency: 'PLN',
      bank: {
        variant: 0,
        bankName: 'mBank',
        annualInterestRatePercent: 0,
        interestDue: false,
        duePeriodCount: 0,
        lastSettlement: null,
        interestCreditedNet: 0,
      },
    } as AccountResponse;
    await setup({ account: bank });
    fetchSpy.mockResolvedValue(jsonResponse(bank));

    expect(component['form'].controls.currency.disabled).toBe(true);
    expect(component['form'].controls.type.disabled).toBe(true);

    component['form'].controls.annualInterestRatePercent.setValue(4);
    await component['onSubmit']();

    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('PUT');
    expect(JSON.parse(await request.text())).toEqual({
      name: 'mBank',
      variant: 0,
      bankName: 'mBank',
      annualInterestRatePercent: 4,
    });
  });
});
