import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { SecuritiesResponse } from '../../../api/portfolio';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { toDateOnly } from '../../../shared/date-only';
import { formatMoney, formatPercent, formatSignedMoney } from '../../../shared/format';
import { clickRowMenuItem, menuItemLabel, rowMenuItems } from '../../../../testing/archive';
import { restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { ASSET_CLASS } from '../../assets/asset-class';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
import { TransactionFormDialog } from '../../transactions/transaction-form-dialog/transaction-form-dialog';
import {
  PRICE_UNAVAILABLE_REASON,
  cdr,
  empty,
  stocks,
  unavailable,
  vwce,
} from '../testing/securities-fixtures';
import { SecuritiesTable } from './securities-table';

const DAY_MS = 24 * 60 * 60 * 1000;
const today = toDateOnly(new Date());
const staleDate = toDateOnly(new Date(Date.now() - 30 * DAY_MS));

const gain = { ...vwce, name: 'Gain holding', lastPriceDate: today };
const loss = { ...cdr, name: 'Loss holding', lastPriceDate: staleDate };

describe('SecuritiesTable', () => {
  let fixture: ComponentFixture<SecuritiesTable>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    reply: SecuritiesResponse | Response | Promise<Response> = stocks,
    settle = true,
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (!requestUrl(input).includes('/api/portfolio/securities')) {
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return 'holdings' in reply ? jsonResponse(reply) : reply;
    });
    dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(true) }) };

    await TestBed.configureTestingModule({
      imports: [SecuritiesTable],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SecuritiesTable);
    fixture.componentRef.setInput('assetClass', ASSET_CLASS.Stock);
    if (!settle) {
      fixture.detectChanges();
      return;
    }
    await fixture.whenStable();
    await switchLanguage(fixture, 'pl');
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function rows(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'));
  }

  function rowFor(name: string): HTMLElement {
    const row = rows().find((candidate) => textOf(candidate).includes(name));
    if (!row) {
      throw new Error(`No row for '${name}'.`);
    }
    return row;
  }

  function cellOf(row: HTMLElement, header: string): HTMLElement {
    const headers = Array.from(element().querySelectorAll('th'), (th) => textOf(th));
    const index = headers.indexOf(header);
    const cell = row.querySelectorAll<HTMLElement>('td')[index];
    if (index < 0 || !cell) {
      throw new Error(`No '${header}' cell in the row.`);
    }
    return cell;
  }

  function collapsed(text: string): string {
    return text.replace(/\s+/g, ' ');
  }

  it('change and unavailable price', async () => {
    await setup({
      holdings: [
        gain,
        loss,
        unavailable('a3', 'Unpriced holding', PRICE_UNAVAILABLE_REASON.NoQuote),
      ],
      totals: empty.totals,
    } as SecuritiesResponse);

    const gainCell = cellOf(rowFor('Gain holding'), 'Zysk/strata');
    expect(gainCell.querySelector('.money--positive')).not.toBeNull();
    expect(textOf(gainCell)).toContain(collapsed(formatSignedMoney(960, 'PLN')));
    expect(textOf(gainCell)).toContain('trending_up');
    expect(textOf(gainCell)).toContain(collapsed(formatPercent(9.09)));

    const lossCell = cellOf(rowFor('Loss holding'), 'Zysk/strata');
    expect(lossCell.querySelector('.money--negative')).not.toBeNull();
    expect(textOf(lossCell)).toContain(collapsed(formatSignedMoney(-500, 'PLN')));
    expect(textOf(lossCell)).toContain('trending_down');

    expect(textOf(cellOf(rowFor('Gain holding'), 'Wartość'))).toContain(
      collapsed(formatMoney(7740, 'PLN')),
    );

    const unpriced = rowFor('Unpriced holding');
    expect(textOf(unpriced.querySelector('app-status-chip'))).toContain('Brak notowania');
    expect(textOf(cellOf(unpriced, 'Wartość'))).toBe('—');
    expect(textOf(cellOf(unpriced, 'Zysk/strata'))).toBe('—');

    expect(textOf(cellOf(rowFor('Loss holding'), 'Kurs'))).toContain('Nieaktualna');
    expect(textOf(cellOf(rowFor('Gain holding'), 'Kurs'))).not.toContain('Nieaktualna');
  });

  it('row menu', async () => {
    await setup();
    const row = rowFor('Vanguard FTSE All-World');
    const trigger = row.querySelector<HTMLButtonElement>('button.mat-mdc-menu-trigger');
    expect(trigger?.getAttribute('aria-label')).toBe('Akcje dla: Vanguard FTSE All-World');

    const items = await rowMenuItems(fixture, row);
    expect(items.map(menuItemLabel)).toEqual([
      'Kup',
      'Sprzedaj',
      'Dywidenda',
      'Historia transakcji',
    ]);
    const history = items.find((item) => menuItemLabel(item) === 'Historia transakcji');
    expect(history?.getAttribute('href')).toBe(`/wealth/assets/${vwce.assetId}/transactions`);

    document.querySelector<HTMLElement>('.cdk-overlay-backdrop')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    for (const [label, type] of [
      [/^kup$/i, 0],
      [/^sprzedaj$/i, 1],
      [/^dywidenda$/i, 4],
    ] as const) {
      dialog.open.mockClear();

      await clickRowMenuItem(fixture, rowFor('Vanguard FTSE All-World'), label);

      expect(dialog.open).toHaveBeenCalledWith(
        TransactionFormDialog,
        expect.objectContaining({
          data: expect.objectContaining({
            assetId: vwce.assetId,
            assetClass: ASSET_CLASS.Stock,
            type,
          }),
        }),
      );
    }
  });

  it('states', async () => {
    await setup(new Promise<Response>(() => undefined), false);
    expect(element().querySelector('app-skeleton')).not.toBeNull();
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup(jsonResponse({ detail: 'Boom.' }, 500));
    const error = element().querySelector('app-section-error');
    expect(error).not.toBeNull();
    expect(textOf(error)).toContain('Ponów');
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup(empty);
    const emptyState = element().querySelector('app-empty-state');
    expect(textOf(emptyState)).toContain('Nie masz jeszcze akcji');
    expect(textOf(emptyState)).toContain('Dodaj');
  });
});
