import { type Locator, type Page } from '@playwright/test';
import { expect } from './fixtures';

const MONTH_NAMES = [
  'styczeń',
  'luty',
  'marzec',
  'kwiecień',
  'maj',
  'czerwiec',
  'lipiec',
  'sierpień',
  'wrzesień',
  'październik',
  'listopad',
  'grudzień',
];

const MONTH_ABBREVIATIONS = [
  'sty',
  'lut',
  'mar',
  'kwi',
  'maj',
  'cze',
  'lip',
  'sie',
  'wrz',
  'paź',
  'lis',
  'gru',
];

export const MBANK = 'mBank ROR';
export const COMPANY = 'Konto firmowe';
export const REVOLUT = 'Revolut EUR';

// pl-PL leaves out the grouping space in four-digit numbers on some ICU builds and may use a true minus sign.
export function money(amount: string, unit: string): RegExp {
  const negative = amount.startsWith('-');
  const digits = (negative ? amount.slice(1) : amount).replace(/ /g, '\\s?');
  return new RegExp(`${negative ? '[-\\u2212]' : ''}${digits}\\s${unit}`);
}

export function pln(amount: string): RegExp {
  return money(amount, 'zł');
}

export function eur(amount: string): RegExp {
  return money(amount, '(?:EUR|€)');
}

export async function expectPlan(page: Page, amount: string): Promise<void> {
  await expect(page.getByTestId('balance-plan').locator('dd')).toContainText(pln(amount));
}

function dateParts(iso: string): { year: number; month: number; day: number } {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return { year, month, day };
}

export function shortDate(iso: string): RegExp {
  const { year, month, day } = dateParts(iso);
  return new RegExp(`^${day}\\s${MONTH_ABBREVIATIONS[month - 1]}\\.?\\s${year}`);
}

export function dateInText(iso: string): RegExp {
  const { year, month, day } = dateParts(iso);
  return new RegExp(`${day}\\s${MONTH_ABBREVIATIONS[month - 1]}\\.?\\s${year}`);
}

export function monthLabel(month: string): string {
  const [year, number] = month.split('-').map(Number) as [number, number];
  return `${MONTH_NAMES[number - 1]} ${year}`;
}

export function exactly(text: string): RegExp {
  return new RegExp(`^${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`);
}

export async function openMonth(page: Page, month: string): Promise<void> {
  await page.goto(`/planning?month=${month}`);
  await expect(page.getByTestId('month-label')).toHaveText(monthLabel(month));
  await expect(page.getByTestId('balance-plan')).toBeVisible();
}

export function occurrences(page: Page, name: string): Locator {
  return page
    .getByTestId('occurrence-row')
    .filter({ has: page.getByRole('cell', { name: exactly(name) }) });
}

export function occurrence(page: Page, name: string, iso: string): Locator {
  return occurrences(page, name).filter({ has: page.getByRole('cell', { name: shortDate(iso) }) });
}

export function definition(page: Page, name: string): Locator {
  return page.getByTestId('definition-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

export async function choose(page: Page, select: Locator, option: string | RegExp): Promise<void> {
  await select.click();
  await page
    .getByRole(
      'option',
      typeof option === 'string' ? { name: option, exact: true } : { name: option },
    )
    .click();
}

export interface IncomeInput {
  name: string;
  amount: string;
  account: string;
  start: string;
  end?: string;
  interval: string;
  day: number | 'last';
  shift?: 'Bez przesunięcia' | 'Poprzedni dzień roboczy' | 'Następny dzień roboczy';
}

export async function fillRule(
  page: Page,
  dialog: Locator,
  income: Pick<IncomeInput, 'interval' | 'day' | 'shift'>,
): Promise<void> {
  await dialog.getByRole('spinbutton', { name: 'Powtarzaj co (miesięcy)' }).fill(income.interval);
  const dayKind = dialog.getByRole('combobox', { name: 'Dzień', exact: true });
  if (income.day === 'last') {
    await choose(page, dayKind, 'Ostatni dzień roboczy');
    return;
  }
  await choose(page, dayKind, 'Dzień miesiąca');
  await dialog.getByRole('spinbutton', { name: 'Dzień miesiąca' }).fill(String(income.day));
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Przesunięcie na dzień roboczy' }),
    income.shift ?? 'Bez przesunięcia',
  );
}

export async function openAddDialog(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Dodaj wpływ' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Dodaj wpływ cykliczny' })).toBeVisible();
  return dialog;
}

export async function fillIncome(page: Page, dialog: Locator, income: IncomeInput): Promise<void> {
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill(income.amount);
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Konto', exact: true }),
    new RegExp(`^${income.account}`),
  );
  await dialog.getByLabel('Data początku').fill(income.start);
  if (income.end) {
    await dialog.getByLabel('Data końca').fill(income.end);
  }
  await fillRule(page, dialog, income);
}

export async function addIncome(page: Page, income: IncomeInput): Promise<void> {
  const dialog = await openAddDialog(page);
  await fillIncome(page, dialog, income);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

export async function openEditDialog(page: Page, name: string): Promise<Locator> {
  await definition(page, name).getByTestId('edit-income').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj wpływ cykliczny' })).toBeVisible();
  return dialog;
}

export async function saveEdit(dialog: Locator): Promise<void> {
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

export async function confirmDelete(page: Page): Promise<void> {
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
  await expect(dialog).toBeHidden();
}
