import { ComponentFixture, TestBed } from '@angular/core/testing';

import { isTranslationIn, looksLikeTranslationKey, textOf } from '../../../testing/i18n';
import { client as marketdataClient } from '../../api/marketdata/client.gen';
import type { SyncStatusResponse } from '../../api/marketdata';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { Settings } from './settings';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const noRunYet: SyncStatusResponse = { hasRun: false };

const completedRun: SyncStatusResponse = {
  hasRun: true,
  prices: {
    runId: '11111111-1111-1111-1111-111111111111',
    status: 1,
    source: 0,
    startedAt: '2026-08-10T18:30:00Z',
    finishedAt: '2026-08-10T18:31:00Z',
    syncedCount: 5,
    noDataCount: 0,
    failedCount: 0,
  },
};

function requestMethod(input: unknown): string {
  return input instanceof Request ? input.method : 'GET';
}

function syncRun(runId: string, source: number) {
  return {
    runId,
    status: 1,
    source,
    startedAt: '2026-08-10T13:00:00Z',
    finishedAt: '2026-08-10T13:00:30Z',
    syncedCount: 4,
    noDataCount: 0,
    failedCount: 0,
  };
}

function runRow(element: HTMLElement, label: string): HTMLElement | undefined {
  return Array.from(element.querySelectorAll<HTMLElement>('.settings-page__run')).find(
    (row) => textOf(row.querySelector('.settings-page__run-label')) === label,
  );
}

function runChips(element: HTMLElement, label: string): string[] {
  const chips = runRow(element, label)?.querySelectorAll('mat-chip') ?? [];
  return Array.from(chips, (chip) => textOf(chip));
}

describe('Settings', () => {
  let fixture: ComponentFixture<Settings>;
  let component: Settings;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    marketdataClient.setConfig({ baseUrl: 'https://example.test' });
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  });

  async function setup(
    statusResponse: Response | Promise<Response>,
    triggerResponse: Response = new Response(null, { status: 204 }),
    settle = true,
  ): Promise<void> {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input: RequestInfo | URL) => {
        if (requestUrl(input).includes('/api/portfolio/')) {
          return jsonResponse([]);
        }
        return requestMethod(input) === 'POST' ? triggerResponse : await statusResponse;
      });

    await TestBed.configureTestingModule({
      imports: [Settings],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Settings);
    component = fixture.componentInstance;
    if (settle) {
      await fixture.whenStable();
    } else {
      fixture.detectChanges();
    }
  }

  it('shows a skeleton while loading', async () => {
    await setup(new Promise<Response>(() => undefined), undefined, false);
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('app-skeleton')).not.toBeNull();
    expect(element.querySelector('[role="progressbar"]')).toBeNull();
    expect(element.querySelector('app-page-header button')).not.toBeNull();
  });

  it('status failure shows a retry', async () => {
    await setup(jsonResponse({ detail: 'Boom' }, 500));
    const element = fixture.nativeElement as HTMLElement;
    const error = element.querySelector('app-section-error') as HTMLElement;
    expect(error).not.toBeNull();
    expect(element.querySelector('app-page-header button')).not.toBeNull();

    fetchSpy.mockImplementation(async () => jsonResponse(completedRun));
    const retry = error.querySelector('button') as HTMLButtonElement;
    retry.click();
    await fixture.whenStable();

    expect(element.querySelector('app-section-error')).toBeNull();
    expect(element.textContent).toContain('synced 5');
  });

  it('mounts the contribution limits section after the market data sync', async () => {
    await setup(jsonResponse(noRunYet));
    const element = fixture.nativeElement as HTMLElement;
    const sync = element.querySelector('app-empty-state');
    const limits = element.querySelector('app-contribution-limits');

    expect(limits).not.toBeNull();
    expect(sync?.compareDocumentPosition(limits!) ?? 0).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });

  it('empty state before any run', async () => {
    await setup(jsonResponse(noRunYet));
    const empty = (fixture.nativeElement as HTMLElement).querySelector('app-empty-state');
    expect(empty?.textContent).toContain('No sync has run yet.');
  });

  it('should create', async () => {
    await setup(jsonResponse(noRunYet));
    expect(component).toBeTruthy();
  });

  it('shows "no sync has run yet" before any run', async () => {
    await setup(jsonResponse(noRunYet));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No sync has run yet.');
  });

  it('shows the last run status and counts', async () => {
    await setup(jsonResponse(completedRun));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Completed');
    expect(text).toContain('synced 5');
  });

  it('shows the last Prices run and the last FX run', async () => {
    const pricesAndFxRun: SyncStatusResponse = {
      hasRun: true,
      prices: {
        runId: '11111111-1111-1111-1111-111111111111',
        status: 1,
        source: 0,
        startedAt: '2026-08-10T18:30:00Z',
        finishedAt: '2026-08-10T18:31:00Z',
        syncedCount: 5,
        noDataCount: 0,
        failedCount: 0,
      },
      fx: {
        runId: '22222222-2222-2222-2222-222222222222',
        status: 1,
        source: 1,
        startedAt: '2026-08-10T13:00:00Z',
        finishedAt: '2026-08-10T13:00:30Z',
        syncedCount: 4,
        noDataCount: 0,
        failedCount: 0,
      },
    };

    await setup(jsonResponse(pricesAndFxRun));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Prices');
    expect(text).toContain('FX');
    expect(text).toContain('synced 5');
    expect(text).toContain('synced 4');
  });

  it('reloads the status after a successful trigger', async () => {
    await setup(jsonResponse(noRunYet));
    fetchSpy.mockImplementation(async (input: RequestInfo | URL) =>
      requestMethod(input) === 'POST'
        ? new Response(null, { status: 204 })
        : jsonResponse(completedRun),
    );

    await component['triggerSync']();
    await fixture.whenStable();

    expect(component['statusResource'].value()).toEqual(completedRun);
    expect(component['triggerError']()).toBeNull();
  });

  it('surfaces a 409 "already running" trigger failure without touching the status', async () => {
    await setup(
      jsonResponse(noRunYet),
      jsonResponse({ detail: 'A price sync is already running.' }, 409),
    );

    await component['triggerSync']();
    await fixture.whenStable();

    expect(component['triggerError']()).toBe('A price sync is already running.');
  });

  it('renders in Polish', async () => {
    await setup(jsonResponse(completedRun));
    const element = fixture.nativeElement as HTMLElement;
    const heading = () => textOf(element.querySelector('h1'));
    const section = () => textOf(element.querySelector('h2'));
    const button = () => textOf(element.querySelector('button'));
    const kind = () => textOf(element.querySelector('.settings-page__run-label'));
    const status = () => textOf(element.querySelector('mat-chip'));

    const english = [heading(), section(), button(), kind(), status()];
    expect(english).toEqual(['Settings', 'Market data sync', 'Sync now', 'Prices', 'Completed']);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const polish = [heading(), section(), button(), kind(), status()];
    polish.forEach((text, index) => {
      expect(text).not.toBe(english[index]);
      expect(looksLikeTranslationKey(text)).toBe(false);
      expect(isTranslationIn('pl', text), `"${text}" is not a pl.json value`).toBe(true);
    });

    const startedAt = new Date('2026-08-10T18:30:00Z');
    const text = element.textContent ?? '';
    expect(text).toContain(
      new Intl.DateTimeFormat('pl-PL', { dateStyle: 'medium' }).format(startedAt),
    );
    expect(text).not.toContain(
      new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(startedAt),
    );
  });

  it('renders "no sync has run yet" in Polish', async () => {
    await setup(jsonResponse(noRunYet));
    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const status = textOf(
      (fixture.nativeElement as HTMLElement).querySelector('app-empty-state .empty-state__message'),
    );
    expect(status).not.toBe('No sync has run yet.');
    expect(isTranslationIn('pl', status)).toBe(true);
  });

  it('shows the source of each run', async () => {
    await setup(
      jsonResponse({
        hasRun: true,
        prices: syncRun('11111111-1111-1111-1111-111111111111', 1),
        fx: syncRun('22222222-2222-2222-2222-222222222222', 0),
      }),
    );
    const element = fixture.nativeElement as HTMLElement;
    expect(runChips(element, 'Prices')).toContain('Start');
    expect(runChips(element, 'FX')).toContain('Schedule');

    fetchSpy.mockImplementation(async () =>
      jsonResponse({
        hasRun: true,
        prices: syncRun('33333333-3333-3333-3333-333333333333', 2),
      }),
    );
    component['statusResource'].reload();
    await fixture.whenStable();

    expect(runChips(element, 'Prices')).toContain('Manual');
  });

  it('shows the bond catalog run', async () => {
    await setup(
      jsonResponse({
        hasRun: true,
        prices: syncRun('11111111-1111-1111-1111-111111111111', 0),
        bondCatalog: syncRun('44444444-4444-4444-4444-444444444444', 1),
      }),
    );
    const element = fixture.nativeElement as HTMLElement;
    expect(runChips(element, 'Bond catalog')).toEqual(
      expect.arrayContaining(['Completed', 'Start']),
    );

    fetchSpy.mockImplementation(async () =>
      jsonResponse({
        hasRun: true,
        prices: syncRun('11111111-1111-1111-1111-111111111111', 0),
        bondCatalog: null,
      }),
    );
    component['statusResource'].reload();
    await fixture.whenStable();

    expect(runRow(element, 'Bond catalog')).toBeUndefined();
  });

  it('labels an unknown source', async () => {
    await setup(
      jsonResponse({
        hasRun: true,
        prices: syncRun('11111111-1111-1111-1111-111111111111', 99),
      }),
    );
    const element = fixture.nativeElement as HTMLElement;

    expect(runChips(element, 'Prices')).toContain('Unknown');
    expect(element.querySelector('app-section-error')).toBeNull();
  });
});
