import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';

import { client as marketDataClient } from '../../../../../api/marketdata/client.gen';
import type {
  CustomInstrumentResponse,
  InstrumentDetailsResponse,
  InstrumentSearchResult,
} from '../../../../../api/marketdata';
import { jsonResponse, renderedText } from '../../testing/asset-form-fixtures';
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

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(allowCustomTicker: boolean): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    control = new FormControl<InstrumentOption | null>(null);

    await TestBed.configureTestingModule({ imports: [InstrumentPicker] }).compileComponents();

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
});
