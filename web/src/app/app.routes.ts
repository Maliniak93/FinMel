import { Routes } from '@angular/router';

import { authGuard, publicGuard } from './core/auth/auth.guard';
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
        path: 'accounts',
        loadComponent: () => import('./features/accounts/accounts').then((m) => m.Accounts),
      },
      {
        path: 'accounts/:id',
        loadComponent: () =>
          import('./features/accounts/account-detail/account-detail').then((m) => m.AccountDetail),
      },
      {
        path: 'bank-accounts',
        loadComponent: () =>
          import('./features/bank-accounts/bank-accounts').then((m) => m.BankAccounts),
      },
      {
        path: 'assets/:assetId/transactions',
        loadComponent: () =>
          import('./features/transactions/transactions').then((m) => m.Transactions),
      },
      {
        path: 'cash',
        loadComponent: () => import('./features/cash/cash').then((m) => m.Cash),
      },
      {
        path: 'securities',
        loadComponent: () => import('./features/securities/securities').then((m) => m.Securities),
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
        path: 'metals',
        loadComponent: () => import('./features/metals/metals').then((m) => m.Metals),
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
