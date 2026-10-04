import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import {
  cashCandidates,
  dueEdoBond,
  dueRorBond,
  edoSeries,
  rorSeries,
} from '../../../../testing/bond-fixtures';
import { restoreEnglish } from '../../../../testing/i18n';
import { client as marketDataClient } from '../../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import {
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { requestMethod } from '../../deposits/testing/deposit-fixtures';
import { SettleAllBondsDialog } from './settle-all-bonds-dialog';

describe('SettleAllBondsDialog', () => {
  let fixture: ComponentFixture<SettleAllBondsDialog>;
  let component: SettleAllBondsDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(settleResponse: (url: string) => Response): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (requestMethod(input) === 'GET') {
        if (url.includes('/api/marketdata/bond-series/ROR0627')) {
          return jsonResponse(rorSeries);
        }
        if (url.includes('/api/marketdata/bond-series/EDO1036')) {
          return jsonResponse(edoSeries);
        }
        if (url.includes('/api/portfolio/transfer-candidates')) {
          return jsonResponse(cashCandidates);
        }
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return settleResponse(url);
    });

    await TestBed.configureTestingModule({
      imports: [SettleAllBondsDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { bonds: [dueRorBond, dueEdoBond] } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettleAllBondsDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(rateInput(dueRorBond.assetId, 2)).not.toBeNull();
    });
  }

  function rateInput(assetId: string, periodIndex: number): HTMLInputElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      `[data-testid="period-rate-${assetId}-${periodIndex}"]`,
    );
  }

  async function untick(assetId: string): Promise<void> {
    const box = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      `[data-testid="include-${assetId}"] input[type="checkbox"]`,
    )!;
    box.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function typeRate(assetId: string, periodIndex: number, value: string): Promise<void> {
    const input = rateInput(assetId, periodIndex)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function settleRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter(
        (input: unknown) =>
          requestMethod(input) === 'POST' && requestUrl(input).endsWith('/interest-settlements'),
      ) as Request[];
  }

  it('lists every bond with due periods and prefills the published rates', async () => {
    await setup(() => jsonResponse({}, 201));

    const text = renderedText(fixture);
    expect(text).toContain(dueRorBond.name);
    expect(text).toContain(dueEdoBond.name);
    expect(Number(rateInput(dueRorBond.assetId, 2)!.value)).toBe(3.75);
    expect(rateInput(dueRorBond.assetId, 3)!.value).toBe('');
  });

  it('blocks submit while a ticked bond is missing a rate, and skips it once unticked', async () => {
    await setup(() => jsonResponse({}, 201));

    await component['onSubmit']();
    expect(settleRequests()).toEqual([]);

    await untick(dueRorBond.assetId);
    await component['onSubmit']();

    const requests = settleRequests();
    expect(requests).toHaveLength(1);
    expect(requests[0].url).toContain(`/bonds/${dueEdoBond.assetId}/interest-settlements`);
  });

  it('submits one settlement per ticked bond in sequence and reports the failing bond', async () => {
    await setup((url) =>
      url.includes(`/bonds/${dueEdoBond.assetId}/`)
        ? jsonResponse(
            {
              detail: 'The bond has no interest period due yet.',
              errorCode: 'Conflict.BondInterestNotDue',
            },
            409,
          )
        : jsonResponse({}, 201),
    );
    await typeRate(dueRorBond.assetId, 3, '3.75');

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    const requests = settleRequests();
    expect(requests.map((request) => request.url)).toEqual([
      expect.stringContaining(`/bonds/${dueRorBond.assetId}/interest-settlements`),
      expect.stringContaining(`/bonds/${dueEdoBond.assetId}/interest-settlements`),
    ]);
    expect(renderedText(fixture)).toContain('The bond has no interest period due yet.');
  });
});
