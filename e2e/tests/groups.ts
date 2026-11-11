import type { Locator, Page } from '@playwright/test';
import { expect, openSelect } from './fixtures';

export async function addContribution(
  page: Page,
  dialog: Locator,
  contribution: { amount: string; account?: string },
): Promise<void> {
  const rows = dialog.getByTestId('contribution-row');
  const index = await rows.count();
  await dialog.getByTestId('add-contribution-row').click();
  await expect(rows).toHaveCount(index + 1);
  const row = rows.nth(index);
  await row.getByRole('spinbutton', { name: 'Kwota (PLN)' }).fill(contribution.amount);
  if (contribution.account) {
    await openSelect(row.getByRole('combobox', { name: 'Konto' }));
    await page.getByRole('option', { name: contribution.account, exact: true }).click();
  }
}

export async function clearContributions(dialog: Locator): Promise<void> {
  const rows = dialog.getByTestId('contribution-row');
  while ((await rows.count()) > 0) {
    await rows.first().getByTestId('remove-contribution-row').click();
  }
}
