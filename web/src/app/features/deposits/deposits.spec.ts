import { OverlayContainer } from '@angular/cdk/overlay';
import { formatDate } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import {
  attributesOf,
  labelsOf,
  matchesTranslation,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  textOf,
} from '../../../testing/i18n';
import {
  clickRowMenuItem,
  menuItemLabel,
  showArchived as showArchivedToggle,
} from '../../../testing/archive';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { DepositResponse } from '../../api/portfolio';
import { formatMoney } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { DepositFormDialog } from './deposit-form-dialog/deposit-form-dialog';
import { Deposits } from './deposits';
import { PayOutDepositDialog } from './pay-out-deposit-dialog/pay-out-deposit-dialog';
import { RollOverDepositDialog } from './roll-over-deposit-dialog/roll-over-deposit-dialog';
import { SettleDepositDialog } from './settle-deposit-dialog/settle-deposit-dialog';
import {
  activeDeposit,
  archivedDueDeposit,
  archivedPortfolioDeposit,
  archivedSettledDeposit,
  dueDeposit,
  paidOutDeposit,
  paidOutDepositDestinationName,
  paidOutDepositWithRemovedDestination,
  paidOutIntoSavingsDeposit,
  plnSavingsCandidate,
  settledDeposit,
  settledDepositFinalAmount,
} from './testing/deposit-fixtures';
import { activeSavingsAccount, taxFreeEurSavingsAccount } from './testing/savings-account-fixtures';
import { provideI18nTesting } from '../../core/i18n/testing';

// term-deposits AC-15. The Deposits page (route `deposits`) lists every deposit of the user across
// portfolios from GET /api/portfolio/deposits, with its projection and an Active / Due status chip.
describe('Deposits', () => {
  let fixture: ComponentFixture<Deposits>;
  let component: Deposits;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await restoreEnglish();
  });

  async function setup(
    deposits: DepositResponse[] = [activeDeposit, dueDeposit, archivedPortfolioDeposit],
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      // asset-archive: the archive / restore endpoints answer 200 with the asset.
      if (request.method === 'POST' && /\/assets\/[^/]+\/(archive|restore)$/.test(request.url)) {
        return jsonResponse({});
      }
      if (requestUrl(input).includes('/api/portfolio/savings-accounts')) {
        return jsonResponse([activeSavingsAccount, taxFreeEurSavingsAccount]);
      }
      return requestUrl(input).includes('/api/portfolio/deposits')
        ? jsonResponse(deposits)
        : jsonResponse({ detail: 'Not found.' }, 404);
    });
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Deposits],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Deposits);
    component = fixture.componentInstance;
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

  function depositListCalls(): number {
    return fetchSpy.mock.calls.filter((call: unknown[]) =>
      requestUrl(call[0]).includes('/api/portfolio/deposits'),
    ).length;
  }

  // term-deposits-settlement: the settle action is an icon button on the row itself, recognised by
  // its "Settle maturity" tooltip (or an aria-label saying the same).
  function settleButton(row: HTMLElement): HTMLButtonElement | undefined {
    return Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find(
      (button) =>
        /settle/i.test(button.getAttribute('mattooltip') ?? '') ||
        /settle/i.test(button.getAttribute('aria-label') ?? ''),
    );
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
    return Array.from(
      TestBed.inject(OverlayContainer)
        .getContainerElement()
        .querySelectorAll('[mat-menu-item], .mat-mdc-menu-item'),
      menuItemLabel,
    );
  }

  // savings-accounts AC-11. The page is titled "Deposits & savings" and is a tab group: "Term deposits"
  // holds the term-deposit table (everything below), "Savings accounts" the savings accounts.
  describe('tabs', () => {
    function tabs(): HTMLElement[] {
      return Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="tab"]'),
      );
    }

    function tabFor(label: RegExp): HTMLElement {
      const tab = tabs().find((candidate) => label.test(candidate.textContent ?? ''));
      if (!tab) {
        throw new Error(`No tab matching ${label}.`);
      }
      return tab;
    }

    it('is titled "Deposits & savings"', async () => {
      await setup();

      expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toContain(
        'Deposits & savings',
      );
    });

    it('renders the Term deposits and Savings accounts tabs', async () => {
      await setup();

      expect(tabs()).toHaveLength(2);
      expect(tabFor(/term deposits/i)).toBeDefined();
      expect(tabFor(/savings accounts/i)).toBeDefined();
    });

    it('opens on Term deposits, which lists the term deposits', async () => {
      await setup();

      expect(tabFor(/term deposits/i).getAttribute('aria-selected')).toBe('true');
      expect(tabFor(/savings accounts/i).getAttribute('aria-selected')).toBe('false');
      expect(rows()).toHaveLength(3);
      expect((fixture.nativeElement as HTMLElement).textContent).not.toContain(
        activeSavingsAccount.name,
      );
    });

    it('the Savings accounts tab lists the savings accounts', async () => {
      await setup();

      tabFor(/savings accounts/i).click();
      fixture.detectChanges();
      await fixture.whenStable();

      await vi.waitFor(() => {
        const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
        expect(text).toContain(activeSavingsAccount.name);
        expect(text).toContain(taxFreeEurSavingsAccount.name);
      });
      expect(tabFor(/savings accounts/i).getAttribute('aria-selected')).toBe('true');
      expect(
        fetchSpy.mock.calls.some((call: unknown[]) =>
          requestUrl(call[0]).includes('/api/portfolio/savings-accounts'),
        ),
      ).toBe(true);
    });
  });

  it('lists every deposit with its terms and projection columns', async () => {
    await setup();

    expect(rows()).toHaveLength(3);
    for (const deposit of [activeDeposit, dueDeposit]) {
      const text = rowFor(deposit.name).textContent ?? '';
      expect(text).toContain(deposit.bankName!);
      expect(text).toContain(deposit.portfolioName);
      expect(text).toContain(formatMoney(deposit.principal, deposit.currency));
      expect(text).toContain(formatMoney(deposit.projection.netInterest, deposit.currency));
      expect(text).toContain(formatMoney(deposit.projection.finalAmount, deposit.currency));
      expect(text).toContain(formatDate(`${deposit.maturityDate}T00:00:00`, 'mediumDate', 'en-US'));
      expect(text).toContain(formatDate(`${deposit.startDate}T00:00:00`, 'mediumDate', 'en-US'));
    }
    expect(rowFor('Running deposit').textContent).toMatch(/5[.,]5\s*%/);
  });

  it('the Due row carries the "Due" chip and the Active row does not', async () => {
    await setup();

    const dueChip = rowFor('Matured deposit').querySelector(
      'mat-chip, mat-chip-option, .mat-mdc-chip',
    );
    expect(dueChip?.textContent).toContain('Due');
    expect(rowFor('Running deposit').textContent).not.toContain('Due');
    expect(rowFor('Running deposit').textContent).toContain('Active');
  });

  it('Add opens the deposit form for a new deposit and reloads after a save', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const listCallsBefore = depositListCalls();

    const add = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((button) => /add/i.test(button.textContent ?? ''));
    expect(add).toBeDefined();
    add!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    const [dialogType, config] = dialog.open.mock.calls[0] as [
      unknown,
      { data?: { deposit?: unknown } },
    ];
    expect(dialogType).toBe(DepositFormDialog);
    expect(config?.data?.deposit).toBeUndefined();
    await vi.waitFor(() => expect(depositListCalls()).toBeGreaterThan(listCallsBefore));
  });

  it('Edit opens the deposit form with that deposit and reloads after a save', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const listCallsBefore = depositListCalls();

    component['openEditDialog'](dueDeposit);
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(
      DepositFormDialog,
      expect.objectContaining({ data: expect.objectContaining({ deposit: dueDeposit }) }),
    );
    await vi.waitFor(() => expect(depositListCalls()).toBeGreaterThan(listCallsBefore));
  });

  it('Delete asks for confirmation, then removes the asset and reloads', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const listCallsBefore = depositListCalls();

    await component['remove'](dueDeposit);
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    const deleteCall = fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .find((request: Request) => request.method === 'DELETE');
    expect(deleteCall?.url).toContain(
      `/api/portfolio/portfolios/${dueDeposit.portfolioId}/assets/${dueDeposit.assetId}`,
    );
    await vi.waitFor(() => expect(depositListCalls()).toBeGreaterThan(listCallsBefore));
  });

  it('does not delete when the confirmation is cancelled', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await component['remove'](dueDeposit);

    expect(
      fetchSpy.mock.calls.some((call: unknown[]) => (call[0] as Request).method === 'DELETE'),
    ).toBe(false);
  });

  it('hides Edit / Delete on a deposit of an archived portfolio', async () => {
    await setup();

    expect(rowFor('Archived deposit').querySelectorAll('button')).toHaveLength(0);
    expect(rowFor('Running deposit').querySelectorAll('button').length).toBeGreaterThan(0);
  });

  // term-deposits-settlement AC-10.
  it('only the Due row carries the settle button', async () => {
    await setup([activeDeposit, dueDeposit, settledDeposit]);

    expect(settleButton(rowFor('Matured deposit'))).toBeDefined();
    expect(settleButton(rowFor('Running deposit'))).toBeUndefined();
    expect(settleButton(rowFor('Settled deposit'))).toBeUndefined();
  });

  it('the Settled row shows the "Settled" chip and the settled final amount', async () => {
    await setup([activeDeposit, dueDeposit, settledDeposit]);

    const row = rowFor('Settled deposit');
    const chip = row.querySelector('mat-chip, mat-chip-option, .mat-mdc-chip');
    expect(chip?.textContent).toContain('Settled');
    expect(row.textContent).not.toContain('Due');
    expect(row.textContent).toContain(
      formatMoney(settledDepositFinalAmount, settledDeposit.currency),
    );
    expect(row.textContent).not.toContain(
      formatMoney(settledDeposit.projection.finalAmount, settledDeposit.currency),
    );
  });

  it('the Settled row offers Delete but not Edit', async () => {
    await setup([settledDeposit]);

    const labels = await menuItemLabels(rowFor('Settled deposit'));

    expect(labels.some((label) => /delete/i.test(label))).toBe(true);
    expect(labels.some((label) => /edit/i.test(label))).toBe(false);
  });

  it('the Due row still offers Edit and Delete', async () => {
    await setup([dueDeposit]);

    const labels = await menuItemLabels(rowFor('Matured deposit'));

    expect(labels.some((label) => /edit/i.test(label))).toBe(true);
    expect(labels.some((label) => /delete/i.test(label))).toBe(true);
  });

  it('Settle opens the settle dialog with that deposit and reloads after a successful settle', async () => {
    await setup([activeDeposit, dueDeposit, settledDeposit]);
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const listCallsBefore = depositListCalls();

    settleButton(rowFor('Matured deposit'))!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(
      SettleDepositDialog,
      expect.objectContaining({ data: expect.objectContaining({ deposit: dueDeposit }) }),
    );
    await vi.waitFor(() => expect(depositListCalls()).toBeGreaterThan(listCallsBefore));
  });

  it('a cancelled settle does not reload the list', async () => {
    await setup([dueDeposit]);
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const listCallsBefore = depositListCalls();

    settleButton(rowFor('Matured deposit'))!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(SettleDepositDialog, expect.anything());
    expect(depositListCalls()).toBe(listCallsBefore);
  });

  // deposit-payout-to-cash AC-9. A Settled row carries a "Pay out" icon button that opens
  // the payout dialog; a PaidOut row carries none, but shows a "Paid out" chip, the payout date and
  // the destination's name ("—" once that asset was removed), and still offers Delete.
  describe('payout', () => {
    // Recognised by its "Pay out" tooltip (or an aria-label saying the same).
    function transferButton(row: HTMLElement): HTMLButtonElement | undefined {
      return Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find(
        (button) =>
          /pay out/i.test(button.getAttribute('mattooltip') ?? '') ||
          /pay out/i.test(button.getAttribute('aria-label') ?? ''),
      );
    }

    const allStatuses = [activeDeposit, dueDeposit, settledDeposit, paidOutDeposit];

    it('only the Settled row carries the "Pay out" button', async () => {
      await setup(allStatuses);

      expect(transferButton(rowFor('Settled deposit'))).toBeDefined();
      expect(transferButton(rowFor('Paid-out deposit'))).toBeUndefined();
      expect(transferButton(rowFor('Matured deposit'))).toBeUndefined();
      expect(transferButton(rowFor('Running deposit'))).toBeUndefined();
    });

    // deposit-payout-to-savings AC-10: the payout action is "Pay out", shown with the `output` icon.
    it('the payout button of a Settled row is labelled "Pay out" and shows the output icon', async () => {
      await setup(allStatuses);

      const button = transferButton(rowFor('Settled deposit'))!;

      expect(button.getAttribute('aria-label')).toBe('Pay out');
      expect(button.querySelector('mat-icon')?.textContent?.trim()).toBe('output');
      expect(
        Array.from(rowFor('Settled deposit').querySelectorAll('button')).some((candidate) =>
          /transfer to cash/i.test(candidate.getAttribute('aria-label') ?? ''),
        ),
      ).toBe(false);
    });

    // deposit-payout-to-savings AC-10: a savings account's name shows as the destination, like Cash's.
    it('a PaidOut row shows the name of a savings destination', async () => {
      await setup([paidOutIntoSavingsDeposit]);

      const row = rowFor('Deposit paid into savings');

      expect(row.querySelector('mat-chip, mat-chip-option, .mat-mdc-chip')?.textContent).toContain(
        'Paid out',
      );
      expect(row.textContent).toContain(formatDate('2026-04-20T00:00:00', 'mediumDate', 'en-US'));
      expect(row.textContent).toContain(plnSavingsCandidate.name);
      expect(transferButton(row)).toBeUndefined();
    });

    it('the PaidOut row shows the "Paid out" chip, the payout date and the destination', async () => {
      await setup(allStatuses);

      const row = rowFor('Paid-out deposit');
      const chip = row.querySelector('mat-chip, mat-chip-option, .mat-mdc-chip');
      expect(chip?.textContent).toContain('Paid out');
      expect(row.textContent).toContain(formatDate('2026-04-20T00:00:00', 'mediumDate', 'en-US'));
      expect(row.textContent).toContain(paidOutDepositDestinationName);
      expect(settleButton(row)).toBeUndefined();
    });

    it('a PaidOut row whose destination was removed shows "—" for it', async () => {
      await setup([paidOutDepositWithRemovedDestination]);

      const row = rowFor('Orphaned payout');
      expect(row.querySelector('mat-chip, mat-chip-option, .mat-mdc-chip')?.textContent).toContain(
        'Paid out',
      );
      expect(row.textContent).toContain(formatDate('2026-04-20T00:00:00', 'mediumDate', 'en-US'));
      expect(row.textContent).toContain('—');
    });

    it('the PaidOut row still offers Delete, but not Edit', async () => {
      await setup([paidOutDeposit]);

      const labels = await menuItemLabels(rowFor('Paid-out deposit'));

      expect(labels.some((label) => /delete/i.test(label))).toBe(true);
      expect(labels.some((label) => /edit/i.test(label))).toBe(false);
    });

    it('Pay out opens the payout dialog with that deposit and reloads after a payout', async () => {
      await setup(allStatuses);
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      const listCallsBefore = depositListCalls();

      transferButton(rowFor('Settled deposit'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(
        PayOutDepositDialog,
        expect.objectContaining({ data: expect.objectContaining({ deposit: settledDeposit }) }),
      );
      await vi.waitFor(() => expect(depositListCalls()).toBeGreaterThan(listCallsBefore));
    });

    it('a cancelled payout does not reload the list', async () => {
      await setup([settledDeposit]);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      const listCallsBefore = depositListCalls();

      transferButton(rowFor('Settled deposit'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(PayOutDepositDialog, expect.anything());
      expect(depositListCalls()).toBe(listCallsBefore);
    });
  });

  // deposit-rollover AC-9. Due and Settled rows carry an `autorenew` icon button with the tooltip
  // "Roll over" next to their existing button; it opens the roll-over dialog with that deposit and
  // reloads the list after a successful rollover. Active and PaidOut rows carry none.
  describe('roll over', () => {
    // Recognised by its "Roll over" tooltip (or an aria-label saying the same).
    function rollOverButton(row: HTMLElement): HTMLButtonElement | undefined {
      return Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find(
        (button) =>
          /roll over/i.test(button.getAttribute('mattooltip') ?? '') ||
          /roll over/i.test(button.getAttribute('aria-label') ?? ''),
      );
    }

    const allStatuses = [activeDeposit, dueDeposit, settledDeposit, paidOutDeposit];

    it('only the Due and Settled rows carry the "Roll over" button', async () => {
      await setup(allStatuses);

      expect(rollOverButton(rowFor('Matured deposit'))).toBeDefined();
      expect(rollOverButton(rowFor('Settled deposit'))).toBeDefined();
      expect(rollOverButton(rowFor('Running deposit'))).toBeUndefined();
      expect(rollOverButton(rowFor('Paid-out deposit'))).toBeUndefined();
      // Next to, not instead of, the row's existing action.
      expect(settleButton(rowFor('Matured deposit'))).toBeDefined();
    });

    it('the button shows the autorenew icon', async () => {
      await setup([dueDeposit]);

      const button = rollOverButton(rowFor('Matured deposit'));
      expect(button?.querySelector('mat-icon')?.textContent?.trim()).toBe('autorenew');
    });

    it.each([
      ['Due', 'Matured deposit', dueDeposit],
      ['Settled', 'Settled deposit', settledDeposit],
    ])(
      'on a %s row it opens the roll-over dialog with that deposit and reloads after a rollover',
      async (_status, name, deposit) => {
        await setup(allStatuses);
        dialog.open.mockReturnValue({ afterClosed: () => of(true) });
        const listCallsBefore = depositListCalls();

        rollOverButton(rowFor(name))!.click();
        await fixture.whenStable();

        expect(dialog.open).toHaveBeenCalledWith(
          RollOverDepositDialog,
          expect.objectContaining({ data: expect.objectContaining({ deposit }) }),
        );
        await vi.waitFor(() => expect(depositListCalls()).toBeGreaterThan(listCallsBefore));
      },
    );

    it('a cancelled rollover does not reload the list', async () => {
      await setup([dueDeposit]);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      const listCallsBefore = depositListCalls();

      rollOverButton(rowFor('Matured deposit'))!.click();
      await fixture.whenStable();

      expect(dialog.open).toHaveBeenCalledWith(RollOverDepositDialog, expect.anything());
      expect(depositListCalls()).toBe(listCallsBefore);
    });
  });

  // asset-archive AC-10. A deposit can be archived on its own: "Show archived" (off by default)
  // reveals it with an "Archived" chip, the row menu gains Archive / Restore behind a ConfirmDialog,
  // and an archived row keeps only Restore and Delete.
  describe('archive', () => {
    const archiveMessage =
      'It drops out of net worth from today. Its transactions and history are kept, and you can restore it at any time.';

    const showArchived = () => showArchivedToggle(fixture);
    const clickMenuItem = (row: HTMLElement, label: RegExp) =>
      clickRowMenuItem(fixture, row, label);

    function writes(): Request[] {
      return fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method !== 'GET');
    }

    it('hides archived deposits until Show archived is on', async () => {
      await setup([activeDeposit, archivedDueDeposit]);

      expect(rows()).toHaveLength(1);
      expect((fixture.nativeElement as HTMLElement).textContent).not.toContain(
        archivedDueDeposit.name,
      );

      await showArchived();

      expect(rows()).toHaveLength(2);
      const chip = rowFor(archivedDueDeposit.name).querySelector(
        'mat-chip, mat-chip-option, .mat-mdc-chip',
      );
      expect(rowFor(archivedDueDeposit.name).textContent).toContain('Archived');
      expect(chip).not.toBeNull();
      expect(rowFor('Running deposit').textContent).not.toContain('Archived');
    });

    it('archives a deposit after confirmation', async () => {
      await setup([activeDeposit, dueDeposit]);
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      const listCallsBefore = depositListCalls();

      await clickMenuItem(rowFor('Matured deposit'), /\barchive\b/i);

      expect(dialog.open).toHaveBeenCalledWith(
        ConfirmDialog,
        expect.objectContaining({
          data: expect.objectContaining({ message: archiveMessage }),
        }),
      );
      await vi.waitFor(() => expect(writes()).toHaveLength(1));
      const [request] = writes();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${dueDeposit.portfolioId}/assets/${dueDeposit.assetId}/archive`,
      );
      await vi.waitFor(() => expect(depositListCalls()).toBeGreaterThan(listCallsBefore));
    });

    it('does not archive when the confirmation is cancelled', async () => {
      await setup([dueDeposit]);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });

      await clickMenuItem(rowFor('Matured deposit'), /\barchive\b/i);

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      expect(writes()).toHaveLength(0);
    });

    it('restores an archived deposit', async () => {
      await setup([archivedDueDeposit]);
      await showArchived();
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      const listCallsBefore = depositListCalls();

      await clickMenuItem(rowFor(archivedDueDeposit.name), /restore/i);

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      await vi.waitFor(() => expect(writes()).toHaveLength(1));
      const [request] = writes();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${archivedDueDeposit.portfolioId}/assets/${archivedDueDeposit.assetId}/restore`,
      );
      await vi.waitFor(() => expect(depositListCalls()).toBeGreaterThan(listCallsBefore));
    });

    it('does not restore when the confirmation is cancelled', async () => {
      await setup([archivedDueDeposit]);
      await showArchived();
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });

      await clickMenuItem(rowFor(archivedDueDeposit.name), /restore/i);

      expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
      expect(writes()).toHaveLength(0);
    });

    it('shows only Restore and Delete for an archived deposit', async () => {
      await setup([archivedDueDeposit, archivedSettledDeposit]);
      await showArchived();

      for (const deposit of [archivedDueDeposit, archivedSettledDeposit]) {
        const row = rowFor(deposit.name);
        const labels = await menuItemLabels(row);

        expect(labels.some((label) => /restore/i.test(label))).toBe(true);
        expect(labels.some((label) => /delete/i.test(label))).toBe(true);
        expect(labels.some((label) => /edit/i.test(label))).toBe(false);
        expect(labels.some((label) => /\barchive\b/i.test(label))).toBe(false);
        // Not one of the row's own action buttons: no settle, no pay out.
        expect(settleButton(row)).toBeUndefined();
        expect(
          Array.from(row.querySelectorAll<HTMLButtonElement>('button')).some(
            (button) =>
              /pay out/i.test(button.getAttribute('mattooltip') ?? '') ||
              /pay out/i.test(button.getAttribute('aria-label') ?? ''),
          ),
        ).toBe(false);
        TestBed.inject(OverlayContainer).getContainerElement().replaceChildren();
      }
    });

    it('offers Archive, not Restore, for an active deposit', async () => {
      await setup([dueDeposit]);

      const labels = await menuItemLabels(rowFor('Matured deposit'));

      expect(labels.some((label) => /\barchive\b/i.test(label))).toBe(true);
      expect(labels.some((label) => /restore/i.test(label))).toBe(false);
    });
  });

  it('shows an empty state with an Add button when there are no deposits', async () => {
    await setup([]);

    expect(rows()).toHaveLength(0);
    const buttons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
      (button) => button.textContent ?? '',
    );
    expect(buttons.some((text) => /add/i.test(text))).toBe(true);
  });

  // i18n screens (#132) AC-6: headings, table headers, the row actions' tooltips and accessible
  // names, the row menu, empty state, delete confirmation and failure snackbar fallback follow the
  // language.
  describe('in Polish', () => {
    // Words spelled the same in Polish.
    const cognates = ['Bank', 'Start', 'Status'];

    // What a row's icon buttons offer: their tooltip (when they have one) and accessible name.
    function actionTooltips(): string[] {
      return fixture.debugElement
        .queryAll(By.css('td.mat-column-actions button'))
        .map((button) => button.injector.get(MatTooltip, null)?.message ?? '')
        .filter((message) => message !== '');
    }

    async function menuItems(): Promise<string[]> {
      const trigger = fixture.debugElement
        .query(By.directive(MatMenuTrigger))
        .injector.get(MatMenuTrigger);
      trigger.openMenu();
      fixture.detectChanges();
      await fixture.whenStable();
      const items = labelsOf(
        TestBed.inject(OverlayContainer).getContainerElement(),
        '.mat-mdc-menu-item',
      );
      trigger.closeMenu();
      fixture.detectChanges();
      await fixture.whenStable();
      return items;
    }

    it('renders in Polish', async () => {
      await setup([dueDeposit, settledDeposit, archivedPortfolioDeposit]);
      const element = fixture.nativeElement as HTMLElement;
      const texts = async () => [
        ...labelsOf(element, 'h1'),
        ...labelsOf(element, '.deposits-page__header > button'),
        ...labelsOf(element, 'th:not(:empty)'),
        ...actionTooltips(),
        ...(await menuItems()),
      ];

      const english = await texts();
      expect(english).toEqual([
        'Deposits & savings',
        'Add deposit',
        'Name',
        'Bank',
        'Portfolio',
        'Principal',
        'Rate',
        'Start',
        'Maturity',
        'Net profit',
        'Final amount',
        'Status',
        'Settle maturity',
        'Roll over',
        'Pay out',
        'Roll over',
        'Edit',
        'Archive',
        'Delete',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, await texts(), cognates)).toEqual([]);
    });

    it("renders the rows' accessible names in Polish", async () => {
      await setup([dueDeposit, settledDeposit]);
      const element = fixture.nativeElement as HTMLElement;
      const labels = () => attributesOf(element, 'td.mat-column-actions button', 'aria-label');

      const english = labels();
      expect(english).toEqual([
        'Settle maturity of Matured deposit',
        'Roll over',
        'Actions for this deposit',
        'Pay out',
        'Roll over',
        'Actions for this deposit',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, labels())).toEqual([]);
      expect(labels()[0]).toContain('Matured deposit');
    });

    it('renders the archived-portfolio marker in Polish', async () => {
      await setup([archivedPortfolioDeposit]);
      const element = fixture.nativeElement as HTMLElement;
      const marker = () => textOf(element.querySelector('.deposits-page__muted'));

      expect(marker()).toBe('(archived)');

      await switchLanguage(fixture, 'pl');

      // The parentheses may sit in the template or in the translation.
      const text = marker();
      expect(text).not.toBe('(archived)');
      expect(
        matchesTranslation('pl', text) || matchesTranslation('pl', text.replace(/^\(|\)$/g, '')),
        `"${text}" is not a pl.json value`,
      ).toBe(true);
    });

    it('renders the empty state in Polish', async () => {
      await setup([]);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, '.deposits-page__state p'),
        ...labelsOf(element, '.deposits-page__state button'),
      ];

      const english = texts();
      expect(english).toEqual(["You don't have any term deposits yet.", 'Add your first deposit']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the load-failure retry button in Polish', async () => {
      await setup([]);
      fetchSpy.mockImplementation(async () =>
        jsonResponse({ detail: 'Service unavailable.' }, 503),
      );
      component['depositsResource'].reload();
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      const retry = () => labelsOf(element, '.deposits-page__state button');

      expect(retry()).toEqual(['Retry']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(['Retry'], retry())).toEqual([]);
      // The backend's own message stays as it arrived.
      expect(textOf(element.querySelector('.deposits-page__state p'))).toBe('Service unavailable.');
    });

    it('asks to delete in Polish, with the deposit name in the message', async () => {
      await setup([dueDeposit]);
      dialog.open.mockReturnValue({ afterClosed: () => of(false) });
      const confirmation = () =>
        (
          dialog.open.mock.calls[0][1] as {
            data: { title: string; message: string; confirmLabel: string };
          }
        ).data;
      await component['remove'](dueDeposit);
      const english = confirmation();
      dialog.open.mockClear();

      await switchLanguage(fixture, 'pl');
      await component['remove'](dueDeposit);
      const polish = confirmation();

      expect(english.title).toBe('Delete this deposit?');
      expect(
        polishProblems([english.title, english.confirmLabel], [polish.title, polish.confirmLabel]),
      ).toEqual([]);
      expect(polish.message).toContain('"Matured deposit"');
      expect(polish.message).not.toContain('permanently deleted');
      expect(
        matchesTranslation('pl', polish.message),
        `"${polish.message}" is not a pl.json value`,
      ).toBe(true);
    });

    it('shows the failure snackbar fallback in Polish', async () => {
      await setup([dueDeposit]);
      dialog.open.mockReturnValue({ afterClosed: () => of(true) });
      fetchSpy.mockImplementation(async (input: unknown) =>
        (input as Request).method === 'DELETE'
          ? jsonResponse({ title: 'Boom' }, 500)
          : jsonResponse([dueDeposit]),
      );

      await switchLanguage(fixture, 'pl');
      await component['remove'](dueDeposit);

      expect(snackBar.open).toHaveBeenCalledTimes(1);
      const [message, action] = snackBar.open.mock.calls[0] as [string, string];
      expect(polishProblems(['Failed to delete deposit.', 'Dismiss'], [message, action])).toEqual(
        [],
      );
    });
  });
});
