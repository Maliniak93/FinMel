import { Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

const WEALTH_TABS = [
  { path: 'accounts', label: 'shell.nav.accounts' },
  { path: 'bank', label: 'shell.nav.bankAccounts' },
  { path: 'cash', label: 'shell.nav.cash' },
  { path: 'deposits', label: 'shell.nav.deposits' },
  { path: 'bonds', label: 'shell.nav.bonds' },
  { path: 'securities', label: 'shell.nav.securities' },
  { path: 'metals', label: 'shell.nav.metals' },
] as const;

@Component({
  selector: 'app-wealth-layout',
  imports: [MatTabsModule, RouterLink, RouterLinkActive, RouterOutlet, TranslocoPipe],
  templateUrl: './wealth-layout.html',
  styleUrl: './wealth-layout.scss',
})
export class WealthLayout {
  protected readonly tabs = WEALTH_TABS;
}
