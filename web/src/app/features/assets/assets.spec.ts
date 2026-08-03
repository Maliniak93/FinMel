import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AssetResponse, PortfolioResponse } from '../../api/portfolio';
import { Assets } from './assets';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const portfolioId = '22222222-2222-2222-2222-222222222222';

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
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  manualValue: 1000,
  manualValueDate: '2020-01-01',
  transactionCount: 0,
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
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(
    assetsResponse: Response,
    portfolioResponse = jsonResponse(portfolio),
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      return url.includes('/assets') ? assetsResponse : portfolioResponse;
    });
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Assets],
      providers: [
        provideRouter([]),
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

  it('shows a snackbar and does not reload when delete is rejected (asset has transactions)', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockImplementationOnce(async () =>
      jsonResponse(
        {
          detail: 'Asset has transactions and cannot be removed.',
          errorCode: 'Conflict.AssetHasTransactions',
        },
        409,
      ),
    );

    await component['remove'](asset);

    expect(snackBar.open).toHaveBeenCalledWith(
      'Asset has transactions and cannot be removed.',
      'Dismiss',
    );
  });
});
