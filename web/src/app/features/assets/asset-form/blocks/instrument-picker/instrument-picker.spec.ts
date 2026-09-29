import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';

import { client as marketDataClient } from '../../../../../api/marketdata/client.gen';
import type {
  CustomInstrumentResponse,
  InstrumentDetailsResponse,
  InstrumentSearchResult,
} from '../../../../../api/marketdata';
import type { MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';

import {
  attributesOf,
  labelsOf,
  matchesTranslation,
  polishProblems,
  restoreEnglish,
  switchLanguage,
} from '../../../../../../testing/i18n';
import { provideI18nTesting } from '../../../../../core/i18n/testing';
import { etfSearchResult, jsonResponse, renderedText } from '../../testing/asset-form-fixtures';
import { InstrumentPicker } from './instrument-picker';

type InstrumentOption =
  InstrumentSearchResult | InstrumentDetailsResponse | CustomInstrumentResponse;

// AC-6: the ADR-018 custom-ticker panel, moved as-is out of the old dialog into the picker block.
// The picker writes the chosen instrument into the control it is given.
describe('InstrumentPicker', () => {
  let fixture: ComponentFixture<InstrumentPicker>;
  let component: InstrumentPicker;
  let control: FormControl<InstrumentOption | null>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await restoreEnglish();
  });

  async function setup(allowCustomTicker: boolean): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    control = new FormControl<InstrumentOption | null>(null);

    await TestBed.configureTestingModule({
      imports: [InstrumentPicker],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(InstrumentPicker);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('control', control);
    fixture.componentRef.setInput('assetClass', 2); // Stock
    fixture.componentRef.setInput('allowCustomTicker', allowCustomTicker);
    await fixture.whenStable();
  }

  it('offers the custom-ticker link only with allowCustomTicker', async () => {
    await setup(true);
    expect(renderedText(fixture)).toContain('Verify a new ticker');

    TestBed.resetTestingModule();
    fetchSpy.mockRestore();

    await setup(false);
    expect(renderedText(fixture)).not.toContain('Verify a new ticker');
  });

  describe('verifying a custom ticker (ADR-018)', () => {
    async function setupWithCustomTicker(): Promise<void> {
      await setup(true);
      component['toggleCustomInstrumentForm']();
      component['customInstrumentForm'].setValue({
        ticker: 'MSFT.US',
        name: 'Microsoft Corp.',
        quoteCurrency: 'USD',
      });
    }

    it('Exists: adds and selects the instrument', async () => {
      const created: CustomInstrumentResponse = {
        id: '77777777-7777-7777-7777-777777777777',
        ticker: 'MSFT.US',
        name: 'Microsoft Corp.',
        source: 1,
        quoteCurrency: 'USD',
        assetClass: 2,
        verificationStatus: 0,
      };
      await setupWithCustomTicker();
      fetchSpy.mockResolvedValue(jsonResponse(created, 201));

      await component['submitCustomInstrument']();

      expect(control.value).toEqual(created);
      expect(component['showCustomInstrumentForm']()).toBe(false);
      const request = fetchSpy.mock.calls[0][0] as Request;
      const body = await request.json();
      expect(body).not.toHaveProperty('source');
      expect(body.assetClass).toBe(2);
    });

    it('DoesNotExist: blocks with a human-readable message, no instrument created', async () => {
      await setupWithCustomTicker();
      fetchSpy.mockResolvedValue(
        jsonResponse(
          {
            detail: "'NOPE.US' was not found at Stooq — check the ticker and try again.",
            errorCode: 'Validation.TickerNotFound',
          },
          400,
        ),
      );

      await component['submitCustomInstrument']();

      expect(component['customInstrumentOutcome']()).toBe('notFound');
      expect(component['customInstrumentMessage']()).toContain('was not found at Stooq');
      expect(control.value).toBeNull();
    });

    it('Unreachable: offers "add anyway", which creates the instrument Unverified', async () => {
      await setupWithCustomTicker();
      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          {
            detail:
              "Stooq could not be reached to verify 'MSFT.US' — try again shortly, or add it anyway.",
            errorCode: 'ServiceUnavailable.TickerVerificationUnreachable',
          },
          503,
        ),
      );

      await component['submitCustomInstrument']();

      expect(component['customInstrumentOutcome']()).toBe('unreachable');

      const unverified: CustomInstrumentResponse = {
        id: '88888888-8888-8888-8888-888888888888',
        ticker: 'MSFT.US',
        name: 'Microsoft Corp.',
        source: 1,
        quoteCurrency: 'USD',
        assetClass: 2,
        verificationStatus: 1, // Unverified
      };
      fetchSpy.mockResolvedValueOnce(jsonResponse(unverified, 201));

      // addInstrumentAnyway() is a fire-and-forget wrapper around submitCustomInstrument(true) —
      // call the awaitable method directly for a deterministic test.
      await component['submitCustomInstrument'](true);

      const secondRequest = fetchSpy.mock.calls[1][0] as Request;
      const body = await secondRequest.json();
      expect(body.allowUnverified).toBe(true);
      expect(body).not.toHaveProperty('source');
      expect(control.value).toEqual(unverified);
    });

    it('Conflict: reports it already exists and points at search', async () => {
      await setupWithCustomTicker();
      fetchSpy.mockResolvedValue(
        jsonResponse(
          {
            detail: "An instrument with source 'Stooq' and ticker 'MSFT.US' already exists.",
            errorCode: 'Conflict.InstrumentAlreadyExists',
          },
          409,
        ),
      );

      await component['submitCustomInstrument']();

      expect(component['customInstrumentOutcome']()).toBe('conflict');
      expect(control.value).toBeNull();
    });
  });

  // i18n screens (#132) AC-4: the field labels, the search placeholder and hint, the selected
  // instrument line, the custom-ticker link and panel, and the outcome texts follow the language. A
  // backend `detail` in an outcome stays as it arrived.
  describe('in Polish', () => {
    function click(selector: string): void {
      (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(selector)!.click();
      fixture.detectChanges();
    }

    it('renders in Polish', async () => {
      await setup(true);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'mat-label'),
        ...attributesOf(element, 'input[placeholder]', 'placeholder'),
        ...labelsOf(element, '.asset-form__hint'),
        ...labelsOf(element, '.asset-form__custom-instrument-link'),
      ];

      const english = texts();
      expect(english).toEqual([
        'Instrument',
        'Search by ticker or name',
        'Pick an existing instrument above, or verify a new ticker below — creation is blocked until one is selected.',
        "Can't find it? Verify a new ticker",
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the custom-ticker panel in Polish', async () => {
      await setup(true);
      click('.asset-form__custom-instrument-link');
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'mat-label'),
        ...labelsOf(element, 'mat-hint'),
        ...labelsOf(element, '.asset-form__custom-instrument-link'),
        ...labelsOf(element, '.asset-form__custom-instrument > button'),
      ];

      const english = texts();
      expect(english).toEqual([
        'Instrument',
        'Ticker / id',
        'Name',
        'Quote currency',
        '3-letter code, e.g. USD',
        'Cancel',
        'Verify and add',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the selected instrument in Polish', async () => {
      await setup(true);
      component['onInstrumentOptionSelected']({
        option: { value: etfSearchResult },
      } as MatAutocompleteSelectedEvent);
      fixture.detectChanges();
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      const line = () => labelsOf(element, '.asset-form__selected-instrument')[0];

      expect(line()).toBe('Verified: VWCE.DE — Vanguard FTSE All-World (EUR)');

      await switchLanguage(fixture, 'pl');

      expect(line()).not.toMatch(/^Verified:/);
      expect(line()).toContain('VWCE.DE — Vanguard FTSE All-World');
      expect(line()).toContain('EUR');
      expect(matchesTranslation('pl', line()), `"${line()}" is not a pl.json value`).toBe(true);
    });

    async function submitAfterSwitch(response: Response): Promise<HTMLElement> {
      await setup(true);
      component['toggleCustomInstrumentForm']();
      component['customInstrumentForm'].setValue({
        ticker: 'MSFT.US',
        name: 'Microsoft Corp.',
        quoteCurrency: 'USD',
      });
      fetchSpy.mockResolvedValue(response);
      await switchLanguage(fixture, 'pl');

      await component['submitCustomInstrument']();
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.nativeElement as HTMLElement;
    }

    it('shows the not-found outcome with the backend detail as it arrived', async () => {
      const detail = "'NOPE.US' was not found at Stooq — check the ticker and try again.";
      const element = await submitAfterSwitch(
        jsonResponse({ detail, errorCode: 'Validation.TickerNotFound' }, 400),
      );

      expect(component['customInstrumentOutcome']()).toBe('notFound');
      expect(labelsOf(element, '.asset-form__error')).toEqual([detail]);
      expect(
        polishProblems(
          ['Verify and add'],
          labelsOf(element, '.asset-form__custom-instrument > button'),
        ),
      ).toEqual([]);
    });

    it('shows the unreachable outcome with a Polish "add anyway" button', async () => {
      const detail =
        "Stooq could not be reached to verify 'MSFT.US' — try again shortly, or add it anyway.";
      const element = await submitAfterSwitch(
        jsonResponse(
          { detail, errorCode: 'ServiceUnavailable.TickerVerificationUnreachable' },
          503,
        ),
      );

      expect(component['customInstrumentOutcome']()).toBe('unreachable');
      expect(labelsOf(element, '.asset-form__warning p')).toEqual([detail]);
      expect(
        polishProblems(['Add anyway'], labelsOf(element, '.asset-form__warning button')),
      ).toEqual([]);
    });

    it('shows the conflict outcome with a Polish hint after the backend detail', async () => {
      const detail = "An instrument with source 'Stooq' and ticker 'MSFT.US' already exists.";
      const element = await submitAfterSwitch(
        jsonResponse({ detail, errorCode: 'Conflict.InstrumentAlreadyExists' }, 409),
      );

      expect(component['customInstrumentOutcome']()).toBe('conflict');
      const [message] = labelsOf(element, '.asset-form__error');
      expect(message.startsWith(detail)).toBe(true);
      const hint = message.slice(detail.length).trim();
      expect(polishProblems(['Try searching for it above instead.'], [hint])).toEqual([]);
    });

    it('shows the generic failure fallback in Polish', async () => {
      const element = await submitAfterSwitch(jsonResponse({ title: 'Boom' }, 500));

      expect(component['customInstrumentOutcome']()).toBe('error');
      expect(
        polishProblems(['Failed to add instrument.'], labelsOf(element, '.asset-form__error')),
      ).toEqual([]);
    });
  });
});
