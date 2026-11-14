import { Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatTabsModule } from '@angular/material/tabs';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { filter } from 'rxjs';

import { activeLocale } from '../../core/i18n/language';
import { formatMoney } from '../../shared/format';
import { WEALTH_SECTIONS, WealthData, type WealthType } from './wealth-data';

interface WealthTab {
  path: string;
  label: string;
  type: WealthType | null;
}

const WEALTH_TABS: readonly WealthTab[] = [
  { path: '', label: 'netWorthChart.allClasses', type: null },
  ...WEALTH_SECTIONS.map((section) => ({
    path: section.path,
    label: section.labelKey,
    type: section.type,
  })),
];

@Component({
  selector: 'app-wealth-layout',
  imports: [MatTabsModule, RouterLink, RouterLinkActive, RouterOutlet, TranslocoPipe],
  templateUrl: './wealth-layout.html',
  styleUrl: './wealth-layout.scss',
})
export class WealthLayout {
  protected readonly data = inject(WealthData);
  protected readonly tabs = WEALTH_TABS;

  constructor() {
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.data.refresh());
  }

  protected sumCaption(type: WealthType): string {
    const sum = this.data.typeSums()[type];
    return sum === null ? '—' : formatMoney(sum);
  }

  protected accountsCountKey(): string {
    return `wealth.accountsCount.${new Intl.PluralRules(activeLocale()).select(this.data.accountsCount())}`;
  }
}
