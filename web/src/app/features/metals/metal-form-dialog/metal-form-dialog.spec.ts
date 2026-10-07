import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { restoreEnglish } from '../../../../testing/i18n';
import { METAL, metalResponse } from '../../../../testing/metal-fixtures';
import {
  findControl,
  hasControl,
  jsonResponse,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  archivedPortfolio,
  reservePortfolio,
  reservePortfolioId,
  savingsPortfolio,
  savingsPortfolioId,
} from '../../deposits/testing/deposit-fixtures';
import { MetalFormDialog, type MetalFormDialogData } from './metal-form-dialog';

describe('MetalFormDialog', () => {
  let fixture: ComponentFixture<MetalFormDialog>;
  let component: MetalFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  function method(input: unknown): string {
    return typeof input === 'string' ? 'GET' : (input as Request).method;
  }

  async function setup(
    data: MetalFormDialogData,
    writeResponse: () => Response = () => jsonResponse(metalResponse(), 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (method(input) === 'GET' && /\/api\/portfolio\/portfolios(\?|$)/.test(requestUrl(input))) {
        return jsonResponse([savingsPortfolio, archivedPortfolio, reservePortfolio]);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [MetalFormDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MetalFormDialog);
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

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => method(input) !== 'GET') as Request[];
  }

  describe('add', () => {
    it('POSTs gold, a 1 oz weight and the first purchase, and closes with true', async () => {
      await setup({});
      await fill({
        portfolioId: reservePortfolioId,
        metal: METAL.Gold,
        name: 'Krugerrand',
        fineWeight: 1,
        weightUnit: 'TroyOunce',
        pieces: 2,
        pricePerPiece: 11000,
        purchaseDate: new Date(2026, 9, 1),
      });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(`/api/portfolio/portfolios/${reservePortfolioId}/metals`);
      expect(await request.json()).toEqual({
        name: 'Krugerrand',
        metal: METAL.Gold,
        fineWeight: 1,
        weightUnit: 'TroyOunce',
        firstPurchase: { pieces: 2, pricePerPiece: 11000, date: '2026-10-01' },
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('preset to a portfolio omits the first purchase when no pieces are entered', async () => {
      await setup({ portfolioId: savingsPortfolioId });
      await fill({ metal: METAL.Silver, name: 'Silver bar', fineWeight: 100, weightUnit: 'Gram' });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.url).toContain(`/api/portfolio/portfolios/${savingsPortfolioId}/metals`);
      const body = await request.json();
      expect(body).not.toHaveProperty('firstPurchase');
      expect(body).toMatchObject({ metal: METAL.Silver, fineWeight: 100, weightUnit: 'Gram' });
    });

    it.each([
      ['name', ''],
      ['fineWeight', 0],
      ['fineWeight', -1],
    ])('an invalid %s (%s) blocks submit', async (field, value) => {
      await setup({ portfolioId: savingsPortfolioId });
      await fill({ metal: METAL.Gold, name: 'Coin', fineWeight: 1, weightUnit: 'Gram' });

      await fill({ [field]: value });
      await component['onSubmit']();

      expect(findControl(form(), field).invalid).toBe(true);
      expect(writeRequests()).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });

  describe('edit', () => {
    it('pre-fills the holding, hides the first purchase and PUTs name, metal and weight only', async () => {
      const holding = metalResponse({
        name: 'Gold bar',
        metal: METAL.Gold,
        fineWeightGramsPerPiece: 100,
        pieces: 3,
        totalFineGrams: 300,
      });
      await setup({ metal: holding }, () => jsonResponse(holding));

      expect(findControl(form(), 'name').value).toBe('Gold bar');
      expect(findControl(form(), 'metal').value).toBe(METAL.Gold);
      expect(Number(findControl(form(), 'fineWeight').value)).toBe(100);
      expect(findControl(form(), 'weightUnit').value).toBe('Gram');
      for (const absent of ['portfolioId', 'pieces', 'pricePerPiece', 'purchaseDate']) {
        expect(hasControl(form(), absent)).toBe(false);
      }

      await fill({ name: 'Silver bar', metal: METAL.Silver });
      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('PUT');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${holding.portfolioId}/metals/${holding.assetId}`,
      );
      expect(await request.json()).toEqual({
        name: 'Silver bar',
        metal: METAL.Silver,
        fineWeight: 100,
        weightUnit: 'Gram',
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });
});
