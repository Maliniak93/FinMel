import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { clickRowMenuItem, showArchived as showArchivedToggle } from '../../../../testing/archive';
import {
  activeBond,
  archivedBond,
  BOND_ESTIMATE_UNAVAILABLE_REASON,
  dueRorBond,
  fullyRedeemedGainBond,
  gainCoiBond,
  gainCoiNoCouponBond,
  gainEdo0736Bond,
  gainEdoBond,
  interestDueBond,
  maturedBond,
  noRateGainBond,
  partiallyRedeemedEdoBond,
  partiallyRedeemedGainBond,
  settledRorBond,
  settledTosBond,
  type BondFixture,
} from '../../../../testing/bond-fixtures';
import { restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../../shared/format';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
import { EarlyRedeemBondDialog } from '../early-redeem-bond-dialog/early-redeem-bond-dialog';
import { RedeemBondDialog } from '../redeem-bond-dialog/redeem-bond-dialog';
import { SettleBondInterestDialog } from '../settle-bond-interest-dialog/settle-bond-interest-dialog';
import { SwapBondDialog } from '../swap-bond-dialog/swap-bond-dialog';
import { MyBonds } from './my-bonds';

const collapse = (text: string): string => text.replace(/\s+/g, ' ').trim();

describe('My bonds', () => {
  let fixture: ComponentFixture<MyBonds>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(bonds: BondFixture[] | 'pending' | 'failed' = [activeBond]): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      if (!requestUrl(input).includes('/api/portfolio/bonds')) {
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (bonds === 'pending') {
        return new Promise<Response>(() => undefined);
      }
      if (bonds === 'failed') {
        return jsonResponse({ detail: 'Service unavailable.' }, 503);
      }
      return jsonResponse(bonds);
    });
    dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(false) }) };

    await TestBed.configureTestingModule({
      imports: [MyBonds],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyBonds);
    if (bonds !== 'pending') {
      await fixture.whenStable();
    }
    fixture.detectChanges();
  }

  function rows(): HTMLElement[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'),
    );
  }

  function rowFor(name: string): HTMLElement {
    const row = rows().find((candidate) => textOf(candidate).includes(name));
    if (!row) {
      throw new Error(`No row for '${name}'.`);
    }
    return row;
  }

  function chipsOf(row: HTMLElement): string[] {
    return Array.from(row.querySelectorAll('app-status-chip'), (chip) => textOf(chip));
  }

  function spaced(text: string): string {
    return collapse(text).replace(/(\S)\(/, '$1 (');
  }

  function gainIn(row: HTMLElement, testId: string): string {
    return spaced(textOf(row.querySelector(`[data-testid="${testId}"] app-gain`)));
  }

  async function showRedeemedToggle(): Promise<void> {
    const toggle = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('mat-slide-toggle'),
    ).find((candidate) => /wykupion/i.test(textOf(candidate)));
    if (!toggle) {
      throw new Error('No "Pokaż wykupione" slide toggle rendered.');
    }
    toggle.querySelector<HTMLElement>('button')?.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('chips and estimate', async () => {
    const estimated = {
      ...activeBond,
      estimate: { grossValue: 1061.9, netValue: 1043.6, asOf: '2026-05-13' },
    };
    const rateMissing = {
      ...maturedBond,
      estimateUnavailableReason: BOND_ESTIMATE_UNAVAILABLE_REASON.RateMissing,
    };
    await setup([estimated, interestDueBond, partiallyRedeemedEdoBond, rateMissing, archivedBond]);
    await switchLanguage(fixture, 'pl');
    await showArchivedToggle(fixture);

    expect(
      chipsOf(rowFor(interestDueBond.name)).some((chip) => chip.includes('Odsetki do rozliczenia')),
    ).toBe(true);
    expect(
      chipsOf(rowFor(partiallyRedeemedEdoBond.name)).some((chip) =>
        /częściowo wykupion/i.test(chip),
      ),
    ).toBe(true);
    expect(chipsOf(rowFor(archivedBond.name)).some((chip) => chip.includes('Zarchiwizowany'))).toBe(
      true,
    );

    const money = (amount: number): string => formatMoney(amount, 'PLN').replace(/\s+/g, ' ');
    const cell = (row: HTMLElement, testId: string): string =>
      textOf(row.querySelector(`[data-testid="${testId}"]`));
    expect(
      textOf(rowFor(estimated.name).querySelector('[data-testid="gross-today"] app-money')),
    ).toBe(money(1061.9));
    expect(cell(rowFor(rateMissing.name), 'gross-today')).toBe('—');
    expect(cell(rowFor(rateMissing.name), 'net-today')).toBe('—');
    expect(
      fixture.debugElement
        .queryAll(By.css('[data-testid="estimate-missing"]'))
        .filter((element) =>
          rowFor(rateMissing.name).contains(element.nativeElement as HTMLElement),
        )
        .map((element) => element.injector.get(MatTooltip).message),
    ).toEqual(['Brak stopy MF dla bieżącego okresu', 'Brak stopy MF dla bieżącego okresu']);
  });

  it('gross gain', async () => {
    await setup([gainEdoBond, gainCoiBond]);
    await switchLanguage(fixture, 'pl');

    expect(gainIn(rowFor(gainEdoBond.name), 'gross-today')).toBe('+147,70 zł (+4,22 %)');
    expect(gainIn(rowFor(gainCoiBond.name), 'gross-today')).toBe('+65,00 zł (+6,50 %)');
  });

  it('net gain', async () => {
    await setup([gainCoiBond, gainEdo0736Bond]);
    await switchLanguage(fixture, 'pl');

    expect(gainIn(rowFor(gainCoiBond.name), 'net-today')).toBe('+40,00 zł (+4,00 %)');
    const zero = rowFor(gainEdo0736Bond.name).querySelector('[data-testid="net-today"] app-gain');
    expect(spaced(textOf(zero))).toBe('0,00 zł (0,00 %)');
    expect(zero?.querySelector('.gain--zero')).not.toBeNull();
    expect(zero?.querySelector('.gain--positive, .gain--negative')).toBeNull();
  });

  it('gain edge cases', async () => {
    await setup([partiallyRedeemedGainBond, noRateGainBond, fullyRedeemedGainBond]);
    await switchLanguage(fixture, 'pl');
    await showRedeemedToggle();

    expect(gainIn(rowFor(partiallyRedeemedGainBond.name), 'gross-today')).toBe(
      '+32,10 zł (+5,35 %)',
    );
    for (const bond of [noRateGainBond, fullyRedeemedGainBond]) {
      const row = rowFor(bond.name);
      for (const testId of ['gross-today', 'net-today']) {
        expect(row.querySelector(`[data-testid="${testId}"] app-gain`)).toBeNull();
        expect(textOf(row.querySelector(`[data-testid="${testId}"]`))).toBe('—');
      }
    }
  });

  it('total gain', async () => {
    await setup([gainEdoBond, gainCoiNoCouponBond, noRateGainBond]);
    await switchLanguage(fixture, 'pl');

    const root = fixture.nativeElement as HTMLElement;
    const grossTotal = root.querySelector('[data-testid="gross-today-total"]');
    const netTotal = root.querySelector('[data-testid="net-today-total"]');
    expect(collapse(textOf(grossTotal?.querySelector('app-money') ?? null))).toBe(
      collapse(formatMoney(5663.9, 'PLN')),
    );
    expect(spaced(textOf(grossTotal?.querySelector('app-gain') ?? null))).toBe(
      '+163,90 zł (+2,98 %)',
    );
    expect(textOf(grossTotal)).toContain('bez 1 pozycji');
    expect(collapse(textOf(netTotal?.querySelector('app-money') ?? null))).toBe(
      collapse(formatMoney(5542.7, 'PLN')),
    );
    expect(spaced(textOf(netTotal?.querySelector('app-gain') ?? null))).toBe('+42,70 zł (+0,78 %)');
    expect(textOf(netTotal)).toContain('bez 1 pozycji');
  });

  it('row menu', async () => {
    const steps: [BondFixture, RegExp, unknown][] = [
      [dueRorBond, /rozlicz/i, SettleBondInterestDialog],
      [settledTosBond, /^Wykup$/, RedeemBondDialog],
      [settledTosBond, /^Zamień$/, SwapBondDialog],
      [activeBond, /^Wykup przed terminem$/, EarlyRedeemBondDialog],
      [settledRorBond, /^Cofnij$/, ConfirmDialog],
      [activeBond, /^Usuń$/, ConfirmDialog],
    ];

    for (const [bond, label, dialogType] of steps) {
      fetchSpy.mockRestore();
      TestBed.resetTestingModule();
      await setup([activeBond, dueRorBond, settledTosBond, settledRorBond]);
      await switchLanguage(fixture, 'pl');

      await clickRowMenuItem(fixture, rowFor(bond.name), label);

      expect(dialog.open).toHaveBeenLastCalledWith(dialogType, expect.anything());
    }
  });

  it('states', async () => {
    await setup('pending');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-skeleton')).not.toBeNull();
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup('failed');
    const errorBlock = (fixture.nativeElement as HTMLElement).querySelector('app-section-error');
    expect(errorBlock?.textContent).toContain('Retry');
    await switchLanguage(fixture, 'pl');
    expect(errorBlock?.textContent).toContain('Ponów');
    fetchSpy.mockRestore();
    await restoreEnglish();
    TestBed.resetTestingModule();

    await setup([]);
    const empty = (fixture.nativeElement as HTMLElement).querySelector('app-empty-state');
    expect(empty?.textContent).toContain('No bonds yet.');
    expect(empty?.querySelector('button')).not.toBeNull();
  });
});
