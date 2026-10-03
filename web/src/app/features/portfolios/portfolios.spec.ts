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
  labelsOf,
  matchesTranslation,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  textOf,
  textsOf,
} from '../../../testing/i18n';
import { provideI18nTesting } from '../../core/i18n/testing';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { PortfolioResponse } from '../../api/portfolio';
import { client as reportingClient } from '../../api/reporting/client.gen';
import type { DashboardResponse } from '../../api/reporting';
import { Portfolios } from './portfolios';

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

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

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
        provideI18nTesting(),
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

  function requestsFrom(index: number): Request[] {
    return fetchSpy.mock.calls.slice(index).map(([input]: [unknown]) => input as Request);
  }

  function portfoliosListReloadedFrom(index: number): boolean {
    return requestsFrom(index).some(
      (request) => request.method === 'GET' && request.url.includes('/api/portfolio/portfolios'),
    );
  }

  function dashboardFetchCount(): number {
    return fetchSpy.mock.calls.filter(([input]: [unknown]) =>
      requestUrl(input).includes('/api/reporting/dashboard'),
    ).length;
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

    await component['archive'](portfolio);
    await fixture.whenStable();

    const archiveCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(archiveCall.method).toBe('POST');
    expect(archiveCall.url).toContain(`/portfolios/${portfolio.id}/archive`);
    expect(portfoliosListReloadedFrom(callsBefore + 1)).toBe(true);
  });

  it('does not archive when the confirmation is cancelled', async () => {
    await setup(jsonResponse([portfolio]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await component['archive'](portfolio);

    expect(fetchSpy).toHaveBeenCalledTimes(callsBefore);
  });

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

    await component['restore'](archived);
    await fixture.whenStable();

    const restoreCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(restoreCall.method).toBe('POST');
    expect(restoreCall.url).toContain(`/portfolios/${portfolio.id}/restore`);
    expect(portfoliosListReloadedFrom(callsBefore + 1)).toBe(true);
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

    await component['remove'](portfolio);
    await fixture.whenStable();

    const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(portfoliosListReloadedFrom(callsBefore + 1)).toBe(true);
  });

  it('reloads total values after archive / restore / delete', async () => {
    await setup(jsonResponse([portfolio]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const initialDashboardFetches = dashboardFetchCount();

    fetchSpy.mockResolvedValueOnce(jsonResponse({ ...portfolio, isArchived: true }));
    await component['archive'](portfolio);
    await fixture.whenStable();
    expect(dashboardFetchCount()).toBe(initialDashboardFetches + 1);

    const archived: PortfolioResponse = { ...portfolio, isArchived: true };
    fetchSpy.mockResolvedValueOnce(jsonResponse({ ...archived, isArchived: false }));
    await component['restore'](archived);
    await fixture.whenStable();
    expect(dashboardFetchCount()).toBe(initialDashboardFetches + 2);

    fetchSpy.mockResolvedValueOnce(new Response(null, { status: 204 }));
    await component['remove'](portfolio);
    await fixture.whenStable();
    expect(dashboardFetchCount()).toBe(initialDashboardFetches + 3);
  });

  it('does not reload total values when an archive fails on the server', async () => {
    await setup(jsonResponse([portfolio]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const initialDashboardFetches = dashboardFetchCount();
    fetchSpy.mockResolvedValueOnce(
      jsonResponse({ detail: 'Something went wrong.', errorCode: 'Unexpected' }, 500),
    );

    await component['archive'](portfolio);
    await fixture.whenStable();

    expect(dashboardFetchCount()).toBe(initialDashboardFetches);
  });

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
      expect(text).not.toContain(
        new Intl.NumberFormat('en-US', { style: 'currency', currency: 'PLN' }).format(0),
      );
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

      const expectedValue = new Intl.NumberFormat('en-US', {
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

    it('enables Delete for a portfolio with assets and warns about its assets in the confirmation', async () => {
      const withAssets: PortfolioResponse = { ...portfolio, assetCount: 2 };
      await setup(jsonResponse([withAssets]));
      const callsBefore = fetchSpy.mock.calls.length;
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      fetchSpy.mockResolvedValueOnce(new Response(null, { status: 204 }));

      const overlayContainer = TestBed.inject(OverlayContainer);
      const trigger = fixture.debugElement
        .query(By.directive(MatMenuTrigger))
        .injector.get(MatMenuTrigger);

      trigger.openMenu();
      fixture.detectChanges();
      await fixture.whenStable();

      const deleteButton = deleteButtonInOverlay(overlayContainer);
      expect(deleteButton?.disabled).toBe(false);
      expect(deleteButton?.classList.contains('mat-mdc-tooltip-trigger')).toBe(false);

      deleteButton!.click();
      await vi.waitFor(() => expect(dialog.open).toHaveBeenCalled());

      const message = (dialog.open.mock.calls[0][1] as { data: { message: string } }).data.message;
      expect(message).toMatch(
        /^"Retirement" and its 2 assets?(\(s\))?, with all their transactions, will be permanently deleted\. This can't be undone\.$/,
      );

      await vi.waitFor(() => expect(portfoliosListReloadedFrom(callsBefore + 1)).toBe(true));
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
      await setup(jsonResponse([portfolio]));

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

  describe('in Polish', () => {
    const archived: PortfolioResponse = { ...portfolio, isArchived: true };

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

    function confirmationData(): { title: string; message: string; confirmLabel: string } {
      return (dialog.open.mock.calls[0][1] as { data: never }).data;
    }

    function confirmationTexts(data: { title: string; confirmLabel: string }): string[] {
      return [data.title, data.confirmLabel];
    }

    it('renders in Polish', async () => {
      await setup(jsonResponse([archived]));
      component['includeArchived'].set(true);
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      const placeholderTooltip = () =>
        fixture.debugElement
          .query(By.css('.portfolios-page__value-placeholder'))
          .injector.get(MatTooltip).message;
      const texts = async () => [
        ...labelsOf(element, 'h1'),
        ...labelsOf(element, 'mat-slide-toggle'),
        ...labelsOf(element, '.portfolios-page__header-actions > button'),
        ...labelsOf(element, 'th:not(:empty)'),
        ...labelsOf(element, 'mat-chip'),
        placeholderTooltip(),
        ...(await menuItems()),
      ];

      const english = await texts();
      expect(english).toEqual([
        'Portfolios',
        'Show archived',
        'New portfolio',
        'Name',
        'Currency',
        'Total value',
        'Status',
        'Archived',
        'No valuation yet',
        'Edit',
        'Restore',
        'Delete',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, await texts(), ['Status'])).toEqual([]);
    });

    it('labels the row actions button with the portfolio name in Polish', async () => {
      await setup(jsonResponse([portfolio]));
      const element = fixture.nativeElement as HTMLElement;
      const label = () => element.querySelector('td button')?.getAttribute('aria-label') ?? '';

      expect(label()).toBe('Actions for Retirement');

      await switchLanguage(fixture, 'pl');

      expect(label()).not.toContain('Actions for');
      expect(label()).toContain('Retirement');
      expect(matchesTranslation('pl', label()), `"${label()}" is not a pl.json value`).toBe(true);
    });

    it('renders the empty state in Polish', async () => {
      await setup(jsonResponse([]));
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...textsOf(element, '.portfolios-page__state p'),
        ...textsOf(element, '.portfolios-page__state button'),
      ];

      const english = texts();
      expect(english).toEqual([
        "You don't have any portfolios yet.",
        'Create your first portfolio',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the load-failure retry button in Polish', async () => {
      await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));
      const element = fixture.nativeElement as HTMLElement;

      expect(textsOf(element, '.portfolios-page__state button')).toEqual(['Retry']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(['Retry'], textsOf(element, '.portfolios-page__state button'))).toEqual(
        [],
      );
      expect(textOf(element.querySelector('.portfolios-page__state p'))).toBe(
        'Service unavailable.',
      );
    });

    it('asks to archive in Polish, with the portfolio name in the message', async () => {
      await setup(jsonResponse([portfolio]));
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      await component['archive'](portfolio);
      const english = confirmationData();
      dialog.open.mockClear();

      await switchLanguage(fixture, 'pl');
      await component['archive'](portfolio);
      const polish = confirmationData();

      expect(english.title).toBe('Archive this portfolio?');
      expect(polishProblems(confirmationTexts(english), confirmationTexts(polish))).toEqual([]);
      expect(polish.message).not.toBe(english.message);
      expect(polish.message).toContain('"Retirement"');
      expect(polish.message).not.toContain('will be hidden');
      expect(
        matchesTranslation('pl', polish.message),
        `"${polish.message}" is not a pl.json value`,
      ).toBe(true);
    });

    it('asks to restore in Polish', async () => {
      await setup(jsonResponse([archived]));
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      await component['restore'](archived);
      const english = confirmationData();
      dialog.open.mockClear();

      await switchLanguage(fixture, 'pl');
      await component['restore'](archived);
      const polish = confirmationData();

      expect(english.title).toBe('Restore this portfolio?');
      expect(polishProblems(confirmationTexts(english), confirmationTexts(polish))).toEqual([]);
      expect(polish.message).toContain('"Retirement"');
      expect(polish.message).not.toContain('will return');
    });

    it('asks to delete in Polish, with the portfolio name and asset count in the message', async () => {
      const withAssets: PortfolioResponse = { ...portfolio, assetCount: 2 };
      await setup(jsonResponse([withAssets]));
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      await component['remove'](withAssets);
      const english = confirmationData();
      dialog.open.mockClear();

      await switchLanguage(fixture, 'pl');
      await component['remove'](withAssets);
      const polish = confirmationData();

      expect(english.title).toBe('Delete this portfolio?');
      expect(polishProblems(confirmationTexts(english), confirmationTexts(polish))).toEqual([]);
      expect(polish.message).toContain('"Retirement"');
      expect(polish.message).toContain('2');
      expect(polish.message).not.toContain('permanently deleted');
    });

    it('shows the failure snackbar fallback in Polish', async () => {
      await setup(jsonResponse([portfolio]));
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      fetchSpy.mockResolvedValueOnce(jsonResponse({ title: 'Boom' }, 500));

      await switchLanguage(fixture, 'pl');
      await component['archive'](portfolio);

      expect(snackBar.open).toHaveBeenCalledTimes(1);
      const [message, action] = snackBar.open.mock.calls[0] as [string, string];
      expect(
        polishProblems(['Failed to archive portfolio.', 'Dismiss'], [message, action]),
      ).toEqual([]);
    });
  });
});
