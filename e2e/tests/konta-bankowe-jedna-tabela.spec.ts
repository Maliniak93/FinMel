import type { Locator, Page } from '@playwright/test';
import {
  expandWealthSections,
  expect,
  logInThroughUi,
  registerThroughUi,
  test,
  wealthTab,
} from './fixtures';
import { choose, eur, pln } from './planning';
import { expectProjected } from './projection';

const MULTI = 'Multi oszczędnościowe';
const ERSTE = 'Erste Platinum';
const REVOLUT_EUR = 'Revolut EUR';
const LABEL = 'Poza majątkiem';
const NOTE = 'bez kont poza majątkiem';
const NO_MATCH = 'Żadne konto bankowe nie pasuje do filtra.';

async function openBankList(page: Page): Promise<void> {
  await wealthTab(page, 'Konta bankowe');
  await expect(page).toHaveURL(/\/wealth\/bank$/);
}

function table(page: Page): Locator {
  return page.locator('.bank-accounts__table');
}

function names(page: Page): Locator {
  return table(page).locator('tbody td.mat-column-name a');
}

function accountRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

function statusOf(page: Page, name: string): Locator {
  return accountRow(page, name).locator('.mat-column-status');
}

function bankTab(page: Page): Locator {
  return page.getByRole('tab', { name: /^Konta bankowe(\s|$)/ });
}

function chip(page: Page, name: string): Locator {
  return page.getByRole('option', { name, exact: true });
}

function flagSwitch(dialog: Locator): Locator {
  return dialog.getByRole('switch', { name: /Poza majątkiem/ });
}

async function openRowMenu(page: Page, name: string, item: string): Promise<void> {
  await accountRow(page, name)
    .getByRole('button', { name: `Akcje dla: ${name}`, exact: true })
    .click();
  await page.getByRole('menuitem', { name: item, exact: true }).click();
}

async function addAccount(
  page: Page,
  account: {
    name: string;
    currency: RegExp;
    variant: string;
    outside: boolean;
    deposit: string;
  },
): Promise<void> {
  await page.getByRole('button', { name: 'Dodaj konto', exact: true }).first().click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nazwa').fill(account.name);
  await choose(page, dialog.getByRole('combobox', { name: 'Waluta' }), account.currency);
  await choose(page, dialog.getByRole('combobox', { name: 'Wariant' }), account.variant);
  await dialog.getByLabel('Roczne oprocentowanie (%)').fill('0');
  if (account.outside) {
    await flagSwitch(dialog).click();
    await expect(flagSwitch(dialog)).toBeChecked();
  }
  await dialog.getByRole('button', { name: 'Utwórz' }).click();
  await expect(dialog).toBeHidden();
  await expect(accountRow(page, account.name)).toBeVisible();

  await openRowMenu(page, account.name, 'Wpłata');
  const deposit = page.getByRole('dialog');
  await deposit.getByLabel('Kwota').fill(account.deposit);
  await deposit.getByRole('button', { name: 'Zapisz' }).click();
  await expect(deposit).toBeHidden();
}

async function editAccount(
  page: Page,
  name: string,
  change: { variant?: string; outside?: boolean },
): Promise<void> {
  await openRowMenu(page, name, 'Edytuj');
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj rachunek' })).toBeVisible();
  if (change.variant) {
    await choose(page, dialog.getByRole('combobox', { name: 'Wariant' }), change.variant);
  }
  if (change.outside !== undefined) {
    const toggle = flagSwitch(dialog);
    if ((await toggle.getAttribute('aria-checked')) !== String(change.outside)) {
      await toggle.click();
    }
    await expect(toggle).toBeChecked({ checked: change.outside });
  }
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function expectCaptionSum(page: Page, amount: RegExp): Promise<void> {
  await expectProjected(
    () => page.reload(),
    async () => {
      await expect(bankTab(page)).toContainText(amount, { timeout: 2_000 });
    },
  );
}

test('konta bankowe w jednej tabeli: etykieta Poza majątkiem, filtry chipów i podpis zakładki', async ({
  page,
  user,
}) => {
  test.setTimeout(600_000);

  await registerThroughUi(page, user);
  await logInThroughUi(page, user);
  await openBankList(page);
  await addAccount(page, {
    name: MULTI,
    currency: /^PLN/,
    variant: 'Oszczędnościowe',
    outside: false,
    deposit: '15000',
  });
  await addAccount(page, {
    name: ERSTE,
    currency: /^PLN/,
    variant: 'Bieżące',
    outside: true,
    deposit: '6400',
  });

  await test.step('Krok 1: jedna tabela z wierszami Erste Platinum i Multi oszczędnościowe, bez sekcji Konta bieżące i Suma w PLN', async () => {
    await openBankList(page);
    await expect(table(page)).toHaveCount(1);
    await expect(names(page)).toHaveText([ERSTE, MULTI]);
    await expect(page.getByTestId('current-accounts')).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Konta bieżące' })).toHaveCount(0);
    await expect(page.getByText('Suma w PLN')).toHaveCount(0);
  });

  await test.step('Krok 2: Erste Platinum ma etykietę Poza majątkiem w Statusie, Multi oszczędnościowe żadnej', async () => {
    await expect(statusOf(page, ERSTE)).toContainText(LABEL);
    await expect(statusOf(page, MULTI)).not.toContainText(LABEL);
  });

  await test.step('Krok 3: podpis zakładki Konta bankowe to 15 000,00 zł bez 6 400 zł i bez dopisku bez kont poza majątkiem', async () => {
    await expectCaptionSum(page, pln('15 000,00'));
    await expect(bankTab(page)).not.toContainText(pln('21 400,00'));
    await expect(bankTab(page)).not.toContainText(NOTE);
  });

  await test.step('Krok 4: chip Bieżące pokazuje tylko Erste Platinum z etykietą, podpis zakładki nadal 15 000,00 zł', async () => {
    await chip(page, 'Bieżące').click();
    await expect(names(page)).toHaveText([ERSTE]);
    await expect(statusOf(page, ERSTE)).toContainText(LABEL);
    await expect(bankTab(page)).toContainText(pln('15 000,00'));
  });

  await test.step('Krok 5: chip Oszczędnościowe pokazuje tylko Multi oszczędnościowe', async () => {
    await chip(page, 'Oszczędnościowe').click();
    await expect(names(page)).toHaveText([MULTI]);
  });

  await test.step('Krok 6: chip Wszystkie pokazuje oba wiersze', async () => {
    await chip(page, 'Wszystkie').click();
    await expect(names(page)).toHaveText([ERSTE, MULTI]);
  });

  await test.step('Krok 7: po zmianie wariantu Erste Platinum na Oszczędnościowe chip Bieżące jest pusty, Oszczędnościowe pokazuje oba konta, etykieta zostaje', async () => {
    await editAccount(page, ERSTE, { variant: 'Oszczędnościowe' });
    await chip(page, 'Bieżące').click();
    await expect(page.getByText(NO_MATCH)).toBeVisible();
    await chip(page, 'Oszczędnościowe').click();
    await expect(names(page)).toHaveText([ERSTE, MULTI]);
    await expect(statusOf(page, ERSTE)).toContainText(LABEL);
  });

  await test.step('Krok 8: po przywróceniu wariantu Bieżące chip Bieżące znów pokazuje Erste Platinum', async () => {
    await editAccount(page, ERSTE, { variant: 'Bieżące' });
    await chip(page, 'Bieżące').click();
    await expect(names(page)).toHaveText([ERSTE]);
    await chip(page, 'Wszystkie').click();
    await expect(names(page)).toHaveText([ERSTE, MULTI]);
  });

  await test.step('Krok 9: menu ⋮ obu kont ma ten sam zestaw pozycji', async () => {
    const items: string[][] = [];
    for (const name of [ERSTE, MULTI]) {
      await accountRow(page, name)
        .getByRole('button', { name: `Akcje dla: ${name}`, exact: true })
        .click();
      items.push(await page.getByRole('menuitem').allInnerTexts());
      await page.keyboard.press('Escape');
      await expect(page.getByRole('menuitem').first()).toBeHidden();
    }
    expect(items[0]!.length).toBeGreaterThan(0);
    expect(items[0]!.map((text) => text.replace(/\s+/g, ' ').trim())).toEqual(
      items[1]!.map((text) => text.replace(/\s+/g, ' ').trim()),
    );
  });

  await test.step('Krok 10: po wypłacie 6 400 zł archiwizacja Erste Platinum ukrywa je i dopisek, a po Pokaż zarchiwizowane wraca z etykietami Poza majątkiem i Zarchiwizowany; przywrócenie i ponowna wpłata 6 400 zł', async () => {
    await openRowMenu(page, ERSTE, 'Wypłata');
    const withdraw = page.getByRole('dialog');
    await withdraw.getByLabel('Kwota').fill('6400');
    await withdraw.getByRole('button', { name: 'Zapisz' }).click();
    await expect(withdraw).toBeHidden();
    await openRowMenu(page, ERSTE, 'Archiwizuj');
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Archiwizuj', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(names(page)).toHaveText([MULTI]);
    await expectProjected(
      () => page.reload(),
      async () => {
        await expect(bankTab(page)).not.toContainText(NOTE, { timeout: 2_000 });
      },
    );

    await page.getByRole('switch', { name: 'Pokaż zarchiwizowane' }).click();
    await expect(names(page)).toHaveText([ERSTE, MULTI]);
    await expect(statusOf(page, ERSTE)).toHaveText(/Poza majątkiem.*Zarchiwizowany/);

    await openRowMenu(page, ERSTE, 'Przywróć');
    const restore = page.getByRole('dialog');
    await restore.getByRole('button', { name: 'Przywróć', exact: true }).click();
    await expect(restore).toBeHidden();
    await page.getByRole('switch', { name: 'Pokaż zarchiwizowane' }).click();
    await expect(statusOf(page, ERSTE)).not.toContainText('Zarchiwizowany');
    await expect(statusOf(page, ERSTE)).toContainText(LABEL);
    await openRowMenu(page, ERSTE, 'Wpłata');
    const deposit = page.getByRole('dialog');
    await deposit.getByLabel('Kwota').fill('6400');
    await deposit.getByRole('button', { name: 'Zapisz' }).click();
    await expect(deposit).toBeHidden();
  });

  await test.step('Krok 11: Revolut EUR poza majątkiem stoi w tabeli po nazwie, ma etykietę i saldo 100 EUR, podpis zakładki bez zmian', async () => {
    await addAccount(page, {
      name: REVOLUT_EUR,
      currency: /^EUR/,
      variant: 'Oszczędnościowe',
      outside: true,
      deposit: '100',
    });
    await expect(names(page)).toHaveText([ERSTE, MULTI, REVOLUT_EUR]);
    await expect(statusOf(page, REVOLUT_EUR)).toContainText(LABEL);
    await expect(accountRow(page, REVOLUT_EUR).locator('.mat-column-balance')).toContainText(
      eur('100,00'),
    );
    await expectCaptionSum(page, pln('15 000,00'));
    await expect(bankTab(page)).not.toContainText(pln('15 425,00'));
  });

  await test.step('Krok 12: po wyłączeniu Poza majątkiem na Erste Platinum etykieta znika, podpis zakładki 21 400,00 zł bez dopisku', async () => {
    await editAccount(page, ERSTE, { outside: false });
    await expect(statusOf(page, ERSTE)).not.toContainText(LABEL);
    await expectCaptionSum(page, pln('21 400,00'));
    await expect(bankTab(page)).not.toContainText(NOTE);
  });

  await test.step('Krok 13: po przełączeniu flag podpis zakładki to 15 000 zł + 100 EUR po kursie 4,25 (15 425,00 zł), bez dopisku, Revolut EUR bez etykiety', async () => {
    await editAccount(page, ERSTE, { outside: true });
    await editAccount(page, REVOLUT_EUR, { outside: false });
    await expect(statusOf(page, REVOLUT_EUR)).not.toContainText(LABEL);
    await expect(statusOf(page, ERSTE)).toContainText(LABEL);
    await expectCaptionSum(page, pln('15 425,00'));
    await expect(bankTab(page)).not.toContainText(NOTE);
  });

  await test.step('Krok 14: strona konta Erste Platinum pokazuje saldo 6 400 zł i etykietę Poza majątkiem', async () => {
    await page.getByRole('link', { name: ERSTE, exact: true }).click();
    await expect(page).toHaveURL(/\/wealth\/bank\/[0-9a-f-]+$/);
    await expect(page.getByRole('heading', { name: ERSTE, exact: true })).toBeVisible();
    await expect(page.getByTestId('account-cash')).toContainText(pln('6 400,00'));
    await expect(page.locator('.account-detail__meta')).toContainText(LABEL);
  });

  await test.step('Krok 15: Erste Platinum nie ma w Majątek → Wszystko ani na pulpicie, wartość netto bez 6 400 zł', async () => {
    await page.goto('/wealth');
    await expandWealthSections(page);
    await expect(page.locator('.wealth-all__name', { hasText: MULTI })).toBeVisible();
    await expect(page.locator('.wealth-all__name', { hasText: ERSTE })).toHaveCount(0);
    await expectProjected(
      () => page.goto('/dashboard'),
      async () => {
        await expect(page.getByTestId('net-worth')).toContainText(pln('15 425,00'), {
          timeout: 2_000,
        });
      },
      { timeout: 60_000 },
    );
    await expect(page.locator('.accounts-card__row').filter({ hasText: ERSTE })).toHaveCount(0);
  });
});
