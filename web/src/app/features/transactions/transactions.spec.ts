import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import {
  attributesOf,
  labelsOf,
  matchesTranslation,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  textOf,
  TRANSLATIONS,
} from '../../../testing/i18n';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type {
  AssetResponse,
  PagedResponseOfTransactionResponse,
  PortfolioResponse,
  TransactionResponse,
} from '../../api/portfolio';
import { Transactions } from './transactions';
import { provideI18nTesting } from '../../core/i18n/testing';

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
  isArchived: false,
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

  afterEach(async () => {
    fetchSpy.mockRestore();
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await restoreEnglish();
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
        provideI18nTesting(),
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
    // Whitespace is stripped because Intl separates the currency code from the amount with a
    // non-breaking space.
    const cellTexts = (row: Element) =>
      Array.from(row.querySelectorAll('td'), (td) => (td.textContent ?? '').replace(/\s/g, ''));

    const pricedCells = cellTexts(rows[0]);
    expect(pricedCells[3]).toBe('EUR');
    expect(pricedCells[4]).toBe('PLN4,300.00');

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

  // asset-archive AC-11: an archived asset's transactions are a 409 on the backend, so the view is
  // read-only as for an archived portfolio — no record button, no Edit / Delete — with its own notice.
  describe('archived asset is read-only', () => {
    const archivedAsset = { ...asset, isArchived: true } as AssetResponse;
    const assetNotice = /This asset is archived\W+restore it to make changes/;

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

    it('is read-only for an archived asset', async () => {
      await setup(jsonResponse(pagedResponse([transaction])), jsonResponse(archivedAsset));

      // The history itself is still listed — archived is read-only, not hidden.
      expect(fixture.nativeElement.querySelectorAll('tbody tr.mat-mdc-row').length).toBe(1);
      expect(pageText()).toMatch(assetNotice);
      expect(pageText()).not.toMatch(/This portfolio is archived/);
      expect(pageButtonTexts().some((text) => text.includes('New transaction'))).toBe(false);

      const menuText = await rowMenuText();
      expect(menuText).not.toContain('Edit');
      expect(menuText).not.toContain('Delete');
    });

    it('is read-only for an archived asset: the empty state offers no record button', async () => {
      await setup(jsonResponse(pagedResponse([])), jsonResponse(archivedAsset));

      expect(pageText()).toMatch(assetNotice);
      const buttons = pageButtonTexts();
      expect(buttons.some((text) => text.includes('New transaction'))).toBe(false);
      expect(buttons.some((text) => text.includes('Record your first transaction'))).toBe(false);
    });

    it('a live asset keeps its actions and shows no asset notice', async () => {
      await setup(jsonResponse(pagedResponse([transaction])));

      expect(pageText()).not.toMatch(assetNotice);
      expect(pageButtonTexts().some((text) => text.includes('New transaction'))).toBe(true);
    });
  });

  // asset-transfers-deposit-funding AC-12: a transfer leg (`transfer` set on the TransactionResponse)
  // is labelled beside its type — "Transfer to <asset> (<portfolio>)" on the Out leg, "Transfer from
  // …" on the In leg — and offers no Edit/Delete (the backend answers 409
  // Conflict.TransferLegManaged), while a plain transaction on the same Cash asset keeps both.
  describe('transfer legs', () => {
    // Backend enum Skarbiec.Portfolio.Features.Transfers.TransferDirection arrives as an int, in C#
    // declaration order.
    const TRANSFER_DIRECTION = { Out: 0, In: 1 } as const;

    const cashAsset: AssetResponse = {
      ...asset,
      assetClass: 0, // Cash
      valuationMode: 2, // CurrencyValued
      name: 'Cash account',
      currency: 'PLN',
      quantity: 4000,
      manualValue: null,
      manualValueDate: null,
    };
    const plainTopUp: TransactionResponse = {
      ...transaction,
      id: '55555555-5555-5555-5555-555555555555',
      type: 2, // Deposit
      quantity: 5000,
      unitPrice: 1,
      currency: 'PLN',
      valuePln: 5000,
      date: '2026-01-01',
    };
    const outLeg = {
      ...transaction,
      id: '66666666-6666-6666-6666-666666666666',
      type: 3, // Withdraw
      quantity: 1000,
      unitPrice: 1,
      currency: 'PLN',
      valuePln: 1000,
      date: '2026-01-15',
      transfer: {
        counterpartAssetId: '77777777-7777-7777-7777-777777777777',
        counterpartAssetName: 'Term deposit',
        counterpartPortfolioId: '88888888-8888-8888-8888-888888888888',
        counterpartPortfolioName: 'Savings',
        direction: TRANSFER_DIRECTION.Out,
      },
    } as TransactionResponse;
    const inLeg = {
      ...outLeg,
      id: '99999999-9999-9999-9999-999999999999',
      type: 2, // Deposit
      transfer: {
        counterpartAssetId: assetId,
        counterpartAssetName: 'Cash account',
        counterpartPortfolioId: portfolioId,
        counterpartPortfolioName: 'Wallet',
        direction: TRANSFER_DIRECTION.In,
      },
    } as TransactionResponse;

    function rows(): HTMLElement[] {
      return Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          'tbody tr.mat-mdc-row',
        ),
      );
    }

    function rowText(row: HTMLElement): string {
      return (row.textContent ?? '').replace(/\s+/g, ' ');
    }

    async function openRowMenu(row: HTMLElement): Promise<string> {
      const triggerElement = fixture.debugElement
        .queryAll(By.directive(MatMenuTrigger))
        .find((debugElement) => row.contains(debugElement.nativeElement));
      if (!triggerElement) {
        throw new Error('The row has no actions menu.');
      }
      triggerElement.injector.get(MatMenuTrigger).openMenu();
      fixture.detectChanges();
      await fixture.whenStable();
      return TestBed.inject(OverlayContainer).getContainerElement().textContent ?? '';
    }

    function rowHasActions(row: HTMLElement): boolean {
      return fixture.debugElement
        .queryAll(By.directive(MatMenuTrigger))
        .some((debugElement) => row.contains(debugElement.nativeElement));
    }

    it('labels the Out leg and offers it no edit / delete, while the plain row keeps both', async () => {
      await setup(jsonResponse(pagedResponse([outLeg, plainTopUp])), jsonResponse(cashAsset));
      fixture.detectChanges();
      await fixture.whenStable();

      const [legRow, plainRow] = rows();
      expect(rowText(legRow)).toContain('Transfer to Term deposit (Savings)');
      expect(rowHasActions(legRow)).toBe(false);

      expect(rowText(plainRow)).not.toContain('Transfer');
      expect(rowHasActions(plainRow)).toBe(true);
      const menuText = await openRowMenu(plainRow);
      expect(menuText).toContain('Edit');
      expect(menuText).toContain('Delete');
    });

    it('labels the In leg "Transfer from <asset> (<portfolio>)"', async () => {
      await setup(jsonResponse(pagedResponse([inLeg, plainTopUp])), jsonResponse(cashAsset));
      fixture.detectChanges();
      await fixture.whenStable();

      const [legRow] = rows();
      expect(rowText(legRow)).toContain('Transfer from Cash account (Wallet)');
      expect(rowHasActions(legRow)).toBe(false);
    });
  });

  // term-deposits AC-16: a term deposit's transactions are system-managed (the backend answers 409
  // Conflict.DepositTransactionsManaged to every write), so its view lists them but offers no
  // record / edit / delete action, even in an active portfolio.
  describe('term deposit transactions are system-managed', () => {
    const depositAsset: AssetResponse = {
      ...asset,
      assetClass: 1, // Deposit
      valuationMode: 2, // CurrencyValued
      name: 'Term deposit',
      currency: 'PLN',
      quantity: 10000,
      manualValue: null,
      manualValueDate: null,
    };
    const openingDeposit: TransactionResponse = {
      ...transaction,
      type: 2, // Deposit
      quantity: 10000,
      unitPrice: 1,
      currency: 'PLN',
      valuePln: 10000,
    };

    function pageButtonTexts(): string[] {
      return Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
        (button) => button.textContent?.trim() ?? '',
      );
    }

    it('the Deposit transactions view has no add / edit / delete actions', async () => {
      await setup(jsonResponse(pagedResponse([openingDeposit])), jsonResponse(depositAsset));

      // The opening transaction is still listed.
      expect(fixture.nativeElement.querySelectorAll('tbody tr.mat-mdc-row').length).toBe(1);
      expect(pageButtonTexts().some((text) => text.includes('New transaction'))).toBe(false);
      expect(fixture.debugElement.queryAll(By.directive(MatMenuTrigger))).toHaveLength(0);
      expect(fixture.nativeElement.querySelector('td.mat-column-actions')).toBeNull();
    });

    it('the Deposit transactions view offers no record button when empty either', async () => {
      await setup(jsonResponse(pagedResponse([])), jsonResponse(depositAsset));

      const buttons = pageButtonTexts();
      expect(buttons.some((text) => text.includes('New transaction'))).toBe(false);
      expect(buttons.some((text) => text.includes('Record your first transaction'))).toBe(false);
    });
  });

  // i18n screens (#132) AC-5: headings, table headers, the transfer label (direction, asset and
  // portfolio interpolated), the row menu, empty state, notices, delete confirmation and failure
  // snackbar fallback follow the language.
  describe('in Polish', () => {
    const outLeg = {
      ...transaction,
      id: '66666666-6666-6666-6666-666666666666',
      type: 3, // Withdraw
      quantity: 1000,
      unitPrice: 1,
      currency: 'PLN',
      valuePln: 1000,
      date: '2026-01-15',
      transfer: {
        counterpartAssetId: '77777777-7777-7777-7777-777777777777',
        counterpartAssetName: 'Term deposit',
        counterpartPortfolioId: '88888888-8888-8888-8888-888888888888',
        counterpartPortfolioName: 'Savings',
        direction: 0, // Out
      },
    } as TransactionResponse;
    const inLeg = {
      ...outLeg,
      id: '99999999-9999-9999-9999-999999999999',
      type: 2, // Deposit
      transfer: { ...outLeg.transfer!, direction: 1 }, // In
    } as TransactionResponse;

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
      await setup(jsonResponse(pagedResponse([transaction])));
      const element = fixture.nativeElement as HTMLElement;
      const texts = async () => [
        ...labelsOf(element, 'a.transactions-page__back'),
        ...labelsOf(element, '.transactions-page__header > button'),
        ...labelsOf(element, 'th:not(:empty)'),
        ...labelsOf(element, 'td.mat-column-type'),
        ...(await menuItems()),
      ];
      const heading = () => textOf(element.querySelector('h1'));
      const summary = () => textOf(element.querySelector('.transactions-page__summary'));

      const english = await texts();
      expect(english).toEqual([
        'Assets',
        'New transaction',
        'Date',
        'Type',
        'Quantity',
        'Currency',
        'Value (PLN)',
        'Buy',
        'Edit',
        'Delete',
      ]);
      expect(heading()).toBe('Transactions — Apple');
      expect(summary()).toContain('Quantity');

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, await texts())).toEqual([]);
      expect(heading()).not.toContain('Transactions');
      expect(heading()).toContain('Apple');
      expect(summary()).not.toContain('Quantity');
    });

    it('labels the row actions button with the type and date in Polish', async () => {
      await setup(jsonResponse(pagedResponse([transaction])));
      const element = fixture.nativeElement as HTMLElement;

      expect(attributesOf(element, 'td button', 'aria-label')).toEqual([
        'Actions for Buy on 2024-01-15',
      ]);

      await switchLanguage(fixture, 'pl');

      const [label] = attributesOf(element, 'td button', 'aria-label');
      expect(label).not.toContain('Actions for');
      expect(label).toContain('2024-01-15');
      expect(matchesTranslation('pl', label), `"${label}" is not a pl.json value`).toBe(true);
    });

    it('renders the transfer label in Polish, with direction, asset and portfolio', async () => {
      await setup(jsonResponse(pagedResponse([outLeg, inLeg])));
      const element = fixture.nativeElement as HTMLElement;
      const transfers = () => labelsOf(element, '.transactions-page__transfer');

      expect(transfers()).toEqual([
        'Transfer to Term deposit (Savings)',
        'Transfer from Term deposit (Savings)',
      ]);

      await switchLanguage(fixture, 'pl');

      const [out, into] = transfers();
      for (const label of [out, into]) {
        expect(label).toContain('Term deposit');
        expect(label).toContain('(Savings)');
        expect(matchesTranslation('pl', label), `"${label}" is not a pl.json value`).toBe(true);
      }
      expect(out).not.toMatch(/^Transfer (to|from)/);
      expect(into).not.toMatch(/^Transfer (to|from)/);
      expect(out).not.toBe(into);
    });

    it('renders the empty state in Polish', async () => {
      await setup(jsonResponse(pagedResponse([])));
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, '.transactions-page__state p'),
        ...labelsOf(element, '.transactions-page__state button'),
      ];

      const english = texts();
      expect(english).toEqual([
        'No transactions recorded for this asset yet.',
        'Record your first transaction',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the archived notice in Polish', async () => {
      await setup(
        jsonResponse(pagedResponse([transaction])),
        jsonResponse(asset),
        jsonResponse({ ...portfolio, isArchived: true }),
      );
      const element = fixture.nativeElement as HTMLElement;
      const notice = () => labelsOf(element, '.transactions-page__archived-notice');

      const english = notice();
      expect(english).toHaveLength(1);
      expect(english[0]).toMatch(/^This portfolio is archived/);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, notice())).toEqual([]);
    });

    it('renders the term-deposit notice in Polish', async () => {
      const deposit: AssetResponse = {
        ...asset,
        assetClass: 1, // Deposit
        valuationMode: 2, // CurrencyValued
        name: 'Term deposit',
        currency: 'PLN',
        manualValue: null,
        manualValueDate: null,
      };
      await setup(jsonResponse(pagedResponse([transaction])), jsonResponse(deposit));
      const element = fixture.nativeElement as HTMLElement;
      const notice = () => labelsOf(element, '.transactions-page__archived-notice');

      const english = notice();
      expect(english).toHaveLength(1);
      expect(english[0]).toMatch(/^A term deposit's transactions are managed by the deposit/);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, notice())).toEqual([]);
    });

    it('renders the load-failure retry button in Polish', async () => {
      await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));
      const element = fixture.nativeElement as HTMLElement;
      const retry = () => labelsOf(element, '.transactions-page__state button');

      expect(retry()).toEqual(['Retry']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(['Retry'], retry())).toEqual([]);
      // The backend's own message stays as it arrived.
      expect(textOf(element.querySelector('.transactions-page__state p'))).toBe(
        'Service unavailable.',
      );
    });

    it('asks to delete in Polish, naming the type and quantity in the message', async () => {
      await setup(jsonResponse(pagedResponse([transaction])));
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      const confirmation = () =>
        (
          dialog.open.mock.calls[0][1] as {
            data: { title: string; message: string; confirmLabel: string };
          }
        ).data;
      await component['remove'](transaction);
      const english = confirmation();
      dialog.open.mockClear();

      await switchLanguage(fixture, 'pl');
      await component['remove'](transaction);
      const polish = confirmation();

      expect(english.title).toBe('Delete this transaction?');
      expect(
        polishProblems([english.title, english.confirmLabel], [polish.title, polish.confirmLabel]),
      ).toEqual([]);
      expect(polish.message).toContain(TRANSLATIONS.pl['enums.transactionType.buy'] as string);
      expect(polish.message).not.toContain('will be permanently deleted');
    });

    it('shows the failure snackbar fallback in Polish', async () => {
      await setup(jsonResponse(pagedResponse([transaction])));
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      fetchSpy.mockImplementationOnce(async () => jsonResponse({ title: 'Boom' }, 500));

      await switchLanguage(fixture, 'pl');
      await component['remove'](transaction);

      expect(snackBar.open).toHaveBeenCalledTimes(1);
      const [message, action] = snackBar.open.mock.calls[0] as [string, string];
      expect(
        polishProblems(['Failed to delete transaction.', 'Dismiss'], [message, action]),
      ).toEqual([]);
    });
  });
});
