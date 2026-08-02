export const environment = {
  production: false,
  // Relative — `ng serve`'s dev proxy (`proxy.conf.json`) forwards `/api/*` to the Gateway
  // (http://localhost:60684, fixed local dev port under Aspire), so the client needs no origin.
  gatewayUrl: '',
};
