import { ComponentFixture, TestBed } from '@angular/core/testing';

import { isTranslationIn, looksLikeTranslationKey, textOf } from '../../../testing/i18n';
import { client as marketdataClient } from '../../api/marketdata/client.gen';
import type { SyncStatusResponse } from '../../api/marketdata';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
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

describe('Settings', () => {
  let fixture: ComponentFixture<Settings>;
  let component: Settings;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    marketdataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  });

  async function setup(
    statusResponse: Response,
    triggerResponse: Response = new Response(null, { status: 204 }),
  ): Promise<void> {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input: RequestInfo | URL) =>
        requestMethod(input) === 'POST' ? triggerResponse : statusResponse,
      );

    await TestBed.configureTestingModule({
      imports: [Settings],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Settings);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

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
        startedAt: '2026-08-10T18:30:00Z',
        finishedAt: '2026-08-10T18:31:00Z',
        syncedCount: 5,
        noDataCount: 0,
        failedCount: 0,
      },
      fx: {
        runId: '22222222-2222-2222-2222-222222222222',
        status: 1,
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
      (fixture.nativeElement as HTMLElement).querySelector('.settings-page__status-text'),
    );
    expect(status).not.toBe('No sync has run yet.');
    expect(isTranslationIn('pl', status)).toBe(true);
  });
});
