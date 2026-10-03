import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { SavingsAccountResponse } from '../../../api/portfolio';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../../shared/format';
import {
  clickRowMenuItem,
  menuItemLabel,
  showArchived as showArchivedToggle,
} from '../../../../testing/archive';
import { restoreEnglish } from '../../../../testing/i18n';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
import { SavingsAccountFormDialog } from '../savings-account-form-dialog/savings-account-form-dialog';
import { SavingsTransferDialog } from '../savings-transfer-dialog/savings-transfer-dialog';
import { SettleSavingsInterestDialog } from '../settle-savings-interest-dialog/settle-savings-interest-dialog';
import {
  activeSavingsAccount,
  archivedPortfolioSavingsAccount,
  archivedPortfolioDueSavingsAccount,
  archivedSavingsAccount,
  dueSavingsAccount,
  manyMonthsDueSavingsAccount,
  settledSavingsAccount,
  savingsAccountResponse,
  taxFreeEurSavingsAccount,
} from '../testing/savings-account-fixtures';
import { SavingsAccounts } from './savings-accounts';

// savings-accounts AC-11 / AC-12. The "Savings accounts" tab of the Deposits & savings page lists
// every savings account of the user across portfolios from GET /api/portfolio/savings-accounts.
describe('SavingsAccounts', () => {
  let fixture: ComponentFixture<SavingsAccounts>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  const defaultAccounts = [
    activeSavingsAccount,
    taxFreeEurSavingsAccount,
    archivedPortfolioSavingsAccount,
  ];

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await restoreEnglish();
  });

  async function setup(accounts: SavingsAccountResponse[] = defaultAccounts): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      // asset-archive: the archive / restore endpoints answer 200 with the asset.
      if (request.method === 'POST' && /\/assets\/[^/]+\/(archive|restore)$/.test(request.url)) {
        return jsonResponse({});
      }
      return requestUrl(input).includes('/api/portfolio/savings-accounts')
        ? jsonResponse(accounts)
        : jsonResponse({ detail: 'Not found.' }, 404);
    });
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [SavingsAccounts],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SavingsAccounts);
    await fixture.whenStable();
  }

  function rows(): HTMLElement[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'),
    );
  }

  function rowFor(name: string): HTMLElement {
    const row = rows().find((r) => (r.textContent ?? '').includes(name));
    if (!row) {
      throw new Error(`No row for '${name}'.`);
    }
    return row;
  }

  function listCalls(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        (typeof call[0] === 'string' ? 'GET' : (call[0] as Request).method) === 'GET' &&
        requestUrl(call[0]).includes('/api/portfolio/savings-accounts'),
    ).length;
  }

  function writes(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method !== 'GET');
  }

  // Opens a row's actions menu and returns the labels of the items it offers, icons left out.
  async function menuItemLabels(row: HTMLElement): Promise<string[]> {
    const trigger = row.querySelector<HTMLButtonElement>('button[aria-label^="Actions for"]');
    if (!trigger) {
      throw new Error('No actions menu on this row.');
    }
    trigger.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const labels = Array.from(
      TestBed.inject(OverlayContainer).getContainerElement().querySelectorAll('.mat-mdc-menu-item'),
      menuItemLabel,
    );
    return labels;
  }

  it('lists every account with its bank, portfolio, balance and rate', async () => {
    await setup();

    expect(rows()).toHaveLength(3);
    for (const account of [activeSavingsAccount, taxFreeEurSavingsAccount]) {
      const text = rowFor(account.name).textContent ?? '';
      expect(text).toContain(account.bankName!);
      expect(text).toContain(account.portfolioName);
      // The balance is shown in the account's own currency.
      expect(text).toContain(formatMoney(account.balance, account.currency));
    }
    expect(rowFor('Emergency fund').textContent).toMatch(/4[.,]5\s*%/);
    expect(rowFor('IKE savings').textContent).toMatch(/3\s*%/);
  });

  it('marks only a tax-exempt account as "Tax-free"', async () => {
    await setup();

    expect(rowFor('IKE savings').textContent).toContain('Tax-free');
    expect(rowFor('Emergency fund').textContent).not.toContain('Tax-free');
  });

  it('links the account name to the asset transactions view', async () => {
    await setup();

    const link = rowFor('Emergency fund').querySelector<HTMLAnchorElement>('a');

    expect(link?.textContent).toContain('Emergency fund');
    expect(link?.getAttribute('href')).toBe(
      `/portfolios/${activeSavingsAccount.portfolioId}/assets/${activeSavingsAccount.assetId}/transactions`,
    );
  });

  it('hides every action on an account of an archived portfolio', async () => {
    await setup();

    expect(rowFor('Frozen account').querySelectorAll('button')).toHaveLength(0);
    expect(rowFor('Emergency fund').querySelectorAll('button').length).toBeGreaterThan(0);
  });

  it('Add opens the savings-account form for a new account and reloads after a save', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const listCallsBefore = listCalls();

    const add = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((button) => /add savings account/i.test(button.textContent ?? ''));
    expect(add).toBeDefined();
    add!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    const [dialogType, config] = dialog.open.mock.calls[0] as [
      unknown,
      { data?: { account?: unknown } },
    ];
    expect(dialogType).toBe(SavingsAccountFormDialog);
    expect(config?.data?.account).toBeUndefined();
    await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(listCallsBefore));
  });

  it('a cancelled Add does not reload the list', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(undefined) });
    const listCallsBefore = listCalls();

    const add = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((button) => /add savings account/i.test(button.textContent ?? ''));
    add!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(listCalls()).toBe(listCallsBefore);
  });

  it('Edit opens the savings-account form with that account and reloads after a save', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const listCallsBefore = listCalls();

    await clickRowMenuItem(fixture, rowFor('Emergency fund'), /edit/i);

    expect(dialog.open).toHaveBeenCalledWith(
      SavingsAccountFormDialog,
      expect.objectContaining({
        data: expect.objectContaining({ account: activeSavingsAccount }),
      }),
    );
    await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(listCallsBefore));
  });

  it('Delete asks for confirmation, then removes the asset and reloads', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const listCallsBefore = listCalls();

    await clickRowMenuItem(fixture, rowFor('Emergency fund'), /delete/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    await vi.waitFor(() => expect(writes()).toHaveLength(1));
    const [request] = writes();
    expect(request.method).toBe('DELETE');
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${activeSavingsAccount.portfolioId}/assets/${activeSavingsAccount.assetId}`,
    );
    await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(listCallsBefore));
  });

  it('does not delete when the confirmation is cancelled', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await clickRowMenuItem(fixture, rowFor('Emergency fund'), /delete/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    expect(writes()).toHaveLength(0);
  });

  it('shows an empty state with an Add button when there are no accounts', async () => {
    await setup([]);

    expect(rows()).toHaveLength(0);
    const buttons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
      (button) => button.textContent ?? '',
    );
    expect(buttons.some((text) => /add/i.test(text))).toBe(true);
  });

  // asset-archive: an account can be archived on its own — "Show archived" (off by default) reveals
  // it with an "Archived" chip, the row menu gains Archive / Restore behind a ConfirmDialog, and an
  // archived row keeps only Restore and Delete.
  describe('archive', () => {
    it('hides archived accounts until Show archived is on', async () => {
      await setup([activeSavingsAccount, archivedSavingsAccount]);

      expect(rows()).toHaveLength(1);
      expect((fixture.nativeElement as HTMLElement).textContent).not.toContain(
        archivedSavingsAccount.name,
      );

      await showArchivedToggle(fixture);

      expect(rows()).toHaveLength(2);
      const row = rowFor(archivedSavingsAccount.name);
      expect(row.textContent).toContain('Archived');
      expect(row.querySelector('mat-chip, mat-chip-option, .mat-mdc-chip')).not.toBeNull();
      expect(rowFor('Emergency fund').textContent).not.toContain('Archived');
    });

    it('archives an account after confirmation', async () => {
      await setup([activeSavingsAccount]);
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      const listCallsBefore = listCalls();

      await clickRowMenuItem(fixture, rowFor('Emergency fund'), /\barchive\b/i);

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      await vi.waitFor(() => expect(writes()).toHaveLength(1));
      const [request] = writes();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${activeSavingsAccount.portfolioId}/assets/${activeSavingsAccount.assetId}/archive`,
      );
      await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(listCallsBefore));
    });

    it('does not archive when the confirmation is cancelled', async () => {
      await setup([activeSavingsAccount]);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });

      await clickRowMenuItem(fixture, rowFor('Emergency fund'), /\barchive\b/i);

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      expect(writes()).toHaveLength(0);
    });

    it('restores an archived account after confirmation', async () => {
      await setup([archivedSavingsAccount]);
      await showArchivedToggle(fixture);
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      const listCallsBefore = listCalls();

      await clickRowMenuItem(fixture, rowFor(archivedSavingsAccount.name), /restore/i);

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      await vi.waitFor(() => expect(writes()).toHaveLength(1));
      const [request] = writes();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${archivedSavingsAccount.portfolioId}/assets/${archivedSavingsAccount.assetId}/restore`,
      );
      await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(listCallsBefore));
    });

    it('does not restore when the confirmation is cancelled', async () => {
      await setup([archivedSavingsAccount]);
      await showArchivedToggle(fixture);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });

      await clickRowMenuItem(fixture, rowFor(archivedSavingsAccount.name), /restore/i);

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      expect(writes()).toHaveLength(0);
    });

    it('shows only Restore and Delete for an archived account', async () => {
      await setup([archivedSavingsAccount]);
      await showArchivedToggle(fixture);

      const labels = await menuItemLabels(rowFor(archivedSavingsAccount.name));

      expect(labels.some((label) => /restore/i.test(label))).toBe(true);
      expect(labels.some((label) => /delete/i.test(label))).toBe(true);
      expect(labels.some((label) => /edit/i.test(label))).toBe(false);
      expect(labels.some((label) => /\barchive\b/i.test(label))).toBe(false);
    });

    it('offers Archive, not Restore, for an active account', async () => {
      await setup([savingsAccountResponse()]);

      const labels = await menuItemLabels(rowFor('Savings account'));

      expect(labels.some((label) => /\barchive\b/i.test(label))).toBe(true);
      expect(labels.some((label) => /restore/i.test(label))).toBe(false);
      expect(labels.some((label) => /edit/i.test(label))).toBe(true);
      expect(labels.some((label) => /delete/i.test(label))).toBe(true);
    });
  });

  // savings-interest-settlement AC-13. A Due row (`interestDue`) carries a `task_alt` icon button with
  // the tooltip "Settle interest" — "Settle interest (n months due)" when `duePeriodCount` is above 1 —
  // that opens the settle dialog and reloads the list after a success. A "Last interest" column shows
  // the latest settled month and its net ("—" when none); that latest settlement has an undo icon
  // ("Undo last settlement") behind the same ConfirmDialog as Delete. A row of an archived portfolio
  // has neither icon.
  describe('interest settlement', () => {
    function iconButton(row: HTMLElement, pattern: RegExp): HTMLButtonElement | undefined {
      return Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find(
        (button) =>
          pattern.test(button.getAttribute('mattooltip') ?? '') ||
          pattern.test(button.getAttribute('aria-label') ?? ''),
      );
    }

    const settleButton = (row: HTMLElement) => iconButton(row, /settle interest/i);
    const undoButton = (row: HTMLElement) => iconButton(row, /undo last settlement/i);

    // The tooltip's message as the MatTooltip directive on the row's button holds it.
    function tooltipMessages(row: HTMLElement): string[] {
      return fixture.debugElement
        .queryAll(By.directive(MatTooltip))
        .filter((debugElement) => row.contains(debugElement.nativeElement))
        .map((debugElement) => debugElement.injector.get(MatTooltip).message);
    }

    it('only a Due row carries the settle icon', async () => {
      await setup([dueSavingsAccount, settledSavingsAccount, activeSavingsAccount]);

      expect(settleButton(rowFor('Interest due account'))).toBeDefined();
      expect(settleButton(rowFor('Settled account'))).toBeUndefined();
      expect(settleButton(rowFor('Emergency fund'))).toBeUndefined();
      expect(rowFor('Interest due account').textContent).toContain('task_alt');
    });

    it('the settle tooltip says "Settle interest", plus the months due when there are several', async () => {
      await setup([dueSavingsAccount, manyMonthsDueSavingsAccount]);

      expect(tooltipMessages(rowFor('Interest due account'))).toContain('Settle interest');
      expect(tooltipMessages(rowFor('Three months due'))).toContain(
        'Settle interest (3 months due)',
      );
    });

    it('Settle opens the settle dialog with that account and reloads after a successful settle', async () => {
      await setup([dueSavingsAccount, activeSavingsAccount]);
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      const listCallsBefore = listCalls();

      settleButton(rowFor('Interest due account'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(
        SettleSavingsInterestDialog,
        expect.objectContaining({ data: expect.objectContaining({ account: dueSavingsAccount }) }),
      );
      await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(listCallsBefore));
    });

    it('a cancelled settle does not reload the list', async () => {
      await setup([dueSavingsAccount]);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      const listCallsBefore = listCalls();

      settleButton(rowFor('Interest due account'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(SettleSavingsInterestDialog, expect.anything());
      expect(listCalls()).toBe(listCallsBefore);
    });

    it('the "Last interest" column shows the latest settled month and its net, or a dash', async () => {
      await setup([settledSavingsAccount, dueSavingsAccount]);

      const headers = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll('th'),
        (header) => header.textContent?.trim() ?? '',
      );
      expect(headers).toContain('Last interest');

      const settled = (rowFor('Settled account').textContent ?? '').replace(/\s+/g, ' ');
      expect(settled).toMatch(/Sep(tember)? 2026/);
      expect(settled).toContain(formatMoney(33.29, 'PLN').replace(/\s+/g, ' '));
      expect(rowFor('Interest due account').textContent).toContain('—');
    });

    it('the latest settlement has an undo icon, and only it does', async () => {
      await setup([settledSavingsAccount, dueSavingsAccount, activeSavingsAccount]);

      expect(undoButton(rowFor('Settled account'))).toBeDefined();
      expect(tooltipMessages(rowFor('Settled account'))).toContain('Undo last settlement');
      expect(undoButton(rowFor('Interest due account'))).toBeUndefined();
      expect(undoButton(rowFor('Emergency fund'))).toBeUndefined();
    });

    it('Undo asks for confirmation, then deletes the settlement and reloads', async () => {
      await setup([settledSavingsAccount]);
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      const listCallsBefore = listCalls();

      undoButton(rowFor('Settled account'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      await vi.waitFor(() => expect(writes()).toHaveLength(1));
      const [request] = writes();
      expect(request.method).toBe('DELETE');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${settledSavingsAccount.portfolioId}/savings-accounts/${settledSavingsAccount.assetId}/interest-settlements/${settledSavingsAccount.lastSettlement!.settlementId}`,
      );
      await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(listCallsBefore));
    });

    it('does not undo when the confirmation is cancelled', async () => {
      await setup([settledSavingsAccount]);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });

      undoButton(rowFor('Settled account'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      expect(writes()).toHaveLength(0);
    });

    it('a row of an archived portfolio has neither the settle nor the undo icon', async () => {
      await setup([archivedPortfolioDueSavingsAccount]);

      const row = rowFor('Frozen due account');
      expect(settleButton(row)).toBeUndefined();
      expect(undoButton(row)).toBeUndefined();
    });
  });

  // savings-cash-transfers AC-8: each active savings row has a "Transfer" icon button (`swap_horiz`)
  // that opens the SavingsTransferDialog with that account and reloads the list after a success; a row
  // of an archived portfolio has none.
  describe('transfer', () => {
    const transferButton = (row: HTMLElement) =>
      Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find(
        (button) =>
          /transfer/i.test(button.getAttribute('mattooltip') ?? '') ||
          /transfer/i.test(button.getAttribute('aria-label') ?? ''),
      );

    it('every active row carries the Transfer icon, an archived-portfolio row does not', async () => {
      await setup();

      for (const name of ['Emergency fund', 'IKE savings']) {
        expect(transferButton(rowFor(name))).toBeDefined();
        expect(rowFor(name).textContent).toContain('swap_horiz');
      }
      expect(transferButton(rowFor('Frozen account'))).toBeUndefined();
    });

    it('opens the transfer dialog with that account and reloads after a successful transfer', async () => {
      await setup();
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      const listCallsBefore = listCalls();

      transferButton(rowFor('Emergency fund'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(
        SavingsTransferDialog,
        expect.objectContaining({
          data: expect.objectContaining({ account: activeSavingsAccount }),
        }),
      );
      await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(listCallsBefore));
    });

    it('a cancelled transfer does not reload the list', async () => {
      await setup();
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      const listCallsBefore = listCalls();

      transferButton(rowFor('Emergency fund'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(SavingsTransferDialog, expect.anything());
      expect(listCalls()).toBe(listCallsBefore);
    });
  });
});
