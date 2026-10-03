import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { PortfolioResponse } from '../../../api/portfolio';
import { PortfolioFormDialog, type PortfolioFormDialogData } from './portfolio-form-dialog';
import { provideI18nTesting } from '../../../core/i18n/testing';
import {
  labelsOf,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  textsOf,
} from '../../../../testing/i18n';

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

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(data: PortfolioFormDialogData): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');

    await TestBed.configureTestingModule({
      imports: [PortfolioFormDialog],
      providers: [
        provideI18nTesting(),
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

  it('shows labels and validation in Polish', async () => {
    await setup({});
    const element = fixture.nativeElement as HTMLElement;
    component['form'].controls.name.setValue('');
    component['form'].controls.description.setValue('a'.repeat(1001));
    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();
    const texts = () => [
      ...labelsOf(element, 'h2'),
      ...labelsOf(element, 'mat-label'),
      ...labelsOf(element, 'mat-error'),
      ...labelsOf(element, 'mat-dialog-actions button'),
    ];

    const english = texts();
    expect(english).toEqual([
      'New portfolio',
      'Name',
      'Description',
      'Currency',
      'Name is required.',
      'Description is too long.',
      'Cancel',
      'Create',
    ]);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);

    component['form'].controls.name.setValue('a'.repeat(201));
    component['form'].controls.description.setValue('');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(polishProblems(['Name is too long.'], textsOf(element, 'mat-error'))).toEqual([]);
  });

  it('shows the edit title and save button in Polish', async () => {
    await setup({ portfolio: existingPortfolio });
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      ...labelsOf(element, 'h2'),
      ...labelsOf(element, 'mat-dialog-actions button'),
    ];

    const english = texts();
    expect(english).toEqual(['Edit portfolio', 'Cancel', 'Save']);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });

  it('shows the out-of-set currency notice in Polish', async () => {
    await setup({ portfolio: { ...existingPortfolio, currency: 'GBP' } });
    const element = fixture.nativeElement as HTMLElement;
    const hint = () => textsOf(element, 'mat-hint');

    const english = hint();
    expect(english).toHaveLength(1);
    expect(english[0]).toContain("isn't PLN, EUR or USD");

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, hint())).toEqual([]);
  });
});
