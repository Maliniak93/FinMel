import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';
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

  // spec-02 AC-17: an archived portfolio's row menu offers Restore instead of Archive.
  it('shows Restore only for archived portfolios', async () => {
    const archived: PortfolioResponse = { ...portfolio, isArchived: true };
    await setup(jsonResponse([archived]));

    const overlayContainer = TestBed.inject(OverlayContainer);
    const trigger = fixture.debugElement
      .query(By.directive(MatMenuTrigger))
      .injector.get(MatMenuTrigger);

    trigger.openMenu();
    fixture.detectChanges();
    await fixture.whenStable();

    const menuText = overlayContainer.getContainerElement().textContent ?? '';
    expect(menuText).toContain('Restore');
    expect(menuText).not.toContain('Archive');
  });

  it('restores an archived portfolio after confirmation and reloads', async () => {
    const archived: PortfolioResponse = { ...portfolio, isArchived: true };
    await setup(jsonResponse([archived]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(jsonResponse({ ...archived, isArchived: false }));
    fetchSpy.mockResolvedValueOnce(jsonResponse([{ ...archived, isArchived: false }]));

    await component['restore'](archived);
    await fixture.whenStable();

    const restoreCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(restoreCall.method).toBe('POST');
    expect(restoreCall.url).toContain(`/portfolios/${portfolio.id}/restore`);
    expect(fetchSpy).toHaveBeenCalledTimes(callsBefore + 2);
  });

  it('does not restore when the confirmation is cancelled', async () => {
    const archived: PortfolioResponse = { ...portfolio, isArchived: true };
    await setup(jsonResponse([archived]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await component['restore'](archived);

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

  // spec-08 AC-10: delete no longer has a 409-specific path, but any server failure still surfaces
  // the ProblemDetails detail in a snackbar and skips the reload.
  it('shows a snackbar and does not reload when delete fails on the server', async () => {
    await setup(jsonResponse([portfolio]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        {
          detail: 'Something went wrong while deleting the portfolio.',
          errorCode: 'Unexpected',
        },
        500,
      ),
    );

    await component['remove'](portfolio);

    expect(snackBar.open).toHaveBeenCalledWith(
      'Something went wrong while deleting the portfolio.',
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

    // spec-08 AC-9: delete cascades, so a portfolio with assets is deletable straight from the
    // menu — the confirmation dialog, not a disabled item, is where the safety lives.
    it('enables Delete for a portfolio with assets and warns about its assets in the confirmation', async () => {
      const withAssets: PortfolioResponse = { ...portfolio, assetCount: 2 };
      await setup(jsonResponse([withAssets]));
      const callsBefore = fetchSpy.mock.calls.length;
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      fetchSpy.mockResolvedValueOnce(new Response(null, { status: 204 }));
      fetchSpy.mockResolvedValueOnce(jsonResponse([]));

      const overlayContainer = TestBed.inject(OverlayContainer);
      const trigger = fixture.debugElement
        .query(By.directive(MatMenuTrigger))
        .injector.get(MatMenuTrigger);

      trigger.openMenu();
      fixture.detectChanges();
      await fixture.whenStable();

      const deleteButton = deleteButtonInOverlay(overlayContainer);
      expect(deleteButton?.disabled).toBe(false);
      // No "archive it instead" tooltip any more — MatTooltip marks its host with this class.
      expect(deleteButton?.classList.contains('mat-mdc-tooltip-trigger')).toBe(false);

      deleteButton!.click();
      await vi.waitFor(() => expect(dialog.open).toHaveBeenCalled());

      const message = (dialog.open.mock.calls[0][1] as { data: { message: string } }).data.message;
      expect(message).toMatch(
        /^"Retirement" and its 2 assets?(\(s\))?, with all their transactions, will be permanently deleted\. This can't be undone\.$/,
      );

      await vi.waitFor(() => expect(fetchSpy).toHaveBeenCalledTimes(callsBefore + 2));
      const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
      expect(deleteCall.method).toBe('DELETE');
      expect(deleteCall.url).toContain(`/portfolios/${portfolio.id}`);
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

  // portfolio-description-tooltip: description read back as a tooltip on the name link.
  describe('description tooltip', () => {
    function tooltipOn(link: number): MatTooltip {
      return fixture.debugElement.queryAll(By.css('td a'))[link].injector.get(MatTooltip);
    }

    it('shows the portfolio description as a tooltip on its name', async () => {
      const withDescription: PortfolioResponse = { ...portfolio, description: 'Long-term savings' };
      await setup(jsonResponse([withDescription]));

      const tooltip = tooltipOn(0);
      expect(tooltip.message).toBe('Long-term savings');
      expect(tooltip.disabled).toBe(false);
    });

    it('does not show a tooltip for a portfolio without a description', async () => {
      await setup(jsonResponse([portfolio])); // shared fixture has description: null

      expect(tooltipOn(0).disabled).toBe(true);
    });

    it('treats a whitespace-only description as no description', async () => {
      const blank: PortfolioResponse = { ...portfolio, description: '   ' };
      await setup(jsonResponse([blank]));

      expect(tooltipOn(0).disabled).toBe(true);
    });

    it('truncates a description longer than 200 characters', async () => {
      const long: PortfolioResponse = {
        ...portfolio,
        id: '22222222-2222-2222-2222-222222222222',
        description: 'a'.repeat(250),
      };
      const exact: PortfolioResponse = {
        ...portfolio,
        id: '33333333-3333-3333-3333-333333333333',
        description: 'b'.repeat(200),
      };
      await setup(jsonResponse([long, exact]));

      const longTooltip = tooltipOn(0);
      expect(longTooltip.message).toBe(`${'a'.repeat(200)}…`);
      expect(longTooltip.message.length).toBe(201);
      expect(longTooltip.disabled).toBe(false);

      const exactTooltip = tooltipOn(1);
      expect(exactTooltip.message).toBe('b'.repeat(200));
      expect(exactTooltip.disabled).toBe(false);
    });
  });
});
