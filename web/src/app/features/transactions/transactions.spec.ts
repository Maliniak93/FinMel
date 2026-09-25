import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type {
  AssetResponse,
  PagedResponseOfTransactionResponse,
  PortfolioResponse,
  TransactionResponse,
} from '../../api/portfolio';
import { Transactions } from './transactions';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const portfolioId = '22222222-2222-2222-2222-222222222222';
const assetId = '11111111-1111-1111-1111-111111111111';

const asset: AssetResponse = {
  id: assetId,
  portfolioId,
  assetClass: 2,
  valuationMode: 1, // Manual
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  manualValue: 1000,
  manualValueDate: '2020-01-01',
  transactionCount: 1,
};

const portfolio: PortfolioResponse = {
  id: portfolioId,
  name: 'Retirement',
  description: null,
  currency: 'PLN',
  isArchived: false,
  assetCount: 1,
};

const transaction: TransactionResponse = {
  id: '33333333-3333-3333-3333-333333333333',
  assetId,
  type: 0,
  quantity: 10,
  unitPrice: 100,
  currency: 'EUR',
  valuePln: 4300,
  date: '2024-01-15',
};

function pagedResponse(
  items: TransactionResponse[],
  totalCount = items.length,
): PagedResponseOfTransactionResponse {
  return { items, page: 1, pageSize: 20, totalCount };
}

function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
}

describe('Transactions', () => {
  let fixture: ComponentFixture<Transactions>;
  let component: Transactions;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  // The owning portfolio (GET /portfolios/{id}, no "/assets" in the URL) is what tells the page
  // whether it is archived (archived-portfolio-out-of-net-worth) — AssetResponse carries no flag.
  async function setup(
    transactionsResponse: Response,
    assetResponse = jsonResponse(asset),
    portfolioResponse = jsonResponse(portfolio),
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (url.includes('/transactions')) {
        return transactionsResponse;
      }
      return url.includes('/assets') ? assetResponse : portfolioResponse.clone();
    });
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Transactions],
      providers: [
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Transactions);
    fixture.componentRef.setInput('portfolioId', portfolioId);
    fixture.componentRef.setInput('assetId', assetId);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('should create', async () => {
    await setup(jsonResponse(pagedResponse([transaction])));
    expect(component).toBeTruthy();
  });

  it('loads the asset and its transactions into their resources', async () => {
    await setup(jsonResponse(pagedResponse([transaction])));

    expect(component['assetResource'].value()).toEqual(asset);
    expect(component['transactionsResource'].value()).toEqual(pagedResponse([transaction]));
  });

  it('reports an empty resource when the asset has no transactions', async () => {
    await setup(jsonResponse(pagedResponse([])));

    expect(component['transactionsResource'].hasValue()).toBe(true);
    expect(component['transactionsResource'].value()?.items).toEqual([]);
  });

  it('surfaces a transactions load failure through the resource error', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));

    expect(component['transactionsResource'].error()?.message).toBe('Service unavailable.');
  });

  it('surfaces an asset load failure through the resource error', async () => {
    await setup(
      jsonResponse(pagedResponse([transaction])),
      jsonResponse({ detail: 'Not found.' }, 404),
    );

    expect(component['assetResource'].error()?.message).toBe('Not found.');
  });

  it('reloads the transactions and the asset header after a successful create-dialog save', async () => {
    await setup(jsonResponse(pagedResponse([transaction])));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBe(callsBefore + 2);
  });

  it('does not reload when the create dialog is dismissed without saving', async () => {
    await setup(jsonResponse(pagedResponse([transaction])));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBe(callsBefore);
  });

  // cash-transaction-types AC-9: the dialog filters its types by the asset's class, so the page
  // hands over the loaded asset's class on both create and edit.
  it('passes the asset class to the transaction dialog', async () => {
    const cashAsset: AssetResponse = {
      ...asset,
      assetClass: 0, // Cash
      valuationMode: 2, // CurrencyValued
      name: 'Checking account',
      currency: 'PLN',
      manualValue: null,
      manualValueDate: null,
    };
    await setup(jsonResponse(pagedResponse([transaction])), jsonResponse(cashAsset));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    component['openCreateDialog']();
    component['openEditDialog'](transaction);

    expect(dialog.open).toHaveBeenCalledTimes(2);
    const [createCall, editCall] = dialog.open.mock.calls;
    expect(createCall[1].data).toEqual({ portfolioId, assetId, assetClass: 0 });
    expect(editCall[1].data).toEqual({ portfolioId, assetId, assetClass: 0, transaction });
  });

  it('deletes a transaction after confirmation and reloads', async () => {
    await setup(jsonResponse(pagedResponse([transaction])));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;
    fetchSpy.mockImplementationOnce(async () => new Response(null, { status: 204 }));

    await component['remove'](transaction);
    await fixture.whenStable();

    const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(deleteCall.url).toContain(transaction.id);
    expect(fetchSpy.mock.calls.length).toBe(callsBefore + 3);
  });

  it('does not delete when the confirmation is cancelled', async () => {
    await setup(jsonResponse(pagedResponse([transaction])));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const callsBefore = fetchSpy.mock.calls.length;

    await component['remove'](transaction);

    expect(fetchSpy.mock.calls.length).toBe(callsBefore);
  });

  it('shows a snackbar and does not reload when delete is rejected (breaks later history)', async () => {
    await setup(jsonResponse(pagedResponse([transaction])));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;
    fetchSpy.mockImplementationOnce(async () =>
      jsonResponse(
        {
          detail: 'This change would make a later Sell take the asset quantity below zero.',
          errorCode: 'Conflict.OversellsPosition',
        },
        409,
      ),
    );

    await component['remove'](transaction);

    expect(snackBar.open).toHaveBeenCalledWith(
      'This change would make a later Sell take the asset quantity below zero.',
      'Dismiss',
    );
    expect(fetchSpy.mock.calls.length).toBe(callsBefore + 1);
  });

  it('updates the page index and size on paginator events', async () => {
    await setup(jsonResponse(pagedResponse([transaction], 45)));
    const callsBefore = fetchSpy.mock.calls.length;

    component['onPage']({ pageIndex: 1, pageSize: 10, length: 45, previousPageIndex: 0 });
    await fixture.whenStable();

    expect(component['pageIndex']()).toBe(1);
    expect(component['pageSize']()).toBe(10);
    expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore);
  });

  // transactions-pln-value-and-fee-removal AC12: the table shows each transaction's own currency and
  // its server-computed PLN value (no unit price, no fee, no client-side money math); a transaction
  // whose rate was unknown shows an em dash instead of a value.
  it('renders currency and PLN value columns', async () => {
    const unpriced: TransactionResponse = {
      ...transaction,
      id: '44444444-4444-4444-4444-444444444444',
      date: '2019-01-02',
      valuePln: null,
    };
    await setup(jsonResponse(pagedResponse([transaction, unpriced])));
    fixture.detectChanges();
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    const headers = Array.from(element.querySelectorAll('thead th'), (th) =>
      (th.textContent ?? '').trim(),
    ).filter((text) => text.length > 0);
    expect(headers).toEqual(['Date', 'Type', 'Quantity', 'Currency', 'Value (PLN)']);
    expect(headers).not.toContain('Unit price');
    expect(headers).not.toContain('Fee');

    const rows = Array.from(element.querySelectorAll('tbody tr.mat-mdc-row'));
    expect(rows.length).toBe(2);
    // Whitespace is stripped because pl-PL separates the currency (and any digit groups) with
    // non-breaking spaces.
    const cellTexts = (row: Element) =>
      Array.from(row.querySelectorAll('td'), (td) => (td.textContent ?? '').replace(/\s/g, ''));

    const pricedCells = cellTexts(rows[0]);
    expect(pricedCells[3]).toBe('EUR');
    expect(pricedCells[4]).toBe('4300,00zł');

    const unpricedCells = cellTexts(rows[1]);
    expect(unpricedCells[3]).toBe('EUR');
    expect(unpricedCells[4]).toBe('—');
  });

  // archived-portfolio-out-of-net-worth AC12: every transaction write on an archived portfolio's
  // asset is a 409 on the backend, so the page offers none — no New transaction / Record your first
  // transaction, no Edit / Delete in any row menu — and shows the archived notice instead.
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

    // Opens every row menu the page renders (if any) and returns what the overlay offers.
    async function rowMenuText(): Promise<string> {
      const overlayContainer = TestBed.inject(OverlayContainer);
      for (const triggerElement of fixture.debugElement.queryAll(By.directive(MatMenuTrigger))) {
        triggerElement.injector.get(MatMenuTrigger).openMenu();
        fixture.detectChanges();
        await fixture.whenStable();
      }
      return overlayContainer.getContainerElement().textContent ?? '';
    }

    it('archived portfolio is read-only: no record / edit / delete actions, archived notice shown', async () => {
      await setup(
        jsonResponse(pagedResponse([transaction])),
        jsonResponse(asset),
        jsonResponse(archivedPortfolio),
      );

      // The history itself is still listed — archived is read-only, not hidden.
      expect(fixture.nativeElement.querySelectorAll('tbody tr.mat-mdc-row').length).toBe(1);
      expect(pageText()).toMatch(archivedNotice);
      expect(pageButtonTexts().some((text) => text.includes('New transaction'))).toBe(false);

      const menuText = await rowMenuText();
      expect(menuText).not.toContain('Edit');
      expect(menuText).not.toContain('Delete');
    });

    it('archived portfolio is read-only: the empty state offers no record button', async () => {
      await setup(
        jsonResponse(pagedResponse([])),
        jsonResponse(asset),
        jsonResponse(archivedPortfolio),
      );

      expect(pageText()).toMatch(archivedNotice);
      const buttons = pageButtonTexts();
      expect(buttons.some((text) => text.includes('New transaction'))).toBe(false);
      expect(buttons.some((text) => text.includes('Record your first transaction'))).toBe(false);
    });

    it('archived portfolio is read-only: an active portfolio keeps its actions and shows no notice', async () => {
      await setup(jsonResponse(pagedResponse([transaction])));

      expect(pageText()).not.toMatch(archivedNotice);
      expect(pageButtonTexts().some((text) => text.includes('New transaction'))).toBe(true);

      const menuText = await rowMenuText();
      expect(menuText).toContain('Edit');
      expect(menuText).toContain('Delete');
    });
  });
});
