import { OverlayContainer } from '@angular/cdk/overlay';
import { formatDate } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { DepositResponse } from '../../api/portfolio';
import { formatMoney } from '../../shared/format-money';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { DepositFormDialog } from './deposit-form-dialog/deposit-form-dialog';
import { Deposits } from './deposits';
import { PayOutDepositDialog } from './pay-out-deposit-dialog/pay-out-deposit-dialog';
import { RollOverDepositDialog } from './roll-over-deposit-dialog/roll-over-deposit-dialog';
import { SettleDepositDialog } from './settle-deposit-dialog/settle-deposit-dialog';
import {
  activeDeposit,
  archivedPortfolioDeposit,
  dueDeposit,
  paidOutDeposit,
  paidOutDepositDestinationName,
  paidOutDepositWithRemovedDestination,
  settledDeposit,
  settledDepositFinalAmount,
} from './testing/deposit-fixtures';

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

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(
    deposits: DepositResponse[] = [activeDeposit, dueDeposit, archivedPortfolioDeposit],
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'DELETE') {
        return new Response(null, { status: 204 });
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

  // Opens a row's actions menu and returns the labels of the items it offers.
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
      (item) => (item.textContent ?? '').trim(),
    );
  }

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

  // deposit-payout-to-cash AC-9. A Settled row carries a "Transfer to cash" icon button that opens
  // the payout dialog; a PaidOut row carries none, but shows a "Paid out" chip, the payout date and
  // the destination's name ("—" once that asset was removed), and still offers Delete.
  describe('payout', () => {
    // Recognised by its "Transfer to cash" tooltip (or an aria-label saying the same).
    function transferButton(row: HTMLElement): HTMLButtonElement | undefined {
      return Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find(
        (button) =>
          /transfer to cash/i.test(button.getAttribute('mattooltip') ?? '') ||
          /transfer to cash/i.test(button.getAttribute('aria-label') ?? ''),
      );
    }

    const allStatuses = [activeDeposit, dueDeposit, settledDeposit, paidOutDeposit];

    it('only the Settled row carries the "Transfer to cash" button', async () => {
      await setup(allStatuses);

      expect(transferButton(rowFor('Settled deposit'))).toBeDefined();
      expect(transferButton(rowFor('Paid-out deposit'))).toBeUndefined();
      expect(transferButton(rowFor('Matured deposit'))).toBeUndefined();
      expect(transferButton(rowFor('Running deposit'))).toBeUndefined();
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

    it('Transfer to cash opens the payout dialog with that deposit and reloads after a payout', async () => {
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

  it('shows an empty state with an Add button when there are no deposits', async () => {
    await setup([]);

    expect(rows()).toHaveLength(0);
    const buttons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
      (button) => button.textContent ?? '',
    );
    expect(buttons.some((text) => /add/i.test(text))).toBe(true);
  });
});
