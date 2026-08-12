import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { AuthService } from '../../core/auth/auth';
import { ThemeService, type ThemePreference } from '../../core/theme/theme';

const THEME_ICONS: Record<ThemePreference, string> = {
  system: 'brightness_auto',
  light: 'light_mode',
  dark: 'dark_mode',
};

// What clicking the button switches *to* — cycle() moves system -> light -> dark -> system, so
// the label always names the state one click away, not the current one.
const THEME_NEXT_LABELS: Record<ThemePreference, string> = {
  system: 'Theme: System (following device) — click for Light',
  light: 'Theme: Light — click for Dark',
  dark: 'Theme: Dark — click for System',
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
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly authService = inject(AuthService);
  protected readonly themeService = inject(ThemeService);

  protected readonly navLinks = [
    { path: 'dashboard', label: 'Dashboard', icon: 'dashboard' },
    { path: 'portfolios', label: 'Portfolios', icon: 'account_balance_wallet' },
    { path: 'settings', label: 'Settings', icon: 'settings' },
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
