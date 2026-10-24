import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { getApiPortfolioAccountsById } from '../../../api/portfolio';
import { ACCOUNT_TYPE } from '../account-type';

export const accountTypeGuard: CanActivateFn = async (route) => {
  const router = inject(Router);
  const id = route.paramMap.get('id');
  if (!id) {
    return true;
  }

  const onBankRoute = route.routeConfig?.path?.startsWith('bank/') ?? false;

  try {
    const result = await getApiPortfolioAccountsById({ path: { id } });
    if (result.error || !result.data) {
      return true;
    }
    const isBank = Number(result.data.type) === ACCOUNT_TYPE.Bank;
    if (isBank === onBankRoute) {
      return true;
    }
    return router.createUrlTree([onBankRoute ? '/wealth/accounts' : '/wealth/bank', id]);
  } catch {
    return true;
  }
};
