import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type {
  AssetResponse,
  PagedResponseOfTransactionResponse,
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
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  manualValue: 1000,
  manualValueDate: '2020-01-01',
  transactionCount: 1,
};

const transaction: TransactionResponse = {
  id: '33333333-3333-3333-3333-333333333333',
  assetId,
  type: 0,
  quantity: 10,
  unitPrice: 100,
  fee: 5,
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

  async function setup(
    transactionsResponse: Response,
    assetResponse = jsonResponse(asset),
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      return url.includes('/transactions') ? transactionsResponse : assetResponse;
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
});
