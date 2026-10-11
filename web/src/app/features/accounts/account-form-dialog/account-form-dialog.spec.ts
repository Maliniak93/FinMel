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
  isArchived: false,
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
});
