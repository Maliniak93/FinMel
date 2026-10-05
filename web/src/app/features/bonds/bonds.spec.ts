import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import {
  clickRowMenuItem,
  menuItemLabel,
  rowMenuItems,
  showArchived as showArchivedToggle,
} from '../../../testing/archive';
import {
  activeBond,
  archivedBond,
  BOND_ESTIMATE_UNAVAILABLE_REASON,
  dueRorBond,
  edoYearOneSettledBond,
  interestDueBond,
  maturedBond,
  octoberOffer,
  partiallyRedeemedEdoBond,
  redeemedTosBond,
  settledRorBond,
  settledTosBond,
  swapBornBond,
  unsettledTosBond,
  type BondFixture,
} from '../../../testing/bond-fixtures';
import { restoreEnglish, switchLanguage, textOf } from '../../../testing/i18n';
import { client as marketDataClient } from '../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../shared/format';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { BondPurchaseDialog } from './bond-purchase-dialog/bond-purchase-dialog';
import { Bonds } from './bonds';
import { EarlyRedeemBondDialog } from './early-redeem-bond-dialog/early-redeem-bond-dialog';
import { RedeemBondDialog } from './redeem-bond-dialog/redeem-bond-dialog';
import { SettleAllBondsDialog } from './settle-all-bonds-dialog/settle-all-bonds-dialog';
import { SettleBondInterestDialog } from './settle-bond-interest-dialog/settle-bond-interest-dialog';
import { SwapBondDialog } from './swap-bond-dialog/swap-bond-dialog';

describe('Bonds page, my bonds tab', () => {
  let fixture: ComponentFixture<Bonds>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    bonds: BondFixture[] = [activeBond, interestDueBond, maturedBond],
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      const url = requestUrl(input);
      if (request.method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      if (request.method === 'POST' && /\/assets\/[^/]+\/(archive|restore)$/.test(url)) {
        return jsonResponse({});
      }
      if (url.includes('/api/marketdata/bond-series')) {
        return jsonResponse(octoberOffer);
      }
      return url.includes('/api/portfolio/bonds')
        ? jsonResponse(bonds)
        : jsonResponse({ detail: 'Not found.' }, 404);
    });
    dialog = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Bonds],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Bonds);
    await fixture.whenStable();
  }

  function tabs(): HTMLElement[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="tab"]'),
    );
  }

  function rows(): HTMLElement[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'),
    );
  }

  function rowFor(text: string): HTMLElement {
    const row = rows().find((candidate) => textOf(candidate).includes(text));
    if (!row) {
      throw new Error(`No row for '${text}'.`);
    }
    return row;
  }

  function bondListCalls(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        requestUrl(call[0]).includes('/api/portfolio/bonds') &&
        (typeof call[0] === 'string' || (call[0] as Request).method === 'GET'),
    ).length;
  }

  it('opens on "Moje obligacje", placed before "Aktualna oferta"', async () => {
    await setup();
    await switchLanguage(fixture, 'pl');

    expect(tabs()).toHaveLength(2);
    expect(textOf(tabs()[0])).toContain('Moje obligacje');
    expect(textOf(tabs()[1])).toContain('Aktualna oferta');
    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
    expect(rows()).toHaveLength(3);
  });

  it('lists each bond with series, purchase date, count, book value and maturity date', async () => {
    await setup();

    const text = textOf(rowFor(activeBond.name));
    expect(text).toContain('EDO1036');
    expect(text).toContain('50');
    expect(text).toContain(formatMoney(activeBond.bookValue, 'PLN').replace(/\s+/g, ' '));
    expect(text).toContain('2026');
    expect(text).toContain('2036');
  });

  it('shows the status chip per bond in Polish', async () => {
    await setup();
    await switchLanguage(fixture, 'pl');

    expect(textOf(rowFor(activeBond.name))).toContain('Aktywna');
    expect(textOf(rowFor(interestDueBond.name))).toContain('Odsetki do rozliczenia');
    expect(textOf(rowFor(maturedBond.name))).toContain('Do wykupu');
  });

  it('hides archived bonds until Show archived is on', async () => {
    await setup([activeBond, archivedBond]);

    expect(rows()).toHaveLength(1);
    expect(textOf(fixture.nativeElement)).not.toContain(archivedBond.name);

    await showArchivedToggle(fixture);

    expect(rows()).toHaveLength(2);
    expect(textOf(rowFor(archivedBond.name))).toContain('Archived');
  });

  it('Edit opens the bond purchase dialog with that bond and reloads after a save', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = bondListCalls();

    await clickRowMenuItem(fixture, rowFor(activeBond.name), /edit/i);

    expect(dialog.open).toHaveBeenCalledWith(
      BondPurchaseDialog,
      expect.objectContaining({
        data: expect.objectContaining({
          bond: expect.objectContaining({ assetId: activeBond.assetId }),
        }),
      }),
    );
    await vi.waitFor(() => expect(bondListCalls()).toBeGreaterThan(callsBefore));
  });

  it('Delete asks for confirmation, then removes the asset and reloads', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = bondListCalls();

    await clickRowMenuItem(fixture, rowFor(activeBond.name), /delete/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    await vi.waitFor(() => {
      const deleteCall = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .find((request: Request) => request.method === 'DELETE');
      expect(deleteCall?.url).toContain(
        `/api/portfolio/portfolios/${activeBond.portfolioId}/assets/${activeBond.assetId}`,
      );
    });
    await vi.waitFor(() => expect(bondListCalls()).toBeGreaterThan(callsBefore));
  });

  it('does not delete when the confirmation is cancelled', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await clickRowMenuItem(fixture, rowFor(activeBond.name), /delete/i);

    expect(
      fetchSpy.mock.calls.some((call: unknown[]) => (call[0] as Request).method === 'DELETE'),
    ).toBe(false);
  });

  it('hides redeemed bonds behind "Pokaż wykupione" and marks a swap-born bond "z zamiany"', async () => {
    await setup([swapBornBond, redeemedTosBond]);
    await switchLanguage(fixture, 'pl');

    expect(rows()).toHaveLength(1);
    expect(textOf(rowFor(swapBornBond.name))).toContain(`z zamiany: ${redeemedTosBond.name}`);

    const toggle = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="show-redeemed"] button',
    );
    expect(textOf(toggle!.closest('mat-slide-toggle')!)).toContain('Pokaż wykupione');
    toggle!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(rows()).toHaveLength(2);
    const redeemedRow = rows().find((row) => !textOf(row).includes('z zamiany'));
    expect(textOf(redeemedRow!)).toContain(redeemedTosBond.name);
    expect(textOf(redeemedRow!)).toContain('Wykupiona');
  });

  it('"Wykup" opens the redeem dialog for a matured bond and reloads after a redemption', async () => {
    await setup([settledTosBond]);
    await switchLanguage(fixture, 'pl');
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = bondListCalls();

    await clickRowMenuItem(fixture, rowFor(settledTosBond.name), /^Wykup$/);

    expect(dialog.open).toHaveBeenCalledWith(
      RedeemBondDialog,
      expect.objectContaining({
        data: { bond: expect.objectContaining({ assetId: settledTosBond.assetId }) },
      }),
    );
    await vi.waitFor(() => expect(bondListCalls()).toBeGreaterThan(callsBefore));
  });

  it('"Zamień" opens the swap dialog for a matured, settled bond', async () => {
    await setup([settledTosBond]);
    await switchLanguage(fixture, 'pl');
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await clickRowMenuItem(fixture, rowFor(settledTosBond.name), /^Zamień$/);

    expect(dialog.open).toHaveBeenCalledWith(
      SwapBondDialog,
      expect.objectContaining({
        data: { bond: expect.objectContaining({ assetId: settledTosBond.assetId }) },
      }),
    );
  });

  it('offers "Wykup" but not "Zamień" on a matured bond with unsettled periods', async () => {
    await setup([unsettledTosBond]);
    await switchLanguage(fixture, 'pl');

    const labels = (await rowMenuItems(fixture, rowFor(unsettledTosBond.name))).map(menuItemLabel);

    expect(labels).toContain('Wykup');
    expect(labels).not.toContain('Zamień');
  });

  it('offers neither "Wykup" nor "Zamień" before maturity', async () => {
    await setup([activeBond]);
    await switchLanguage(fixture, 'pl');

    const labels = (await rowMenuItems(fixture, rowFor(activeBond.name))).map(menuItemLabel);

    expect(labels).not.toContain('Wykup');
    expect(labels).not.toContain('Zamień');
  });

  it('"Wykup" on a bond with unsettled periods opens the settle dialog when the redeem dialog points there', async () => {
    await setup([unsettledTosBond]);
    await switchLanguage(fixture, 'pl');
    dialog.open.mockReturnValueOnce({ afterClosed: () => of('settle') });
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    await clickRowMenuItem(fixture, rowFor(unsettledTosBond.name), /^Wykup$/);

    expect(dialog.open).toHaveBeenNthCalledWith(1, RedeemBondDialog, expect.anything());
    expect(dialog.open).toHaveBeenNthCalledWith(
      2,
      SettleBondInterestDialog,
      expect.objectContaining({
        data: { bond: expect.objectContaining({ assetId: unsettledTosBond.assetId }) },
      }),
    );
  });

  it.each([
    ['an active', activeBond, true],
    ['an interest-due', dueRorBond, true],
    ['a matured', settledTosBond, false],
  ])('offers "Wykup przed terminem" on %s bond', async (_, bond, offered) => {
    await setup([bond]);
    await switchLanguage(fixture, 'pl');

    const labels = (await rowMenuItems(fixture, rowFor(bond.name))).map(menuItemLabel);

    expect(labels.includes('Wykup przed terminem')).toBe(offered);
  });

  it('"Wykup przed terminem" opens the early-redemption dialog and reloads after a redemption', async () => {
    await setup([edoYearOneSettledBond]);
    await switchLanguage(fixture, 'pl');
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = bondListCalls();

    await clickRowMenuItem(fixture, rowFor(edoYearOneSettledBond.name), /^Wykup przed terminem$/);

    expect(dialog.open).toHaveBeenCalledWith(
      EarlyRedeemBondDialog,
      expect.objectContaining({
        data: { bond: expect.objectContaining({ assetId: edoYearOneSettledBond.assetId }) },
      }),
    );
    await vi.waitFor(() => expect(bondListCalls()).toBeGreaterThan(callsBefore));
  });

  it('"Wykup przed terminem" opens the settle dialog when the early-redemption dialog points there', async () => {
    await setup([dueRorBond]);
    await switchLanguage(fixture, 'pl');
    dialog.open.mockReturnValueOnce({ afterClosed: () => of('settle') });
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await clickRowMenuItem(fixture, rowFor(dueRorBond.name), /^Wykup przed terminem$/);

    expect(dialog.open).toHaveBeenNthCalledWith(1, EarlyRedeemBondDialog, expect.anything());
    expect(dialog.open).toHaveBeenNthCalledWith(
      2,
      SettleBondInterestDialog,
      expect.objectContaining({
        data: { bond: expect.objectContaining({ assetId: dueRorBond.assetId }) },
      }),
    );
  });

  it('shows the remaining count and "częściowo wykupiona" after a partial early redemption, and no Edit', async () => {
    await setup([partiallyRedeemedEdoBond, edoYearOneSettledBond]);
    await switchLanguage(fixture, 'pl');

    const partial = textOf(rowFor(partiallyRedeemedEdoBond.name));
    expect(partial).toContain('6');
    expect(partial).toContain('częściowo wykupiona');
    expect(textOf(rowFor(edoYearOneSettledBond.name))).not.toContain('częściowo wykupiona');

    const labels = (await rowMenuItems(fixture, rowFor(partiallyRedeemedEdoBond.name))).map(
      menuItemLabel,
    );
    expect(labels).toContain('Wykup przed terminem');
    expect(labels).not.toContain('Edytuj');
  });

  it('shows the value today gross and net with their totals, and "—" with the reason when there is no estimate', async () => {
    const estimated = {
      ...activeBond,
      estimate: { grossValue: 1061.9, netValue: 1043.6, asOf: '2026-05-13' },
    };
    const secondEstimated = {
      ...interestDueBond,
      estimate: { grossValue: 1006.1, netValue: 1000.05, asOf: '2026-05-13' },
    };
    const rateMissing = {
      ...maturedBond,
      estimateUnavailableReason: BOND_ESTIMATE_UNAVAILABLE_REASON.RateMissing,
    };
    const marketDataDown = {
      ...archivedBond,
      isArchived: false,
      estimateUnavailableReason: BOND_ESTIMATE_UNAVAILABLE_REASON.MarketDataUnavailable,
    };
    await setup([estimated, secondEstimated, rateMissing, marketDataDown]);
    await switchLanguage(fixture, 'pl');

    const root = fixture.nativeElement as HTMLElement;
    const money = (amount: number): string => formatMoney(amount, 'PLN').replace(/\s+/g, ' ');
    const cell = (row: HTMLElement, testId: string): string =>
      textOf(row.querySelector(`[data-testid="${testId}"]`));
    const tooltipsIn = (name: string): string[] =>
      fixture.debugElement
        .queryAll(By.css('[data-testid="estimate-missing"]'))
        .filter((element) => rowFor(name).contains(element.nativeElement as HTMLElement))
        .map((element) => element.injector.get(MatTooltip).message);

    const headers = Array.from(root.querySelectorAll('th')).map(textOf);
    expect(headers).toContain('Wartość dziś (brutto)');
    expect(headers).toContain('Do wypłaty dziś (netto)');
    expect(cell(rowFor(estimated.name), 'gross-today')).toBe(money(1061.9));
    expect(cell(rowFor(estimated.name), 'net-today')).toBe(money(1043.6));
    expect(cell(rowFor(rateMissing.name), 'gross-today')).toBe('—');
    expect(cell(rowFor(rateMissing.name), 'net-today')).toBe('—');
    expect(tooltipsIn(rateMissing.name)).toEqual([
      'Brak stopy MF dla bieżącego okresu',
      'Brak stopy MF dla bieżącego okresu',
    ]);
    expect(tooltipsIn(marketDataDown.name)).toEqual([
      'Wycena chwilowo niedostępna',
      'Wycena chwilowo niedostępna',
    ]);
    expect(textOf(root.querySelector('tfoot, tr.mat-mdc-footer-row'))).toContain('Razem');
    expect(textOf(root.querySelector('[data-testid="gross-today-total"]'))).toBe(money(2068));
    expect(textOf(root.querySelector('[data-testid="net-today-total"]'))).toBe(money(2043.65));
  });
});

describe('Bonds page, current offer tab', () => {
  let fixture: ComponentFixture<Bonds>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  it('"Kup" on an offer row opens the bond purchase dialog with that series picked', async () => {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input) =>
        requestUrl(input).includes('/api/marketdata/bond-series')
          ? jsonResponse(octoberOffer)
          : requestUrl(input).includes('/api/portfolio/bonds')
            ? jsonResponse([])
            : jsonResponse({ detail: 'Not found.' }, 404),
      );
    dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(false) }) };
    await TestBed.configureTestingModule({
      imports: [Bonds],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(Bonds);
    await fixture.whenStable();
    await switchLanguage(fixture, 'pl');

    const offerTab = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="tab"]'),
    ).find((tab) => textOf(tab).includes('Aktualna oferta'));
    offerTab!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    await vi.waitFor(() => {
      const rows = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          'tbody tr.mat-mdc-row',
        ),
      );
      expect(rows.some((row) => textOf(row).includes('EDO1036'))).toBe(true);
    });
    const row = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'),
    ).find((candidate) => textOf(candidate).includes('EDO1036'))!;
    const buy = Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find((button) =>
      textOf(button).includes('Kup'),
    );
    expect(buy).toBeDefined();
    buy!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(
      BondPurchaseDialog,
      expect.objectContaining({
        data: expect.objectContaining({ seriesCode: 'EDO1036' }),
      }),
    );
  });
});

describe('Bonds page, interest settlement', () => {
  let fixture: ComponentFixture<Bonds>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(bonds: BondFixture[]): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      const url = requestUrl(input);
      if (request.method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      if (url.includes('/api/marketdata/bond-series')) {
        return jsonResponse(octoberOffer);
      }
      return url.includes('/api/portfolio/bonds')
        ? jsonResponse(bonds)
        : jsonResponse({ detail: 'Not found.' }, 404);
    });
    dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(true) }) };

    await TestBed.configureTestingModule({
      imports: [Bonds],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Bonds);
    await fixture.whenStable();
    await switchLanguage(fixture, 'pl');
  }

  function button(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((candidate) => textOf(candidate).includes(label));
  }

  function bondListCalls(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        requestUrl(call[0]).includes('/api/portfolio/bonds') &&
        (typeof call[0] === 'string' || (call[0] as Request).method === 'GET'),
    ).length;
  }

  async function expand(bond: BondFixture): Promise<void> {
    const toggle = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      `[data-testid="expand-${bond.assetId}"]`,
    );
    expect(toggle).not.toBeNull();
    toggle!.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('shows "Rozlicz wszystkie" only when a bond has due periods', async () => {
    await setup([activeBond, maturedBond]);

    expect(button('Rozlicz wszystkie')).toBeUndefined();

    fetchSpy.mockRestore();
    TestBed.resetTestingModule();
    await setup([activeBond, dueRorBond]);

    expect(button('Rozlicz wszystkie')).toBeDefined();
  });

  it('"Rozlicz wszystkie" opens the settle-all dialog with the bonds that have due periods and reloads', async () => {
    await setup([activeBond, dueRorBond]);
    const callsBefore = bondListCalls();

    button('Rozlicz wszystkie')!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(
      SettleAllBondsDialog,
      expect.objectContaining({
        data: expect.objectContaining({
          bonds: [expect.objectContaining({ assetId: dueRorBond.assetId })],
        }),
      }),
    );
    await vi.waitFor(() => expect(bondListCalls()).toBeGreaterThan(callsBefore));
  });

  it('the row menu settles one bond through the settle dialog', async () => {
    await setup([dueRorBond]);

    await clickRowMenuItem(
      fixture,
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          'tbody tr.mat-mdc-row',
        ),
      ).find((row) => textOf(row).includes(dueRorBond.name))!,
      /rozlicz/i,
    );

    expect(dialog.open).toHaveBeenCalledWith(
      SettleBondInterestDialog,
      expect.objectContaining({
        data: expect.objectContaining({
          bond: expect.objectContaining({ assetId: dueRorBond.assetId }),
        }),
      }),
    );
  });

  it('expands a row into the period table and offers "Cofnij" on the latest settled period only', async () => {
    await setup([settledRorBond]);

    await expand(settledRorBond);

    // textOf folds the locale's no-break spaces into plain ones, so the expected amounts are folded the same way.
    const table = textOf(fixture.nativeElement);
    const money = (amount: number): string => formatMoney(amount, 'PLN').replace(/\s+/g, ' ');
    expect(table).toContain(money(16.5));
    expect(table).toContain(money(15.5));
    expect(table).toContain(money(3.14));
    const root = fixture.nativeElement as HTMLElement;
    const first = root.querySelector<HTMLElement>('[data-testid="period-row-1"]');
    const second = root.querySelector<HTMLElement>('[data-testid="period-row-2"]');
    expect(first).not.toBeNull();
    expect(second).not.toBeNull();
    expect(textOf(first)).not.toContain('Cofnij');
    expect(textOf(second)).toContain('Cofnij');
  });

  it('"Cofnij" asks for confirmation, then deletes the latest settlement and reloads', async () => {
    await setup([settledRorBond]);
    await expand(settledRorBond);
    const callsBefore = bondListCalls();

    const undo = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
        '[data-testid="period-row-2"] button',
      ),
    ).find((candidate) => textOf(candidate).includes('Cofnij'))!;
    undo.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    await vi.waitFor(() => {
      const deleteCall = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .find((request: Request) => request.method === 'DELETE');
      expect(deleteCall?.url).toContain(
        `/api/portfolio/portfolios/${settledRorBond.portfolioId}/bonds/${settledRorBond.assetId}/interest-settlements/${settledRorBond.lastSettlement!.settlementId}`,
      );
    });
    await vi.waitFor(() => expect(bondListCalls()).toBeGreaterThan(callsBefore));
  });
});
