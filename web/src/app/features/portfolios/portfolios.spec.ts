import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { PortfolioResponse } from '../../api/portfolio';
import { client as reportingClient } from '../../api/reporting/client.gen';
import type { DashboardResponse } from '../../api/reporting';
import { Portfolios } from './portfolios';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
}

const portfolio: PortfolioResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Retirement',
  description: null,
  currency: 'PLN',
  isArchived: false,
  assetCount: 0,
};

// Portfolios loads its own list plus Reporting's dashboard (for ByPortfolio totals) concurrently
// on init, so every test that doesn't care about valuation gets this empty-but-valid payload —
// it must never be omitted, or the component's `byPortfolio` iteration throws on an undefined
// shape when the mocked response was only ever the portfolios array.
const emptyDashboard: DashboardResponse = {
  netWorthPln: 0,
  asOf: null,
  isStale: false,
  byAssetClass: [],
  byPortfolio: [],
};

describe('Portfolios', () => {
  let fixture: ComponentFixture<Portfolios>;
  let component: Portfolios;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  // Routes the two concurrent initial GETs (portfolios list, Reporting dashboard) to distinct
  // responses by URL, cloning each so a later reload (e.g. after the "Show archived" toggle)
  // gets its own readable body instead of hitting "body already read" on a consumed Response.
  // Any `mockResolvedValueOnce` added later in a test (for a mutation's POST/DELETE + reload)
  // takes priority over this base implementation, so those still work call-by-call regardless
  // of URL.
  async function setup(
    portfoliosResponse: Response,
    dashboardResponse: Response = jsonResponse(emptyDashboard),
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      return (
        url.includes('/api/reporting/dashboard') ? dashboardResponse : portfoliosResponse
      ).clone();
    });
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

  function headerTexts(): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('th'),
      (th) => th.textContent?.trim() ?? '',
    );
  }

  it('should create', async () => {
    await setup(jsonResponse([portfolio]));
    expect(component).toBeTruthy();
  });

  it('loads portfolios into the resource', async () => {
    await setup(jsonResponse([portfolio]));

    expect(component['portfoliosResource'].hasValue()).toBe(true);
    expect(component['portfoliosResource'].value()).toEqual([portfolio]);

    const portfoliosCall = fetchSpy.mock.calls
      .map(([input]: [unknown]) => input as Request)
      .find((request: Request) => !request.url.includes('/api/reporting/dashboard'));
    expect(portfoliosCall?.url).toContain('/api/portfolio/portfolios');
    expect(portfoliosCall?.url).not.toContain('includeArchived=true');
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
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore);
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
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(jsonResponse({ ...portfolio, isArchived: true }));
    fetchSpy.mockResolvedValueOnce(jsonResponse([{ ...portfolio, isArchived: true }]));

    await component['archive'](portfolio);
    await fixture.whenStable();

    const archiveCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(archiveCall.method).toBe('POST');
    expect(archiveCall.url).toContain(`/portfolios/${portfolio.id}/archive`);
    expect(fetchSpy).toHaveBeenCalledTimes(callsBefore + 2);
  });

  it('does not archive when the confirmation is cancelled', async () => {
    await setup(jsonResponse([portfolio]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await component['archive'](portfolio);

    expect(fetchSpy).toHaveBeenCalledTimes(callsBefore);
  });

  it('deletes an empty portfolio after confirmation and reloads', async () => {
    await setup(jsonResponse([portfolio]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(new Response(null, { status: 204 }));
    fetchSpy.mockResolvedValueOnce(jsonResponse([]));

    await component['remove'](portfolio);
    await fixture.whenStable();

    const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(fetchSpy).toHaveBeenCalledTimes(callsBefore + 2);
  });

  it('shows a snackbar and does not reload when delete is rejected (portfolio has assets)', async () => {
    await setup(jsonResponse([portfolio]));
    const callsBefore = fetchSpy.mock.calls.length;
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
    expect(fetchSpy).toHaveBeenCalledTimes(callsBefore + 1);
  });

  // M1.9 — portfolio list columns (S3): Name, Currency, Total value, actions by default; Status
  // only under "Show archived"; a portfolio without a dashboard snapshot renders a placeholder,
  // never a zero.
  describe('columns (M1.9)', () => {
    it('shows Name, Currency, Total value and actions by default — no Status column', async () => {
      await setup(jsonResponse([portfolio]));

      expect(headerTexts()).toEqual(['Name', 'Currency', 'Total value', '']);
    });

    it('shows the Status column with the archived chip once "Show archived" is on', async () => {
      const archivedPortfolio: PortfolioResponse = { ...portfolio, isArchived: true };
      await setup(jsonResponse([archivedPortfolio]));

      component['includeArchived'].set(true);
      await fixture.whenStable();

      expect(headerTexts()).toEqual(['Name', 'Currency', 'Total value', 'Status', '']);
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Archived');
    });

    it('renders a placeholder, not a zero, for a portfolio with no dashboard snapshot', async () => {
      await setup(jsonResponse([portfolio]), jsonResponse(emptyDashboard));

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('—');
      expect(text).not.toContain('0,00 zł');
    });

    it('renders the formatted PLN total value when a dashboard snapshot exists', async () => {
      const dashboard: DashboardResponse = {
        ...emptyDashboard,
        netWorthPln: 12345.67,
        asOf: '2026-08-10',
        byPortfolio: [
          {
            portfolioId: portfolio.id,
            valuePln: 12345.67,
            snapshotDate: '2026-08-10',
            isStale: false,
          },
        ],
      };
      await setup(jsonResponse([portfolio]), jsonResponse(dashboard));

      const expectedValue = new Intl.NumberFormat('pl-PL', {
        style: 'currency',
        currency: 'PLN',
      }).format(12345.67);
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain(expectedValue);
    });

    function deleteButtonInOverlay(
      overlayContainer: OverlayContainer,
    ): HTMLButtonElement | undefined {
      return Array.from(overlayContainer.getContainerElement().querySelectorAll('button')).find(
        (button) => (button.textContent ?? '').includes('Delete'),
      ) as HTMLButtonElement | undefined;
    }

    it('disables Delete for a portfolio with assets (gating unchanged by the column swap)', async () => {
      const withAssets: PortfolioResponse = { ...portfolio, assetCount: 2 };
      await setup(jsonResponse([withAssets]));

      const overlayContainer = TestBed.inject(OverlayContainer);
      const trigger = fixture.debugElement
        .query(By.directive(MatMenuTrigger))
        .injector.get(MatMenuTrigger);

      trigger.openMenu();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(deleteButtonInOverlay(overlayContainer)?.disabled).toBe(true);
    });

    it('enables Delete for a portfolio with no assets (gating unchanged by the column swap)', async () => {
      const empty: PortfolioResponse = { ...portfolio, assetCount: 0 };
      await setup(jsonResponse([empty]));

      const overlayContainer = TestBed.inject(OverlayContainer);
      const trigger = fixture.debugElement
        .query(By.directive(MatMenuTrigger))
        .injector.get(MatMenuTrigger);

      trigger.openMenu();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(deleteButtonInOverlay(overlayContainer)?.disabled).toBe(false);
    });
  });
});
