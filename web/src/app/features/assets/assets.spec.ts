import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import {
  attributesOf,
  labelsOf,
  matchesTranslation,
  polishProblems,
  switchLanguage,
  textOf,
} from '../../../testing/i18n';
import {
  clickRowMenuItem,
  menuItemLabel,
  rowMenuItems,
  showArchived,
} from '../../../testing/archive';
import { client as marketDataClient } from '../../api/marketdata/client.gen';
import type { InstrumentDetailsResponse } from '../../api/marketdata';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AssetResponse, PortfolioResponse } from '../../api/portfolio';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatDate, formatMoney } from '../../shared/format';
import { toDateOnly } from '../../shared/date-only';
import { bondResponse } from '../../../testing/bond-fixtures';
import { metalResponse } from '../../../testing/metal-fixtures';
import { MetalFormDialog } from '../metals/metal-form-dialog/metal-form-dialog';
import { BondPurchaseDialog } from '../bonds/bond-purchase-dialog/bond-purchase-dialog';
import { DepositFormDialog } from '../deposits/deposit-form-dialog/deposit-form-dialog';
import { SavingsAccountFormDialog } from '../deposits/savings-account-form-dialog/savings-account-form-dialog';
import { savingsAccountResponse } from '../deposits/testing/savings-account-fixtures';
import { depositResponse } from '../deposits/testing/deposit-fixtures';
import { AssetFormDialog } from './asset-form/asset-form-dialog/asset-form-dialog';
import { ASSET_CLASS } from './asset-class';
import { VALUATION_MODE } from './asset-valuation-mode';
import { Assets } from './assets';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const portfolioId = '22222222-2222-2222-2222-222222222222';
const instrumentId = '33333333-3333-3333-3333-333333333333';

const portfolio: PortfolioResponse = {
  id: portfolioId,
  name: 'Retirement',
  description: null,
  currency: 'PLN',
  isArchived: false,
  assetCount: 1,
};

const asset: AssetResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  portfolioId,
  assetClass: 2,
  valuationMode: 1,
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  manualValue: 1000,
  manualValueDate: '2020-01-01',
  transactionCount: 0,
  isArchived: false,
};

const marketAsset: AssetResponse = {
  id: '44444444-4444-4444-4444-444444444444',
  portfolioId,
  assetClass: 2,
  valuationMode: 0,
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  instrumentId,
  transactionCount: 0,
  isArchived: false,
};

const currencyValuedAsset: AssetResponse = {
  id: '55555555-5555-5555-5555-555555555555',
  portfolioId,
  assetClass: 0,
  valuationMode: VALUATION_MODE.CurrencyValued,
  name: 'Cash',
  currency: 'PLN',
  quantity: 1000,
  manualValue: null,
  manualValueDate: null,
  transactionCount: 1,
  isArchived: false,
};

function depositAsset(maturityDate: string): AssetResponse {
  return {
    ...currencyValuedAsset,
    id: '99999999-9999-9999-9999-999999999999',
    assetClass: 1,
    name: 'Term deposit',
    quantity: 10000,
    depositMaturityDate: maturityDate,
  };
}

function daysFromToday(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return toDateOnly(date);
}

const currencyValuedEurAsset: AssetResponse = {
  ...currencyValuedAsset,
  id: '66666666-6666-6666-6666-666666666666',
  currency: 'EUR',
  quantity: 250,
};

function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
}

describe('Assets', () => {
  let fixture: ComponentFixture<Assets>;
  let component: Assets;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  });

  async function setup(
    assetsResponse: Response,
    portfolioResponse = jsonResponse(portfolio),
    instrumentResponse?: Response,
    depositResponseBody?: unknown,
    savingsAccountBody?: unknown,
    bondBody?: unknown,
    metalBody?: unknown,
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (url.includes('/instruments/')) {
        return instrumentResponse ?? jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (url.includes('/savings-accounts')) {
        return savingsAccountBody
          ? jsonResponse(savingsAccountBody)
          : jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (url.includes('/metals')) {
        return metalBody ? jsonResponse(metalBody) : jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (url.includes('/bonds')) {
        return bondBody ? jsonResponse(bondBody) : jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (url.includes('/deposits')) {
        return depositResponseBody
          ? jsonResponse(depositResponseBody)
          : jsonResponse({ detail: 'Not found.' }, 404);
      }
      return url.includes('/assets') ? assetsResponse : portfolioResponse;
    });
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Assets],
      providers: [
        provideRouter([]),
        provideI18nTesting(),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Assets);
    fixture.componentRef.setInput('portfolioId', portfolioId);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('should create', async () => {
    await setup(jsonResponse([asset]));
    expect(component).toBeTruthy();
  });

  it('loads the portfolio and its assets into their resources', async () => {
    await setup(jsonResponse([asset]));

    expect(component['portfolioResource'].value()).toEqual(portfolio);
    expect(component['assetsResource'].value()).toEqual([asset]);
  });

  it('reports an empty resource when the portfolio has no assets', async () => {
    await setup(jsonResponse([]));

    expect(component['assetsResource'].hasValue()).toBe(true);
    expect(component['assetsResource'].value()).toEqual([]);
  });

  it('surfaces an assets load failure through the resource error', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));

    expect(component['assetsResource'].error()?.message).toBe('Service unavailable.');
  });

  it('surfaces a portfolio load failure through the resource error', async () => {
    await setup(jsonResponse([asset]), jsonResponse({ detail: 'Not found.' }, 404));

    expect(component['portfolioResource'].error()?.message).toBe('Not found.');
  });

  it('reloads after a successful create-dialog save', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore);
  });

  it('opens the asset-form shell 560px wide for create and edit', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    component['openCreateDialog']();
    component['openEditDialog'](asset);

    expect(dialog.open).toHaveBeenNthCalledWith(
      1,
      AssetFormDialog,
      expect.objectContaining({ width: '560px', data: { portfolioId } }),
    );
    expect(dialog.open).toHaveBeenNthCalledWith(
      2,
      AssetFormDialog,
      expect.objectContaining({ width: '560px', data: { portfolioId, asset } }),
    );
  });

  it('does not reload when the create dialog is dismissed without saving', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBe(callsBefore);
  });

  it('deletes a transaction-less asset after confirmation and reloads', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;
    fetchSpy.mockImplementationOnce(async () => new Response(null, { status: 204 }));

    await component['remove'](asset);
    await fixture.whenStable();

    const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(deleteCall.url).toContain(asset.id);
    expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore + 1);
  });

  it('does not delete when the confirmation is cancelled', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const callsBefore = fetchSpy.mock.calls.length;

    await component['remove'](asset);

    expect(fetchSpy.mock.calls.length).toBe(callsBefore);
  });

  it('shows a snackbar and does not reload when delete fails on the server', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;
    fetchSpy.mockImplementationOnce(async () =>
      jsonResponse(
        {
          detail: 'Something went wrong while deleting the asset.',
          errorCode: 'Unexpected',
        },
        500,
      ),
    );

    await component['remove'](asset);

    expect(snackBar.open).toHaveBeenCalledWith(
      'Something went wrong while deleting the asset.',
      'Dismiss',
    );
    expect(fetchSpy.mock.calls.length).toBe(callsBefore + 1);
  });

  it('deletes an asset with transactions after a confirmation naming the transaction count', async () => {
    const withTransactions: AssetResponse = { ...asset, transactionCount: 3 };
    await setup(jsonResponse([withTransactions]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;
    fetchSpy.mockImplementationOnce(async () => new Response(null, { status: 204 }));

    const overlayContainer = TestBed.inject(OverlayContainer);
    const trigger = fixture.debugElement
      .query(By.directive(MatMenuTrigger))
      .injector.get(MatMenuTrigger);

    trigger.openMenu();
    fixture.detectChanges();
    await fixture.whenStable();

    const deleteButton = Array.from(
      overlayContainer.getContainerElement().querySelectorAll('button'),
    ).find((button) => (button.textContent ?? '').includes('Delete')) as
      HTMLButtonElement | undefined;
    expect(deleteButton?.disabled).toBe(false);
    expect(deleteButton?.classList.contains('mat-mdc-tooltip-trigger')).toBe(false);

    deleteButton!.click();
    await vi.waitFor(() => expect(dialog.open).toHaveBeenCalled());

    const message = (dialog.open.mock.calls[0][1] as { data: { message: string } }).data.message;
    expect(message).toMatch(
      /^"Apple" and its 3 transactions?(\(s\))? will be permanently deleted\. This can't be undone\.$/,
    );

    await vi.waitFor(() => expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore + 1));
    const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(deleteCall.url).toContain(asset.id);
  });

  it("shows a market asset's last price and date", async () => {
    const instrument: InstrumentDetailsResponse = {
      id: instrumentId,
      ticker: 'AAPL.US',
      name: 'Apple Inc.',
      assetClass: 2,
      quoteCurrency: 'USD',
      source: 1,
      verificationStatus: 0,
      lastPrice: 212,
      lastPriceDate: new Date().toISOString().slice(0, 10),
    };
    await setup(jsonResponse([marketAsset]), undefined, jsonResponse(instrument));
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(
      new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(2120),
    );
    expect(text).not.toContain('Stale');
  });

  it('flags a market asset as stale when its last price is older than 7 days', async () => {
    const staleDate = new Date();
    staleDate.setDate(staleDate.getDate() - 10);
    const instrument: InstrumentDetailsResponse = {
      id: instrumentId,
      ticker: 'AAPL.US',
      name: 'Apple Inc.',
      assetClass: 2,
      quoteCurrency: 'USD',
      source: 1,
      verificationStatus: 0,
      lastPrice: 212,
      lastPriceDate: staleDate.toISOString().slice(0, 10),
    };
    await setup(jsonResponse([marketAsset]), undefined, jsonResponse(instrument));
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Stale');
  });

  it("shows 'No price yet' for a market asset with no quotes", async () => {
    const instrument: InstrumentDetailsResponse = {
      id: instrumentId,
      ticker: 'NEW.US',
      name: 'Brand New Co.',
      assetClass: 2,
      quoteCurrency: 'USD',
      source: 1,
      verificationStatus: 1,
      lastPrice: null,
      lastPriceDate: null,
    };
    await setup(jsonResponse([marketAsset]), undefined, jsonResponse(instrument));
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No price yet');
  });

  it("shows a currency-valued asset's quantity as its value", async () => {
    await setup(jsonResponse([currencyValuedAsset]));

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(formatMoney(1000, 'PLN'));
    expect(text).not.toContain(
      new Intl.NumberFormat('en-US', { style: 'currency', currency: 'PLN' }).format(0),
    );
  });

  it('does not flag a currency-valued asset as stale', async () => {
    await setup(jsonResponse([currencyValuedAsset]));

    const cell = (fixture.nativeElement as HTMLElement).querySelector(
      'td.mat-column-manualValueDate',
    );
    expect(cell?.textContent).toContain('—');
    expect(cell?.textContent).not.toContain('Stale');
  });

  it('shows a non-PLN currency-valued asset in its own currency', async () => {
    await setup(jsonResponse([currencyValuedEurAsset]));

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(formatMoney(250, 'EUR'));
  });

  it('flags only a manual asset with an old valuation date as stale', async () => {
    const staleDate = new Date();
    staleDate.setMonth(staleDate.getMonth() - 7);
    const freshDate = new Date();
    freshDate.setDate(freshDate.getDate() - 7);

    const staleManualAsset: AssetResponse = {
      ...asset,
      id: '77777777-7777-7777-7777-777777777777',
      manualValueDate: staleDate.toISOString().slice(0, 10),
    };
    const freshManualAsset: AssetResponse = {
      ...asset,
      id: '88888888-8888-8888-8888-888888888888',
      name: 'Fresh',
      manualValueDate: freshDate.toISOString().slice(0, 10),
    };

    await setup(jsonResponse([staleManualAsset, freshManualAsset]));

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr.mat-mdc-row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Stale — refresh me');
    expect(rows[1].textContent).not.toContain('Stale');
  });

  describe('archived portfolio is read-only', () => {
    const archivedPortfolio: PortfolioResponse = { ...portfolio, isArchived: true };
    const archivedNotice = /This portfolio is archived\W+restore it to make changes/;

    function pageText(): string {
      return (fixture.nativeElement as HTMLElement).textContent ?? '';
    }

    function pageButtonTexts(): string[] {
      return Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
        (button) => button.textContent?.trim() ?? '',
      );
    }

    async function rowMenuText(): Promise<string> {
      const overlayContainer = TestBed.inject(OverlayContainer);
      for (const triggerElement of fixture.debugElement.queryAll(By.directive(MatMenuTrigger))) {
        triggerElement.injector.get(MatMenuTrigger).openMenu();
        fixture.detectChanges();
        await fixture.whenStable();
      }
      return overlayContainer.getContainerElement().textContent ?? '';
    }

    it('archived portfolio is read-only: no add / edit / remove actions, archived notice shown', async () => {
      await setup(jsonResponse([asset, currencyValuedAsset]), jsonResponse(archivedPortfolio));

      expect(pageText()).toContain('Apple');
      expect(pageText()).toMatch(archivedNotice);
      expect(pageButtonTexts().some((text) => text.includes('New asset'))).toBe(false);

      const menuText = await rowMenuText();
      expect(menuText).not.toContain('Edit');
      expect(menuText).not.toContain('Delete');
    });

    it('archived portfolio is read-only: the empty state offers no add button', async () => {
      await setup(jsonResponse([]), jsonResponse(archivedPortfolio));

      expect(pageText()).toMatch(archivedNotice);
      const buttons = pageButtonTexts();
      expect(buttons.some((text) => text.includes('New asset'))).toBe(false);
      expect(buttons.some((text) => text.includes('Add your first asset'))).toBe(false);
    });

    it('archived portfolio is read-only: an active portfolio keeps its actions and shows no notice', async () => {
      await setup(jsonResponse([asset]));

      expect(pageText()).not.toMatch(archivedNotice);
      expect(pageButtonTexts().some((text) => text.includes('New asset'))).toBe(true);

      const menuText = await rowMenuText();
      expect(menuText).toContain('Edit');
      expect(menuText).toContain('Delete');
    });
  });

  describe('archive', () => {
    const archivedCash = {
      ...currencyValuedAsset,
      name: 'Old cash',
      isArchived: true,
    } as AssetResponse;
    const archivedDueDepositAsset = {
      ...depositAsset(daysFromToday(-1)),
      name: 'Shelved matured deposit',
      isArchived: true,
    } as AssetResponse;

    function rows(): HTMLElement[] {
      return Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          'tbody tr.mat-mdc-row',
        ),
      );
    }

    function rowFor(name: string): HTMLElement {
      const row = rows().find((r) => (r.textContent ?? '').includes(name));
      if (!row) {
        throw new Error(`No row for '${name}'.`);
      }
      return row;
    }

    function writes(): Request[] {
      return fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method !== 'GET');
    }

    async function menuLabels(row: HTMLElement): Promise<string[]> {
      const items = await rowMenuItems(fixture, row);
      const labels = items.map(menuItemLabel);
      TestBed.inject(OverlayContainer).getContainerElement().replaceChildren();
      return labels;
    }

    it('hides archived assets until Show archived is on', async () => {
      await setup(jsonResponse([asset, archivedCash]));

      expect(rows()).toHaveLength(1);
      expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Old cash');

      await showArchived(fixture);

      expect(rows()).toHaveLength(2);
      const archivedRow = rowFor('Old cash');
      expect(archivedRow.querySelector('mat-chip, .mat-mdc-chip')?.textContent).toContain(
        'Archived',
      );
      expect(rowFor('Apple').textContent).not.toContain('Archived');
    });

    it('an archived row has no Edit and no Due chip but keeps Restore and Delete', async () => {
      await setup(jsonResponse([archivedCash, archivedDueDepositAsset]));
      await showArchived(fixture);

      expect(rowFor('Shelved matured deposit').textContent).not.toContain('Due');
      for (const name of ['Old cash', 'Shelved matured deposit']) {
        const labels = await menuLabels(rowFor(name));

        expect(labels.some((label) => /restore/i.test(label))).toBe(true);
        expect(labels.some((label) => /delete/i.test(label))).toBe(true);
        expect(labels.some((label) => /edit/i.test(label))).toBe(false);
        expect(labels.some((label) => /\barchive\b/i.test(label))).toBe(false);
      }
    });

    it('a live row offers Archive, Edit and Delete but not Restore', async () => {
      await setup(jsonResponse([asset]));

      const labels = await menuLabels(rowFor('Apple'));

      expect(labels.some((label) => /\barchive\b/i.test(label))).toBe(true);
      expect(labels.some((label) => /edit/i.test(label))).toBe(true);
      expect(labels.some((label) => /delete/i.test(label))).toBe(true);
      expect(labels.some((label) => /restore/i.test(label))).toBe(false);
    });

    it('archives and restores an asset after confirmation', async () => {
      await setup(jsonResponse([asset, archivedCash]));
      await showArchived(fixture);
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      fetchSpy.mockImplementation(async (input: unknown) =>
        requestUrl(input).includes('/assets')
          ? jsonResponse([asset, archivedCash])
          : jsonResponse(portfolio),
      );

      let callsBefore = fetchSpy.mock.calls.length;
      fetchSpy.mockImplementationOnce(async () => jsonResponse({ ...asset, isArchived: true }));
      await clickRowMenuItem(fixture, rowFor('Apple'), /\barchive\b/i);

      expect(dialog.open).toHaveBeenCalledTimes(1);
      await vi.waitFor(() => expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore + 1));
      const archiveCall = fetchSpy.mock.calls[callsBefore][0] as Request;
      expect(archiveCall.method).toBe('POST');
      expect(archiveCall.url).toContain(`/portfolios/${portfolioId}/assets/${asset.id}/archive`);
      expect((fetchSpy.mock.calls[callsBefore + 1][0] as Request).method).toBe('GET');

      callsBefore = fetchSpy.mock.calls.length;
      fetchSpy.mockImplementationOnce(async () =>
        jsonResponse({ ...archivedCash, isArchived: false }),
      );
      await clickRowMenuItem(fixture, rowFor('Old cash'), /restore/i);

      expect(dialog.open).toHaveBeenCalledTimes(2);
      await vi.waitFor(() => expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore + 1));
      const restoreCall = fetchSpy.mock.calls[callsBefore][0] as Request;
      expect(restoreCall.method).toBe('POST');
      expect(restoreCall.url).toContain(
        `/portfolios/${portfolioId}/assets/${archivedCash.id}/restore`,
      );
      expect((fetchSpy.mock.calls[callsBefore + 1][0] as Request).method).toBe('GET');
    });

    it('does not archive or restore when the confirmation is cancelled', async () => {
      await setup(jsonResponse([asset, archivedCash]));
      await showArchived(fixture);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });

      await clickRowMenuItem(fixture, rowFor('Apple'), /\barchive\b/i);
      await clickRowMenuItem(fixture, rowFor('Old cash'), /restore/i);

      expect(dialog.open).toHaveBeenCalledTimes(2);
      expect(writes()).toHaveLength(0);
    });

    it('an archived portfolio still offers no actions, whatever the asset flags', async () => {
      await setup(
        jsonResponse([asset, archivedCash]),
        jsonResponse({ ...portfolio, isArchived: true }),
      );

      expect((fixture.nativeElement as HTMLElement).querySelectorAll('td button')).toHaveLength(0);
    });
  });

  it('Edit on a Deposit row opens DepositFormDialog, not the asset form', async () => {
    const deposit = depositAsset('2027-01-01');
    const terms = depositResponse({ assetId: deposit.id, portfolioId, maturityDate: '2027-01-01' });
    await setup(jsonResponse([deposit]), undefined, undefined, terms);
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openEditDialog'](deposit);

    await vi.waitFor(() => expect(dialog.open).toHaveBeenCalled());
    const [dialogType, config] = dialog.open.mock.calls[0] as [unknown, { data?: unknown }];
    expect(dialogType).toBe(DepositFormDialog);
    expect(dialogType).not.toBe(AssetFormDialog);
    expect(JSON.stringify(config?.data)).toContain(deposit.id);
    await vi.waitFor(() =>
      expect(
        fetchSpy.mock.calls
          .slice(callsBefore)
          .some((call: unknown[]) =>
            requestUrl(call[0]).endsWith(`/portfolios/${portfolioId}/assets`),
          ),
      ).toBe(true),
    );
  });

  it('Edit on a Savings row opens SavingsAccountFormDialog, not the asset form', async () => {
    const savings: AssetResponse = {
      ...currencyValuedAsset,
      id: '88888888-aaaa-8888-aaaa-888888888888',
      assetClass: 9,
      name: 'Savings account',
      quantity: 10000,
    };
    const terms = savingsAccountResponse({ assetId: savings.id, portfolioId });
    await setup(jsonResponse([savings]), undefined, undefined, undefined, terms);
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openEditDialog'](savings);

    await vi.waitFor(() => expect(dialog.open).toHaveBeenCalled());
    const [dialogType, config] = dialog.open.mock.calls[0] as [unknown, { data?: unknown }];
    expect(dialogType).toBe(SavingsAccountFormDialog);
    expect(dialogType).not.toBe(AssetFormDialog);
    expect(JSON.stringify(config?.data)).toContain(savings.id);
    await vi.waitFor(() =>
      expect(
        fetchSpy.mock.calls
          .slice(callsBefore)
          .some((call: unknown[]) =>
            requestUrl(call[0]).endsWith(`/portfolios/${portfolioId}/assets`),
          ),
      ).toBe(true),
    );
  });

  it('Edit on a PreciousMetal row opens MetalFormDialog, not the asset form', async () => {
    const metalAsset: AssetResponse = {
      ...marketAsset,
      id: '88888888-cccc-8888-cccc-888888888888',
      assetClass: ASSET_CLASS.PreciousMetal,
      name: 'Krugerrand',
      currency: 'PLN',
      quantity: 2,
    };
    const holding = metalResponse({ assetId: metalAsset.id, portfolioId });
    await setup(
      jsonResponse([metalAsset]),
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      holding,
    );
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });

    component['openEditDialog'](metalAsset);

    await vi.waitFor(() => expect(dialog.open).toHaveBeenCalled());
    const [dialogType, config] = dialog.open.mock.calls[0] as [unknown, { data?: unknown }];
    expect(dialogType).toBe(MetalFormDialog);
    expect(dialogType).not.toBe(AssetFormDialog);
    expect(JSON.stringify(config?.data)).toContain(metalAsset.id);
  });

  it("multiplies a metal asset's market value by its fine weight per piece", async () => {
    const instrument: InstrumentDetailsResponse = {
      id: instrumentId,
      ticker: 'XAU',
      name: 'Gold (1 g)',
      assetClass: 6,
      quoteCurrency: 'PLN',
      source: 1,
      verificationStatus: 0,
      lastPrice: 500,
      lastPriceDate: new Date().toISOString().slice(0, 10),
    };
    const metalAsset: AssetResponse = {
      ...marketAsset,
      assetClass: ASSET_CLASS.PreciousMetal,
      currency: 'PLN',
      quantity: 2,
      fineWeightGramsPerPiece: 31.1,
    };
    await setup(jsonResponse([metalAsset]), undefined, jsonResponse(instrument));
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(formatMoney(31100, 'PLN'));
  });

  it('Edit on a Bond row opens BondPurchaseDialog, not the asset form', async () => {
    const bond: AssetResponse = {
      ...currencyValuedAsset,
      id: '88888888-bbbb-8888-bbbb-888888888888',
      assetClass: ASSET_CLASS.Bond,
      name: 'EDO1036',
      quantity: 5000,
    };
    const terms = bondResponse({ assetId: bond.id, portfolioId });
    await setup(jsonResponse([bond]), undefined, undefined, undefined, undefined, terms);
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openEditDialog'](bond);

    await vi.waitFor(() => expect(dialog.open).toHaveBeenCalled());
    const [dialogType, config] = dialog.open.mock.calls[0] as [unknown, { data?: unknown }];
    expect(dialogType).toBe(BondPurchaseDialog);
    expect(dialogType).not.toBe(AssetFormDialog);
    expect(JSON.stringify(config?.data)).toContain(bond.id);
    await vi.waitFor(() =>
      expect(
        fetchSpy.mock.calls
          .slice(callsBefore)
          .some((call: unknown[]) =>
            requestUrl(call[0]).endsWith(`/portfolios/${portfolioId}/assets`),
          ),
      ).toBe(true),
    );
  });

  it('Edit on a non-Deposit row still opens the asset form', async () => {
    await setup(jsonResponse([currencyValuedAsset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    component['openEditDialog'](currencyValuedAsset);

    expect(dialog.open).toHaveBeenCalledWith(AssetFormDialog, expect.anything());
  });

  it('a past-maturity Deposit row shows the "Due" chip', async () => {
    const matured = { ...depositAsset(daysFromToday(-1)), name: 'Matured deposit' };
    const running = {
      ...depositAsset(daysFromToday(30)),
      id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      name: 'Running deposit',
    };
    await setup(jsonResponse([matured, running]));

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr.mat-mdc-row'),
    );
    const maturedRow = rows.find((row) => (row.textContent ?? '').includes('Matured deposit'));
    const runningRow = rows.find((row) => (row.textContent ?? '').includes('Running deposit'));
    const chip = maturedRow?.querySelector('mat-chip, mat-chip-option, .mat-mdc-chip');
    expect(chip?.textContent).toContain('Due');
    expect(runningRow?.textContent).not.toContain('Due');
  });

  it('a settled Deposit row shows no "Due" chip', async () => {
    const matured = { ...depositAsset(daysFromToday(-1)), name: 'Matured deposit' };
    const settled = {
      ...depositAsset(daysFromToday(-1)),
      id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      name: 'Settled deposit',
      quantity: 10121.5,
      depositSettled: true,
    };
    await setup(jsonResponse([matured, settled]));

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr.mat-mdc-row'),
    );
    const maturedRow = rows.find((row) => (row.textContent ?? '').includes('Matured deposit'));
    const settledRow = rows.find((row) => (row.textContent ?? '').includes('Settled deposit'));
    expect(maturedRow?.textContent).toContain('Due');
    expect(settledRow).toBeDefined();
    expect(settledRow?.textContent).not.toContain('Due');
  });

  it('a savings account with interest to settle shows the "Due" chip', async () => {
    const savings = (overrides: Partial<AssetResponse>): AssetResponse =>
      ({
        ...currencyValuedAsset,
        assetClass: 9,
        quantity: 10000,
        ...overrides,
      }) as AssetResponse;
    const due = savings({
      id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
      name: 'Ripe savings',
      savingsInterestDue: true,
    } as Partial<AssetResponse>);
    const settledUp = savings({
      id: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
      name: 'Settled savings',
      savingsInterestDue: false,
    } as Partial<AssetResponse>);
    const cash = { ...currencyValuedAsset, name: 'Plain cash' };
    await setup(jsonResponse([due, settledUp, cash]));

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'),
    );
    const rowFor = (name: string) => rows.find((row) => (row.textContent ?? '').includes(name));
    const dueRow = rowFor('Ripe savings');
    expect(dueRow?.textContent).toContain('Due');
    expect(rowFor('Settled savings')?.textContent).not.toContain('Due');
    expect(rowFor('Plain cash')?.textContent).not.toContain('Due');

    const chip = fixture.debugElement
      .queryAll(By.css('.assets-page__chip--due'))
      .find((debugElement) => dueRow?.contains(debugElement.nativeElement));
    expect(chip?.injector.get(MatTooltip).message).toBe('Interest to settle');
  });

  it('reformats values when the language changes', async () => {
    const valuedAsset: AssetResponse = {
      ...asset,
      quantity: 12345.5,
      manualValue: 1234.56,
      manualValueDate: '2026-09-27',
    };
    await setup(jsonResponse([valuedAsset]));
    const element = fixture.nativeElement as HTMLElement;
    const cell = (column: string) => textOf(element.querySelector(`td.mat-column-${column}`));
    const money = (locale: string) =>
      new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD' })
        .format(1234.56)
        .replace(/\s+/g, ' ');
    const quantity = (locale: string) =>
      new Intl.NumberFormat(locale, { maximumFractionDigits: 8 })
        .format(12345.5)
        .replace(/\s+/g, ' ');

    expect(cell('value')).toContain(money('en-US'));
    expect(cell('quantity')).toContain(quantity('en-US'));
    expect(cell('manualValueDate')).toContain('Sep 27, 2026');
    const fetchesBefore = fetchSpy.mock.calls.length;

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    expect(cell('value')).toContain(money('pl-PL'));
    expect(cell('quantity')).toContain(quantity('pl-PL'));
    expect(cell('manualValueDate')).toContain('27 wrz 2026');
    expect(cell('manualValueDate')).not.toContain('Sep 27, 2026');
    expect(fetchSpy.mock.calls.length).toBe(fetchesBefore);
  });

  describe('in Polish', () => {
    const maturity = daysFromToday(-1);

    function tooltipOf(selector: string): string {
      return fixture.debugElement.query(By.css(selector)).injector.get(MatTooltip).message;
    }

    async function menuItems(): Promise<string[]> {
      const trigger = fixture.debugElement
        .query(By.directive(MatMenuTrigger))
        .injector.get(MatMenuTrigger);
      trigger.openMenu();
      fixture.detectChanges();
      await fixture.whenStable();
      const items = labelsOf(
        TestBed.inject(OverlayContainer).getContainerElement(),
        '.mat-mdc-menu-item',
      );
      trigger.closeMenu();
      fixture.detectChanges();
      await fixture.whenStable();
      return items;
    }

    it('renders in Polish', async () => {
      const deposit = { ...depositAsset(maturity), name: 'Matured deposit' };
      await setup(jsonResponse([asset, deposit]));
      const element = fixture.nativeElement as HTMLElement;
      const texts = async () => [
        ...labelsOf(element, 'a.assets-page__back'),
        ...labelsOf(element, '.assets-page__header > button'),
        ...labelsOf(element, 'th:not(:empty)'),
        ...labelsOf(element, 'mat-chip'),
        tooltipOf('.mat-column-manualValueDate mat-chip'),
        ...(await menuItems()),
      ];
      const heading = () => textOf(element.querySelector('h1'));
      const maturedTooltip = () => tooltipOf('.assets-page__chip--due');

      const english = await texts();
      expect(english).toEqual([
        'Portfolios',
        'New asset',
        'Class',
        'Name',
        'Quantity',
        'Currency',
        'Value',
        'Valued on',
        'Stale — refresh me',
        'Due',
        'This valuation is more than 6 months old — consider refreshing it.',
        'Edit',
        'Archive',
        'Delete',
      ]);
      expect(heading()).toBe('Assets — Retirement');
      expect(maturedTooltip()).toMatch(/^Matured on /);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, await texts())).toEqual([]);
      expect(heading()).not.toContain('Assets');
      expect(heading()).toContain('Retirement');
      expect(maturedTooltip()).not.toMatch(/^Matured on /);
      expect(maturedTooltip()).toContain(formatDate(maturity));
      expect(
        matchesTranslation('pl', maturedTooltip()),
        `"${maturedTooltip()}" is not a pl.json value`,
      ).toBe(true);
    });

    it('labels the row actions button with the asset name in Polish', async () => {
      await setup(jsonResponse([asset]));
      const element = fixture.nativeElement as HTMLElement;

      expect(attributesOf(element, 'td button', 'aria-label')).toEqual(['Actions for Apple']);

      await switchLanguage(fixture, 'pl');

      const [label] = attributesOf(element, 'td button', 'aria-label');
      expect(label).not.toContain('Actions for');
      expect(label).toContain('Apple');
      expect(matchesTranslation('pl', label), `"${label}" is not a pl.json value`).toBe(true);
    });

    it("renders a market asset's price cells in Polish", async () => {
      const staleDate = new Date();
      staleDate.setDate(staleDate.getDate() - 10);
      const staleInstrument: InstrumentDetailsResponse = {
        id: instrumentId,
        ticker: 'AAPL.US',
        name: 'Apple Inc.',
        assetClass: 2,
        quoteCurrency: 'USD',
        source: 1,
        verificationStatus: 0,
        lastPrice: 212,
        lastPriceDate: staleDate.toISOString().slice(0, 10),
      };
      await setup(jsonResponse([marketAsset]), undefined, jsonResponse(staleInstrument));
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'mat-chip'),
        tooltipOf('.mat-column-manualValueDate mat-chip'),
      ];

      const english = texts();
      expect(english).toEqual(['Stale', "This instrument's price is more than 7 days old."]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it("renders 'No price yet' in Polish", async () => {
      const noQuotes: InstrumentDetailsResponse = {
        id: instrumentId,
        ticker: 'NEW.US',
        name: 'Brand New Co.',
        assetClass: 2,
        quoteCurrency: 'USD',
        source: 1,
        verificationStatus: 1,
        lastPrice: null,
        lastPriceDate: null,
      };
      await setup(jsonResponse([marketAsset]), undefined, jsonResponse(noQuotes));
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      const cell = () => [textOf(element.querySelector('td.mat-column-value'))];

      expect(cell()).toEqual(['No price yet']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(['No price yet'], cell())).toEqual([]);
    });

    it('renders the empty state in Polish', async () => {
      await setup(jsonResponse([]));
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, '.assets-page__state p'),
        ...labelsOf(element, '.assets-page__state button'),
      ];

      const english = texts();
      expect(english).toEqual([
        "This portfolio doesn't have any assets yet.",
        'Add your first asset',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the archived notice in Polish', async () => {
      await setup(jsonResponse([asset]), jsonResponse({ ...portfolio, isArchived: true }));
      const element = fixture.nativeElement as HTMLElement;
      const notice = () => labelsOf(element, '.assets-page__archived-notice');

      const english = notice();
      expect(english).toHaveLength(1);
      expect(english[0]).toMatch(/^This portfolio is archived/);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, notice())).toEqual([]);
    });

    it('renders the load-failure retry button in Polish', async () => {
      await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));
      const element = fixture.nativeElement as HTMLElement;

      expect(labelsOf(element, '.assets-page__state button')).toEqual(['Retry']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(['Retry'], labelsOf(element, '.assets-page__state button'))).toEqual(
        [],
      );
      expect(textOf(element.querySelector('.assets-page__state p'))).toBe('Service unavailable.');
    });

    it('asks to delete in Polish, with the asset name and transaction count in the message', async () => {
      const withTransactions: AssetResponse = { ...asset, transactionCount: 3 };
      await setup(jsonResponse([withTransactions]));
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      const confirmation = () =>
        (
          dialog.open.mock.calls[0][1] as {
            data: { title: string; message: string; confirmLabel: string };
          }
        ).data;
      await component['remove'](withTransactions);
      const english = confirmation();
      dialog.open.mockClear();

      await switchLanguage(fixture, 'pl');
      await component['remove'](withTransactions);
      const polish = confirmation();

      expect(english.title).toBe('Delete this asset?');
      expect(
        polishProblems([english.title, english.confirmLabel], [polish.title, polish.confirmLabel]),
      ).toEqual([]);
      expect(polish.message).toContain('"Apple"');
      expect(polish.message).toContain('3');
      expect(polish.message).not.toContain('permanently deleted');
    });

    it('shows the failure snackbar fallback in Polish', async () => {
      await setup(jsonResponse([asset]));
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      fetchSpy.mockImplementationOnce(async () => jsonResponse({ title: 'Boom' }, 500));

      await switchLanguage(fixture, 'pl');
      await component['remove'](asset);

      expect(snackBar.open).toHaveBeenCalledTimes(1);
      const [message, action] = snackBar.open.mock.calls[0] as [string, string];
      expect(polishProblems(['Failed to delete asset.', 'Dismiss'], [message, action])).toEqual([]);
    });
  });
});
