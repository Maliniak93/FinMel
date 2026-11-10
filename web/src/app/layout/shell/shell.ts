import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';

import { AuthService } from '../../core/auth/auth';
import { PlanningState } from '../../core/planning/planning-state';
import { ThemeService, type ThemePreference } from '../../core/theme/theme';
import { LanguageSwitch } from '../../shared/language-switch/language-switch';
import { NAV_LINKS, SETTINGS_LINK } from './nav';
import { ShellLayout } from './shell-layout';

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
    NgTemplateOutlet,
    RouterLink,
    RouterOutlet,
    MatButtonModule,
    MatIconModule,
    MatSidenavModule,
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
  protected readonly layout = inject(ShellLayout).layout;

  constructor() {
    this.planningState.startSession();
  }

  protected readonly navLinks = NAV_LINKS;
  protected readonly settingsLink = SETTINGS_LINK;

  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected isActive(path: string): boolean {
    const [pathname] = this.url().split('#')[0].split('?');
    const target = `/${path}`;
    return pathname === target || pathname.startsWith(`${target}/`);
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
