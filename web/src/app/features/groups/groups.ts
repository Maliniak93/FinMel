import { Component, computed, inject, linkedSignal, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioAssetGroupsById,
  getApiPortfolioAssetGroups,
  getApiPortfolioAssetGroupsAssets,
  putApiPortfolioAssetGroupsAssignment,
  type AssetGroupResponse,
} from '../../api/portfolio';
import { getApiReportingDashboard } from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { groupColor } from '../../shared/colors';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { formatMoney } from '../../shared/format';
import { GroupList } from '../../shared/group-select/group-list';
import { Money } from '../../shared/money/money';
import { SectionError } from '../../shared/section-error/section-error';
import { Skeleton } from '../../shared/skeleton/skeleton';
import { SummaryStrip, type SummaryItem } from '../../shared/summary-strip/summary-strip';
import { ASSET_CLASSES, assetClassLabel } from '../assets/asset-class';
import { GroupCard } from './group-card/group-card';
import { GroupFormDialog, type GroupFormDialogData } from './group-form-dialog/group-form-dialog';
import { buildGroupMetrics, buildGroupsSummary } from './group-metrics';

const MAX_GROUPS = 12;

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-groups',
  imports: [
    MatButtonModule,
    EmptyState,
    GroupCard,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    Money,
    SectionError,
    Skeleton,
    SummaryStrip,
    TranslocoPipe,
  ],
  templateUrl: './groups.html',
  styleUrl: './groups.scss',
})
export class Groups {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly groupList = inject(GroupList);

  protected readonly selectedId = signal<string | null>(null);
  protected readonly textFilter = signal('');
  protected readonly classFilter = signal<number | null>(null);
  protected readonly saving = signal(false);

  protected readonly groupsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAssetGroups({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('groups.loadFailed'));
      }
      return result.data;
    },
  });

  protected readonly dashboardResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiReportingDashboard({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('groups.loadFailed'));
      }
      return result.data;
    },
  });

  protected readonly assetsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAssetGroupsAssets({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('groups.assets.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly groups = computed(() =>
    this.groupsResource.hasValue() ? this.groupsResource.value() : [],
  );
  protected readonly maxGroups = MAX_GROUPS;
  protected readonly atLimit = computed(() => this.groups().length >= MAX_GROUPS);
  protected readonly selectedGroup = computed(
    () => this.groups().find((group) => group.id === this.selectedId()) ?? null,
  );

  protected readonly assets = computed(() =>
    this.assetsResource.hasValue() ? this.assetsResource.value() : [],
  );

  private readonly dashboard = computed(() =>
    this.dashboardResource.hasValue() ? this.dashboardResource.value() : null,
  );

  protected readonly cards = computed(() =>
    this.groups().map((group) => ({
      group,
      metrics: buildGroupMetrics(group, this.dashboard(), this.assets()),
    })),
  );

  protected readonly summary = computed(() =>
    buildGroupsSummary(this.groups(), this.dashboard(), this.assets()),
  );

  protected readonly summaryItems = computed<SummaryItem[]>(() => {
    const summary = this.summary();
    return [
      { labelKey: 'groups.strip.grouped', amount: summary.grouped, testId: 'strip-grouped' },
      { labelKey: 'groups.strip.ungrouped', amount: summary.ungrouped, testId: 'strip-ungrouped' },
      {
        labelKey: 'groups.strip.contribution',
        amount: summary.contributionTotal,
        testId: 'strip-contribution',
        link: { labelKey: 'groups.strip.planningLink', route: '/planning' },
      },
    ];
  });

  private readonly members = computed(
    () =>
      new Set(
        this.assets()
          .filter((asset) => asset.groupId === this.selectedId())
          .map((asset) => asset.assetId),
      ),
  );

  protected readonly checked = linkedSignal<ReadonlySet<string>>(() => this.members());

  protected readonly presentClasses = computed(() => {
    const present = new Set(this.assets().map((asset) => Number(asset.assetClass)));
    return ASSET_CLASSES.filter((assetClass) => present.has(Number(assetClass.value)));
  });

  protected readonly shownAssets = computed(() => {
    const text = this.textFilter().trim().toLowerCase();
    const assetClass = this.classFilter();
    return this.assets().filter(
      (asset) =>
        (assetClass === null || Number(asset.assetClass) === assetClass) &&
        (text === '' ||
          [asset.name, asset.accountName, asset.cashPlaceName].some((value) =>
            (value ?? '').toLowerCase().includes(text),
          )),
    );
  });

  protected readonly changeCount = computed(() => {
    const checked = this.checked();
    const members = this.members();
    return (
      [...checked].filter((id) => !members.has(id)).length +
      [...members].filter((id) => !checked.has(id)).length
    );
  });

  protected readonly formatMoney = formatMoney;
  protected readonly groupColor = groupColor;
  protected readonly assetClassLabel = assetClassLabel;

  protected groupName(groupId: string | null): string {
    return this.groups().find((group) => group.id === groupId)?.name ?? '';
  }

  protected groupColorOf(groupId: string | null): string {
    return groupColor(this.groups().find((group) => group.id === groupId)?.color);
  }

  protected select(group: AssetGroupResponse): void {
    this.selectedId.set(group.id);
    this.textFilter.set('');
    this.classFilter.set(null);
  }

  protected closeAssignment(): void {
    this.selectedId.set(null);
  }

  protected onFilterInput(event: Event): void {
    this.textFilter.set((event.target as HTMLInputElement).value);
  }

  protected toggle(assetId: string, isChecked: boolean): void {
    this.checked.update((current) => {
      const next = new Set(current);
      if (isChecked) {
        next.add(assetId);
      } else {
        next.delete(assetId);
      }
      return next;
    });
  }

  protected selectAllShown(): void {
    this.checked.update((current) => {
      const next = new Set(current);
      this.shownAssets().forEach((asset) => next.add(asset.assetId));
      return next;
    });
  }

  protected openCreateDialog(): void {
    this.openFormDialog({ mode: 'create', groups: this.groups() });
  }

  protected openEditDialog(group: AssetGroupResponse): void {
    this.openFormDialog({ mode: 'edit', group, groups: this.groups() });
  }

  protected async remove(group: AssetGroupResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('groups.delete.title'),
            message: translate('groups.delete.message', {
              name: group.name,
              count: Number(group.assetCount),
            }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioAssetGroupsById({ path: { id: group.id } });
    if (result.error) {
      this.showError(result.error, 'groups.delete.failed');
      return;
    }

    if (this.selectedId() === group.id) {
      this.selectedId.set(null);
    }
    this.assetsResource.reload();
    this.reloadGroups();
  }

  protected async saveAssignment(): Promise<void> {
    const groupId = this.selectedId();
    if (groupId === null || this.saving() || this.changeCount() === 0) {
      return;
    }

    const checked = this.checked();
    const members = this.members();
    const added = [...checked].filter((id) => !members.has(id));
    const removed = [...members].filter((id) => !checked.has(id));

    this.saving.set(true);
    try {
      for (const [assetIds, target] of [
        [added, groupId],
        [removed, null],
      ] as const) {
        if (assetIds.length === 0) {
          continue;
        }

        const result = await putApiPortfolioAssetGroupsAssignment({
          body: { assetIds: [...assetIds], groupId: target },
        });
        if (result.error) {
          this.showError(result.error, 'groups.assets.saveFailed');
          break;
        }
      }
    } finally {
      this.saving.set(false);
    }

    this.assetsResource.reload();
    this.reloadGroups();
  }

  private reloadGroups(): void {
    this.groupsResource.reload();
    this.dashboardResource.reload();
    this.groupList.reload();
  }

  private openFormDialog(data: GroupFormDialogData): void {
    this.dialog
      .open(GroupFormDialog, { width: '480px', data })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.reloadGroups();
        }
      });
  }

  private showError(error: unknown, fallbackKey: string): void {
    this.snackBar.open(
      readProblemDetails(error).detail ?? translate(fallbackKey),
      translate('common.dismiss'),
    );
  }
}
