import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';

function runRow(page: Page, label: string) {
  return page.locator('.settings-page__run').filter({ hasText: label });
}

test.describe('sync przy starcie', () => {
  test('sync przy starcie: Ustawienia pokazują status i źródło przebiegów', async ({
    signedInPage: page,
  }) => {
    await test.step('Krok 1: Ustawienia, Synchronizuj teraz, wiersz Ceny Zakończona i Ręcznie', async () => {
      await page.goto('/settings');
      await page.getByRole('button', { name: 'Synchronizuj teraz' }).click();
      const prices = runRow(page, 'Ceny');
      await expect(prices).toContainText('Zakończona', { timeout: 30_000 });
      await expect(prices).toContainText('ręcznie');
    });

    await test.step('Krok 2: wiersz Kursy walut Zakończona ze źródłem Harmonogram lub Start', async () => {
      await page.goto('/settings');
      const fx = runRow(page, 'Kursy walut');
      await expect(fx).toContainText('Zakończona', { timeout: 30_000 });
      await expect(fx).toContainText(/harmonogram|start/);
    });
  });
});
