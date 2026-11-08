import type { ComponentType } from '@angular/cdk/portal';
import { Component, computed, inject, linkedSignal, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { formatDate } from '../../../shared/format';
import { groupColor } from '../../../shared/colors';
import { Collapse } from '../../../shared/collapse/collapse';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { PageHeader } from '../../../shared/page-header/page-header';
import { Gain } from '../../../shared/gain/gain';
import { GroupList } from '../../../shared/group-select/group-list';
import { Money } from '../../../shared/money/money';
import { SectionError } from '../../../shared/section-error/section-error';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { StatusChip, type StatusChipTone } from '../../../shared/status-chip/status-chip';
import { ACCOUNT_TYPE } from '../../accounts/account-type';
import { AccountFormDialog } from '../../accounts/account-form-dialog/account-form-dialog';
import { ASSET_CLASS } from '../../assets/asset-class';
import { AssetFormDialog } from '../../assets/asset-form/asset-form-dialog/asset-form-dialog';
import { BondPurchaseDialog } from '../../bonds/bond-purchase-dialog/bond-purchase-dialog';
import { CashPlaceFormDialog } from '../../cash/cash-place-form-dialog/cash-place-form-dialog';
import { DepositFormDialog } from '../../deposits/deposit-form-dialog/deposit-form-dialog';
import { MetalFormDialog } from '../../metals/metal-form-dialog/metal-form-dialog';
import {
  WEALTH_SECTIONS,
  WealthData,
  sumPln,
  type WealthFlag,
  type WealthRow,
  type WealthType,
} from '../wealth-data';

interface NewAssetEntry {
  labelKey: string;
  open: () => void;
}

interface FlagView {
  labelKey: string;
  icon: string;
  tone: StatusChipTone;
}

const FLAG_VIEWS: Record<WealthFlag, FlagView> = {
  stale: { labelKey: 'common.stale', icon: 'schedule', tone: 'warning' },
  rateUnavailable: {
    labelKey: 'common.rateUnavailable',
    icon: 'currency_exchange',
    tone: 'warning',
  },
  maturity: { labelKey: 'wealth.status.maturity', icon: 'event', tone: 'warning' },
  interestDue: { labelKey: 'wealth.status.interestDue', icon: 'payments', tone: 'negative' },
};

// MatDialogModule stays out of imports: it would shadow the TestBed provider override.
@Component({
  selector: 'app-wealth-all',
  imports: [
    Collapse,
    EmptyState,
    Gain,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatSelectModule,
    MatSlideToggleModule,
    PageHeader,
    Money,
    RouterLink,
    SectionError,
    Skeleton,
    StatusChip,
    TranslocoPipe,
  ],
  templateUrl: './wealth-all.html',
  styleUrl: './wealth-all.scss',
})
export class WealthAll {
  private readonly dialog = inject(MatDialog);
  private readonly groupList = inject(GroupList);

  protected readonly data = inject(WealthData);
  protected readonly search = signal('');
  protected readonly groupFilter = signal('all');

  protected readonly expanded = linkedSignal<string, ReadonlySet<WealthType>>({
    source: () => this.search().trim().toLowerCase(),
    computation: (term) =>
      term === ''
        ? new Set<WealthType>()
        : new Set(
            untracked(() => this.data.rows())
              .filter((row) => row.name.toLowerCase().includes(term))
              .map((row) => row.type),
          ),
  });

  protected readonly groups = this.groupList.groups;
  protected readonly formatDate = formatDate;
  protected readonly groupColor = groupColor;
  protected readonly flagViews = FLAG_VIEWS;

  protected readonly selectedGroup = computed(() => {
    const filter = this.groupFilter();
    if (filter === 'all') {
      return { key: 'wealth.groupAll', name: '' };
    }
    if (filter === 'none') {
      return { key: 'wealth.groupNone', name: '' };
    }
    return { key: '', name: this.groups().find((group) => group.id === filter)?.name ?? '' };
  });

  protected readonly sections = computed(() => {
    const term = this.search().trim().toLowerCase();
    const group = this.groupFilter();
    const rows = this.data
      .rows()
      .filter(
        (row) =>
          (term === '' || row.name.toLowerCase().includes(term)) &&
          (group === 'all' || (group === 'none' ? row.groupId === null : row.groupId === group)),
      );
    return WEALTH_SECTIONS.map((section) => {
      const sectionRows = rows.filter((row) => row.type === section.type);
      return { ...section, rows: sectionRows, sum: sumPln(sectionRows) };
    }).filter((section) => section.rows.length > 0);
  });

  protected readonly newAssetEntries: readonly NewAssetEntry[] = [
    {
      labelKey: 'wealth.newAsset.account',
      open: () => this.openDialog(AccountFormDialog, '480px', { type: ACCOUNT_TYPE.Brokerage }),
    },
    {
      labelKey: 'wealth.newAsset.bank',
      open: () => this.openDialog(AccountFormDialog, '480px', { type: ACCOUNT_TYPE.Bank }),
    },
    {
      labelKey: 'wealth.newAsset.cashPlace',
      open: () => this.openDialog(CashPlaceFormDialog, '480px', { mode: 'create' }),
    },
    {
      labelKey: 'wealth.newAsset.deposit',
      open: () => this.openDialog(DepositFormDialog, '560px', {}),
    },
    {
      labelKey: 'wealth.newAsset.bond',
      open: () => this.openDialog(BondPurchaseDialog, '560px', {}),
    },
    {
      labelKey: 'wealth.newAsset.stock',
      open: () => this.openDialog(AssetFormDialog, '560px', { assetClass: ASSET_CLASS.Stock }),
    },
    {
      labelKey: 'wealth.newAsset.etf',
      open: () => this.openDialog(AssetFormDialog, '560px', { assetClass: ASSET_CLASS.Etf }),
    },
    {
      labelKey: 'wealth.newAsset.metal',
      open: () => this.openDialog(MetalFormDialog, '560px', {}),
    },
  ];

  protected isExpanded(type: WealthType): boolean {
    return this.expanded().has(type);
  }

  protected toggle(type: WealthType): void {
    this.expanded.update((current) => {
      const next = new Set(current);
      if (!next.delete(type)) {
        next.add(type);
      }
      return next;
    });
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected groupOf(row: WealthRow) {
    return row.groupId ? this.groups().find((group) => group.id === row.groupId) : undefined;
  }

  private openDialog<T>(component: ComponentType<T>, width: string, data: object): void {
    this.dialog
      .open(component, { width, data })
      .afterClosed()
      .subscribe((result: unknown) => {
        if (result) {
          this.data.reload();
        }
      });
  }
}
