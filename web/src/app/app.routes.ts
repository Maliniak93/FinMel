import { Routes } from '@angular/router';

import { authGuard, publicGuard } from './core/auth/auth.guard';
import { accountTypeGuard } from './features/accounts/account-detail/account-type.guard';
import { Shell } from './layout/shell/shell';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [publicGuard],
    loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
  },
  {
    path: 'register',
    canActivate: [publicGuard],
    loadComponent: () => import('./features/auth/register/register').then((m) => m.Register),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'wealth',
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/wealth/wealth-layout').then((m) => m.WealthLayout),
            children: [
              { path: '', pathMatch: 'full', redirectTo: 'accounts' },
              {
                path: 'accounts',
                loadComponent: () => import('./features/accounts/accounts').then((m) => m.Accounts),
              },
              {
                path: 'bank',
                loadComponent: () =>
                  import('./features/bank-accounts/bank-accounts').then((m) => m.BankAccounts),
              },
              {
                path: 'cash',
                loadComponent: () => import('./features/cash/cash').then((m) => m.Cash),
              },
              {
                path: 'deposits',
                loadComponent: () => import('./features/deposits/deposits').then((m) => m.Deposits),
              },
              {
                path: 'bonds',
                loadComponent: () => import('./features/bonds/bonds').then((m) => m.Bonds),
              },
              {
                path: 'securities',
                loadComponent: () =>
                  import('./features/securities/securities').then((m) => m.Securities),
              },
              {
                path: 'metals',
                loadComponent: () => import('./features/metals/metals').then((m) => m.Metals),
              },
            ],
          },
          {
            path: 'accounts/:id',
            canActivate: [accountTypeGuard],
            loadComponent: () =>
              import('./features/accounts/account-detail/account-detail').then(
                (m) => m.AccountDetail,
              ),
          },
          {
            path: 'bank/:id',
            canActivate: [accountTypeGuard],
            loadComponent: () =>
              import('./features/accounts/account-detail/account-detail').then(
                (m) => m.AccountDetail,
              ),
          },
          {
            path: 'assets/:assetId/transactions',
            loadComponent: () =>
              import('./features/transactions/transactions').then((m) => m.Transactions),
          },
        ],
      },
      {
        path: 'groups',
        loadComponent: () => import('./features/groups/groups').then((m) => m.Groups),
      },
      {
        path: 'planning',
        loadComponent: () => import('./features/planning/planning').then((m) => m.Planning),
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/settings').then((m) => m.Settings),
      },
      {
        path: '**',
        loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
