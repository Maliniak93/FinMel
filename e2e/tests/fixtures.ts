import {
  expect,
  test as base,
  type APIRequestContext,
  type Locator,
  type Page,
} from '@playwright/test';

// The unfloated label and its required marker can sit over the middle of a select, so the click lands on the arrow instead.
export async function openSelect(select: Locator): Promise<void> {
  const box = await select.boundingBox();
  await select.click({ position: { x: Math.max(box!.width - 24, 1), y: box!.height / 2 } });
}

export const WEALTH_TABS: readonly string[] = [
  'Rachunki inwestycyjne',
  'Konta bankowe',
  'Gotówka',
  'Lokaty',
  'Obligacje',
  'Akcje i ETF',
  'Metale szlachetne',
];

export async function wealthTab(page: Page, name: string): Promise<void> {
  await page.getByTestId('nav-list').getByRole('link', { name: 'Majątek', exact: true }).click();
  // The tab bar scrolls at 1280 px, so a pointer click on an edge tab can land on the scroll shift and be lost.
  await page.getByRole('tab', { name: new RegExp(`^${name}(\\s|$)`) }).focus();
  await page.keyboard.press('Enter');
}

export const STUB_URL = 'http://localhost:18081';

export interface Credentials {
  email: string;
  password: string;
  displayName: string;
}

interface Fixtures {
  user: Credentials;
  signedInPage: Page;
  accessToken: string;
}

export async function registerThroughUi(page: Page, user: Credentials): Promise<void> {
  await page.goto('/register');
  await page.getByLabel('Nazwa wyświetlana').fill(user.displayName);
  await page.getByLabel('E-mail').fill(user.email);
  await page.getByLabel('Hasło').fill(user.password);
  await page.getByRole('button', { name: 'Zarejestruj się' }).click();
  await expect(page).toHaveURL(/\/login$/);
}

export async function logInThroughUi(page: Page, user: Credentials): Promise<void> {
  await page.getByLabel('E-mail').fill(user.email);
  await page.getByLabel('Hasło').fill(user.password);
  await page.getByRole('button', { name: 'Zaloguj się' }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
}

export async function logInThroughApi(
  request: APIRequestContext,
  user: Credentials,
): Promise<string> {
  const registered = await request.post('/api/identity/register', { data: user });
  expect(registered.ok()).toBeTruthy();
  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  return ((await login.json()) as { accessToken: string }).accessToken;
}

export function warsawDate(daysAgo = 0): string {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() - daysAgo);
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Warsaw',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(date);
}

function bearer(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

export async function createAccountThroughApi(
  request: APIRequestContext,
  token: string,
  account: { name: string; type: number; wrapper: number },
): Promise<{ id: string; cashAssetId: string }> {
  const response = await request.post('/api/portfolio/accounts', {
    headers: bearer(token),
    data: account,
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as { id: string; cashAssetId: string };
}

export async function depositCashThroughApi(
  request: APIRequestContext,
  token: string,
  cashAssetId: string,
  deposit: { quantity: number; date: string },
): Promise<void> {
  const response = await request.post(`/api/portfolio/assets/${cashAssetId}/transactions`, {
    headers: bearer(token),
    data: { type: 2, quantity: deposit.quantity, unitPrice: 1, date: deposit.date },
  });
  expect(response.ok()).toBeTruthy();
}

export const test = base.extend<Fixtures>({
  // The UI is asserted in Polish; the language key must be set before the first page load.
  context: async ({ context }, use) => {
    await context.addInitScript(() => localStorage.setItem('skarbiec-lang', 'pl'));
    await use(context);
  },

  user: async ({}, use, testInfo) => {
    const unique = `${Date.now().toString(36)}-${testInfo.workerIndex}-${testInfo.retry}`;
    await use({
      email: `e2e-${unique}@skarbiec.test`,
      password: 'E2e-Passw0rd!',
      displayName: `E2E ${unique}`,
    });
  },

  signedInPage: async ({ page, user }, use) => {
    await registerThroughUi(page, user);
    await logInThroughUi(page, user);
    await use(page);
  },

  accessToken: async ({ request, user }, use) => {
    await use(await logInThroughApi(request, user));
  },
});

export { expect };
