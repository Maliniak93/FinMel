import { ComponentFixture, TestBed } from '@angular/core/testing';

import { restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { client as marketDataClient } from '../../../api/marketdata/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
import { Bonds } from '../bonds';

const offer = [
  {
    code: 'OTS0127',
    type: 0,
    isin: 'PL0000000001',
    saleStart: '2026-10-01',
    saleEnd: '2026-10-31',
    issuePrice: 100,
    swapPrice: null,
    marginPercent: null,
    firstPeriodRatePercent: 2,
  },
  {
    code: 'ROR1027',
    type: 1,
    isin: 'PL0000000002',
    saleStart: '2026-10-01',
    saleEnd: '2026-10-31',
    issuePrice: 100,
    swapPrice: 99.9,
    marginPercent: 0,
    firstPeriodRatePercent: 4.25,
  },
  {
    code: 'EDO1036',
    type: 5,
    isin: 'PL0000000003',
    saleStart: '2026-10-01',
    saleEnd: '2026-10-31',
    issuePrice: 100,
    swapPrice: 99.9,
    marginPercent: 2,
    firstPeriodRatePercent: 5.35,
  },
];

describe('Bonds page, current offer tab', () => {
  let fixture: ComponentFixture<Bonds>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(series: unknown[]): Promise<void> {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input) =>
        requestUrl(input).includes('/api/marketdata/bond-series')
          ? jsonResponse(series)
          : jsonResponse({ detail: 'Not found.' }, 404),
      );
    await TestBed.configureTestingModule({
      imports: [Bonds],
      providers: [provideI18nTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(Bonds);
    await fixture.whenStable();
    await switchLanguage(fixture, 'pl');
  }

  function rows(): HTMLElement[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'),
    );
  }

  function rowFor(code: string): HTMLElement {
    const row = rows().find((r) => textOf(r).includes(code));
    if (!row) {
      throw new Error(`No row for '${code}'.`);
    }
    return row;
  }

  it('shows the "Aktualna oferta" tab with one row per series', async () => {
    await setup(offer);

    const tabs = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="tab"]'),
    );
    expect(tabs).toHaveLength(1);
    expect(textOf(tabs[0])).toContain('Aktualna oferta');
    expect(rows()).toHaveLength(3);
  });

  it('renders the "dalej" rule per type', async () => {
    await setup(offer);

    expect(textOf(rowFor('OTS0127'))).toContain('stałe');
    expect(textOf(rowFor('ROR1027'))).toMatch(/stopa ref\. NBP \+ marża/);
    expect(textOf(rowFor('EDO1036'))).toMatch(/inflacja \+ marża/);
  });

  it('shows a dash for a missing swap price and a default fee on every row', async () => {
    await setup(offer);

    expect(textOf(rowFor('OTS0127'))).toContain('—');
    expect(textOf(rowFor('EDO1036'))).not.toContain('—');
    rows().forEach((row) => {
      const cells = Array.from(row.querySelectorAll('td'));
      expect(cells.length).toBeGreaterThanOrEqual(6);
      expect(textOf(cells[cells.length - 1])).toMatch(/\d/);
    });
  });

  it('shows the empty state when the catalog has no series', async () => {
    await setup([]);

    expect(rows()).toHaveLength(0);
    expect(textOf(fixture.nativeElement)).toContain(
      'Brak danych z MF — obligację można wprowadzić ręcznie',
    );
  });
});
