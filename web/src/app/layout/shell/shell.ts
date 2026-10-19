import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';

import { AuthService } from '../../core/auth/auth';
import { PlanningState } from '../../core/planning/planning-state';
import { ThemeService, type ThemePreference } from '../../core/theme/theme';
import { LanguageSwitch } from '../../shared/language-switch/language-switch';

const THEME_ICONS: Record<ThemePreference, string> = {
  system: 'brightness_auto',
  light: 'light_mode',
  dark: 'dark_mode',
};

const THEME_NEXT_LABELS: Record<ThemePreference, string> = {
  system: 'shell.theme.system',
  light: 'shell.theme.light',
  dark: 'shell.theme.dark',
};

interface NavLink {
  path: string;
  label: string;
  icon: string;
}

interface NavSection {
  label: string;
  links: NavLink[];
}

const ORIGIN_PARAM = 'from';
const TRANSACTIONS_ROUTE = /^\/assets\/[^/]+\/transactions$/;

@Component({
  selector: 'app-shell',
  imports: [
    RouterLink,
    RouterOutlet,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    MatSidenavModule,
    MatToolbarModule,
    MatTooltipModule,
    TranslocoPipe,
    LanguageSwitch,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly authService = inject(AuthService);
  protected readonly themeService = inject(ThemeService);
  protected readonly planningState = inject(PlanningState);

  constructor() {
    this.planningState.startSession();
  }

  protected readonly navSections: NavSection[] = [
    {
      label: 'shell.navSection.overview',
      links: [
        { path: 'dashboard', label: 'shell.nav.dashboard', icon: 'dashboard' },
        { path: 'allocation', label: 'shell.nav.allocation', icon: 'donut_small' },
        { path: 'planning', label: 'shell.nav.planning', icon: 'event_note' },
      ],
    },
    {
      label: 'shell.navSection.wealth',
      links: [
        { path: 'accounts', label: 'shell.nav.accounts', icon: 'account_balance_wallet' },
        { path: 'bank-accounts', label: 'shell.nav.bankAccounts', icon: 'account_balance' },
        { path: 'cash', label: 'shell.nav.cash', icon: 'payments' },
        { path: 'deposits', label: 'shell.nav.deposits', icon: 'savings' },
        { path: 'bonds', label: 'shell.nav.bonds', icon: 'receipt_long' },
        { path: 'securities', label: 'shell.nav.securities', icon: 'show_chart' },
        { path: 'metals', label: 'shell.nav.metals', icon: 'diamond' },
        { path: 'groups', label: 'shell.nav.groups', icon: 'category' },
      ],
    },
  ];

  protected readonly settingsLink: NavLink = {
    path: 'settings',
    label: 'shell.nav.settings',
    icon: 'settings',
  };

  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected isActive(path: string): boolean {
    const [pathname, query = ''] = this.url().split('#')[0].split('?');
    const target = `/${path}`;
    if (pathname === target || pathname.startsWith(`${target}/`)) {
      return true;
    }

    return (
      TRANSACTIONS_ROUTE.test(pathname) && new URLSearchParams(query).get(ORIGIN_PARAM) === path
    );
  }

  protected readonly themeIcon = computed(() => THEME_ICONS[this.themeService.preference()]);
  protected readonly themeLabel = computed(() => THEME_NEXT_LABELS[this.themeService.preference()]);

  protected cycleTheme(): void {
    this.themeService.cycle();
  }

  protected logout(): void {
    void this.authService.logout();
  }
}
