import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';

import { AuthService } from '../../core/auth/auth';
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

@Component({
  selector: 'app-shell',
  imports: [
    RouterLink,
    RouterLinkActive,
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

  protected readonly navLinks = [
    { path: 'dashboard', label: 'shell.nav.dashboard', icon: 'dashboard' },
    { path: 'portfolios', label: 'shell.nav.portfolios', icon: 'account_balance_wallet' },
    { path: 'deposits', label: 'shell.nav.deposits', icon: 'savings' },
    { path: 'settings', label: 'shell.nav.settings', icon: 'settings' },
  ];

  protected readonly themeIcon = computed(() => THEME_ICONS[this.themeService.preference()]);
  protected readonly themeLabel = computed(() => THEME_NEXT_LABELS[this.themeService.preference()]);

  protected cycleTheme(): void {
    this.themeService.cycle();
  }

  protected logout(): void {
    void this.authService.logout();
  }
}
