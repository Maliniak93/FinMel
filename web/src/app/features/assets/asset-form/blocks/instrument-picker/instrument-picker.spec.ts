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
import {
  etfCandidate,
  etfSearchResult,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../testing/asset-form-fixtures';
import { InstrumentPicker } from './instrument-picker';

type InstrumentOption =
  InstrumentSearchResult | InstrumentDetailsResponse | CustomInstrumentResponse;

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
    fixture.componentRef.setInput('assetClass', 2);
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
        exchange: null,
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
        verificationStatus: 1,
        exchange: null,
      };
      fetchSpy.mockResolvedValueOnce(jsonResponse(unverified, 201));

      await component['submitCustomInstrument'](true);

      const secondRequest = fetchSpy.mock.calls[1][0] as Request;
      const body = await secondRequest.json();
      expect(body.allowUnverified).toBe(true);
      expect(body).not.toHaveProperty('source');
      expect(control.value).toEqual(unverified);
    });
  });

  describe('searching for an Etf', () => {
    const created: CustomInstrumentResponse = {
      id: '99999999-9999-9999-9999-999999999999',
      ticker: 'VWCE.DE',
      name: 'Vanguard FTSE All-World UCITS ETF',
      source: 1,
      quoteCurrency: 'EUR',
      assetClass: 3,
      verificationStatus: 0,
      exchange: 'Xetra',
    };

    async function typeAndSearch(providerUnavailable: boolean): Promise<void> {
      await setup(false);
      fixture.componentRef.setInput('assetClass', 3);
      fetchSpy.mockImplementation(async (input: unknown) => {
        if ((input as Request).method === 'POST') {
          return jsonResponse(created, 201);
        }
        return jsonResponse({ results: [etfCandidate], providerUnavailable });
      });

      component['instrumentControl'].setValue('vwce');
      await new Promise((resolve) => setTimeout(resolve, 400));
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('sends the asset class, and picking a "nowy" option posts the instrument once and selects it', async () => {
      await typeAndSearch(false);
      const searchRequest = fetchSpy.mock.calls.find(
        (call: unknown[]) => (call[0] as Request).method === 'GET',
      )![0];
      expect(requestUrl(searchRequest)).toContain('assetClass=3');
      await switchLanguage(fixture, 'pl');

      (fixture.nativeElement as HTMLElement)
        .querySelector('input')!
        .dispatchEvent(new Event('focusin'));
      fixture.detectChanges();
      await fixture.whenStable();
      const option = document.querySelector<HTMLElement>('mat-option')!;
      expect(option.textContent).toContain('VWCE.DE');
      expect(option.textContent).toContain('Xetra');
      expect(option.textContent).toContain('nowy');
      option.click();
      fixture.detectChanges();
      await fixture.whenStable();

      const posts = fetchSpy.mock.calls.filter(
        (call: unknown[]) => (call[0] as Request).method === 'POST',
      );
      expect(posts).toHaveLength(1);
      const body = await (posts[0][0] as Request).clone().json();
      expect(body).toMatchObject({
        ticker: 'VWCE.DE',
        name: 'Vanguard FTSE All-World UCITS ETF',
        assetClass: 3,
      });
      expect(body).not.toHaveProperty('quoteCurrency');
      expect(control.value).toEqual(created);
    });

    it('shows the hint when the provider search is unavailable', async () => {
      await typeAndSearch(true);
      await switchLanguage(fixture, 'pl');

      expect(renderedText(fixture)).toContain(
        'Wyszukiwarka giełdowa niedostępna — pokazuję tylko znane instrumenty',
      );
    });

    it('shows no hint when the provider answered', async () => {
      await typeAndSearch(false);
      await switchLanguage(fixture, 'pl');

      expect(renderedText(fixture)).not.toContain('Wyszukiwarka giełdowa niedostępna');
    });
  });

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

    it('shows the generic failure fallback in Polish', async () => {
      const element = await submitAfterSwitch(jsonResponse({ title: 'Boom' }, 500));

      expect(component['customInstrumentOutcome']()).toBe('error');
      expect(
        polishProblems(['Failed to add instrument.'], labelsOf(element, '.asset-form__error')),
      ).toEqual([]);
    });
  });
});
