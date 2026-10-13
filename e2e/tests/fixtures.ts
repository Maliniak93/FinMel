import { expect, test as base, type APIRequestContext, type Page } from '@playwright/test';

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
