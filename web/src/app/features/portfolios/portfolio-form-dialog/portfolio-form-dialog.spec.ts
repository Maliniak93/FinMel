import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { PortfolioResponse } from '../../../api/portfolio';
import { PortfolioFormDialog, type PortfolioFormDialogData } from './portfolio-form-dialog';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const existingPortfolio: PortfolioResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Retirement',
  description: 'Long-term',
  currency: 'PLN',
  isArchived: false,
  assetCount: 0,
};

describe('PortfolioFormDialog', () => {
  let fixture: ComponentFixture<PortfolioFormDialog>;
  let component: PortfolioFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(data: PortfolioFormDialogData): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');

    await TestBed.configureTestingModule({
      imports: [PortfolioFormDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PortfolioFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('creates with the form values and closes with true on success', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingPortfolio, name: 'New one' }, 201));

    component['form'].controls.name.setValue('New one');

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('POST');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('updates an existing portfolio via PUT and closes with true on success', async () => {
    await setup({ portfolio: existingPortfolio });
    fetchSpy.mockResolvedValue(jsonResponse(existingPortfolio));

    expect(component['form'].controls.name.value).toBe('Retirement');

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(existingPortfolio.id);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('does not submit an invalid form', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(jsonResponse(existingPortfolio, 201));

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
          detail: "A portfolio named 'New one' already exists.",
          errorCode: 'Conflict.DuplicatePortfolioName',
        },
        409,
      ),
    );

    component['form'].controls.name.setValue('New one');

    await component['onSubmit']();

    expect(component['form'].controls.name.hasError('server')).toBe(true);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup({});

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('defaults a new portfolio to PLN, offering only the supported set', async () => {
    await setup({});

    expect(component['form'].controls.currency.value).toBe('PLN');
    expect(component['legacyCurrency']).toBeNull();
    expect(component['currencies'].map((c) => c.code)).toEqual(['PLN', 'EUR', 'USD']);
  });

  it('editing a portfolio already in the supported set carries no legacy flag', async () => {
    await setup({ portfolio: existingPortfolio });

    expect(component['form'].controls.currency.value).toBe('PLN');
    expect(component['legacyCurrency']).toBeNull();
  });

  // Simulates picking an option from the mat-select — the control only ever emits one of
  // `currencies`' codes (Validators.required is the only validator left; format is enforced by
  // construction, not a regex), so setting a value straight from that list is the same effect a
  // real user selection has.
  it('round-trips a currency chosen from the dropdown set on create', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingPortfolio, currency: 'EUR' }, 201));

    component['form'].controls.name.setValue('New one');
    component['form'].controls.currency.setValue('EUR');
    await component['onSubmit']();

    const createRequest = fetchSpy.mock.calls[0][0] as Request;
    const createBody = JSON.parse(await createRequest.text());
    expect(createBody.currency).toBe('EUR');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('round-trips a currency chosen from the dropdown set on edit', async () => {
    await setup({ portfolio: { ...existingPortfolio, currency: 'EUR' } });
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingPortfolio, currency: 'USD' }));

    component['form'].controls.currency.setValue('USD');
    await component['onSubmit']();

    const updateRequest = fetchSpy.mock.calls[0][0] as Request;
    const updateBody = JSON.parse(await updateRequest.text());
    expect(updateBody.currency).toBe('USD');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  // M1.3's server-side restriction predates this dropdown: a portfolio stored (or seeded) with a
  // currency outside PLN/EUR/USD must still load into the form with its real value — never
  // silently swapped for DEFAULT_CURRENCY, and never blank because no <mat-option> matches it.
  it('editing a portfolio with an out-of-set currency keeps its real value, not blank or defaulted', async () => {
    await setup({ portfolio: { ...existingPortfolio, currency: 'GBP' } });

    expect(component['legacyCurrency']).toBe('GBP');
    expect(component['form'].controls.currency.value).toBe('GBP');
  });

  it('surfaces an unsupported-currency 400 on the currency field, without closing', async () => {
    await setup({ portfolio: { ...existingPortfolio, currency: 'GBP' } });
    fetchSpy.mockResolvedValue(
      jsonResponse(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { Currency: ['Currency must be one of: PLN, EUR, USD.'] },
        },
        400,
      ),
    );

    await component['onSubmit']();

    expect(component['form'].controls.currency.hasError('server')).toBe(true);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
