export const environment = {
  production: true,
  // ADR-013: Angular never calls services directly, only the Gateway. Relative — the production
  // deploy serves the Angular build and the Gateway behind the same origin/reverse proxy
  // (finalized in T1.15); local dev instead proxies through `proxy.conf.json`.
  gatewayUrl: '',
};
