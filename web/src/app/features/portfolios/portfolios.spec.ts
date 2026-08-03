import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { PortfolioResponse } from '../../api/portfolio';
import { Portfolios } from './portfolios';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const portfolio: PortfolioResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Retirement',
  description: null,
  currency: 'PLN',
  isArchived: false,
  assetCount: 0,
};

describe('Portfolios', () => {
  let fixture: ComponentFixture<Portfolios>;
  let component: Portfolios;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(initialResponse: Response): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(initialResponse);
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Portfolios],
      providers: [
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Portfolios);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('should create', async () => {
    await setup(jsonResponse([portfolio]));
    expect(component).toBeTruthy();
  });

  it('loads portfolios into the resource', async () => {
    await setup(jsonResponse([portfolio]));

    expect(component['portfoliosResource'].hasValue()).toBe(true);
    expect(component['portfoliosResource'].value()).toEqual([portfolio]);

    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.url).toContain('/api/portfolio/portfolios');
    expect(request.url).not.toContain('includeArchived=true');
  });

  it('reports an empty resource when there are no portfolios', async () => {
    await setup(jsonResponse([]));

    expect(component['portfoliosResource'].hasValue()).toBe(true);
    expect(component['portfoliosResource'].value()).toEqual([]);
  });

  it('surfaces a load failure through the resource error', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));

    expect(component['portfoliosResource'].error()?.message).toBe('Service unavailable.');
  });

  it('re-fetches with includeArchived when the toggle flips', async () => {
    await setup(jsonResponse([portfolio]));

    component['includeArchived'].set(true);
    await fixture.whenStable();

    const lastCall = fetchSpy.mock.calls.at(-1)?.[0] as Request;
    expect(lastCall.url).toContain('includeArchived=true');
  });

  it('reloads after a successful create-dialog save', async () => {
    await setup(jsonResponse([portfolio]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBeGreaterThan(1);
  });

  it('does not reload when the create dialog is dismissed without saving', async () => {
    await setup(jsonResponse([portfolio]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBe(callsBefore);
  });

  it('archives a portfolio after confirmation and reloads', async () => {
    await setup(jsonResponse([portfolio]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(jsonResponse({ ...portfolio, isArchived: true }));
    fetchSpy.mockResolvedValueOnce(jsonResponse([{ ...portfolio, isArchived: true }]));

    await component['archive'](portfolio);
    await fixture.whenStable();

    const archiveCall = fetchSpy.mock.calls[1][0] as Request;
    expect(archiveCall.method).toBe('POST');
    expect(archiveCall.url).toContain(`/portfolios/${portfolio.id}/archive`);
    expect(fetchSpy).toHaveBeenCalledTimes(3);
  });

  it('does not archive when the confirmation is cancelled', async () => {
    await setup(jsonResponse([portfolio]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await component['archive'](portfolio);

    expect(fetchSpy).toHaveBeenCalledTimes(1);
  });

  it('deletes an empty portfolio after confirmation and reloads', async () => {
    await setup(jsonResponse([portfolio]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(new Response(null, { status: 204 }));
    fetchSpy.mockResolvedValueOnce(jsonResponse([]));

    await component['remove'](portfolio);
    await fixture.whenStable();

    const deleteCall = fetchSpy.mock.calls[1][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(fetchSpy).toHaveBeenCalledTimes(3);
  });

  it('shows a snackbar and does not reload when delete is rejected (portfolio has assets)', async () => {
    await setup(jsonResponse([portfolio]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        {
          detail: 'Portfolio has assets and cannot be deleted; archive it instead.',
          errorCode: 'Conflict.PortfolioHasAssets',
        },
        409,
      ),
    );

    await component['remove'](portfolio);

    expect(snackBar.open).toHaveBeenCalledWith(
      'Portfolio has assets and cannot be deleted; archive it instead.',
      'Dismiss',
    );
    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });
});
