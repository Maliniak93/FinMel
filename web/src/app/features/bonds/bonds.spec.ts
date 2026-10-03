import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { clickRowMenuItem, showArchived as showArchivedToggle } from '../../../testing/archive';
import {
  activeBond,
  archivedBond,
  interestDueBond,
  maturedBond,
  octoberOffer,
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
