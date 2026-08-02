import { client as identityClient } from '../src/app/api/identity/client.gen.js';
import { postApiIdentityLogin, postApiIdentityRegister } from '../src/app/api/identity/sdk.gen.js';
import { client as portfolioClient } from '../src/app/api/portfolio/client.gen.js';
import { getApiPortfolioPortfolios } from '../src/app/api/portfolio/sdk.gen.js';

// T1.7 AC: "a smoke call (list portfolios) works through the Gateway" — proves the generated
// client, the Gateway's openapi/business routing, and JWT auth all line up end to end. Talks only
// to the Gateway (ADR-013): register + login on Identity mint the token listPortfolios needs.
const gatewayUrl = process.env['SKARBIEC_GATEWAY_URL'] ?? 'http://localhost:60684';

identityClient.setConfig({ baseUrl: gatewayUrl });
portfolioClient.setConfig({ baseUrl: gatewayUrl });

async function main(): Promise<void> {
  const email = `smoke-${crypto.randomUUID()}@example.com`;
  const password = 'Str0ng!Passw0rd';

  const register = await postApiIdentityRegister({
    body: { email, password, displayName: 'Smoke Test' },
  });
  if (register.error) {
    throw new Error(`register failed: ${JSON.stringify(register.error)}`);
  }

  const login = await postApiIdentityLogin({ body: { email, password } });
  if (login.error || !login.data) {
    throw new Error(`login failed: ${JSON.stringify(login.error)}`);
  }

  const list = await getApiPortfolioPortfolios({
    headers: { Authorization: `Bearer ${login.data.accessToken}` },
  });
  if (list.error) {
    throw new Error(`listPortfolios failed: ${JSON.stringify(list.error)}`);
  }

  console.log(
    `smoke OK — listPortfolios through the Gateway returned ${list.data?.length ?? 0} portfolio(s)`,
  );
}

main().catch((error: unknown) => {
  console.error(error);
  process.exitCode = 1;
});
