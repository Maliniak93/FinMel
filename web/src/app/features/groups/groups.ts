import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, inject, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioAssetGroupsById,
  getApiPortfolioAssetGroups,
  getApiPortfolioAssetGroupsAssets,
  type AssetGroupResponse,
} from '../../api/portfolio';
import { getApiReportingDashboard } from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { GroupList } from '../../shared/group-select/group-list';
import { Money } from '../../shared/money/money';
import { SectionError } from '../../shared/section-error/section-error';
import { Skeleton } from '../../shared/skeleton/skeleton';
import { SummaryStrip, type SummaryItem } from '../../shared/summary-strip/summary-strip';
import { GroupAssetsPanel } from './group-assets-panel/group-assets-panel';
import { GroupCard } from './group-card/group-card';
import { GroupFormDialog, type GroupFormDialogData } from './group-form-dialog/group-form-dialog';
import { buildGroupMetrics, buildGroupsSummary } from './group-metrics';

const MAX_GROUPS = 12;
const SIDE_PANEL_QUERY = '(min-width: 960px)';

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-groups',
  imports: [
    MatButtonModule,
    EmptyState,
    GroupAssetsPanel,
    GroupCard,
    MatIconModule,
    MatSidenavModule,
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

  private readonly layout = toSignal(inject(BreakpointObserver).observe(SIDE_PANEL_QUERY), {
    requireSync: true,
  });

  protected readonly panel = signal<{ groupId: string | null } | null>(null);
  protected readonly panelMode = computed<'side' | 'over'>(() =>
    this.layout().matches ? 'side' : 'over',
  );

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

  protected openAssets(groupId: string | null): void {
    this.panel.set({ groupId });
  }

  protected closePanel(): void {
    this.panel.set(null);
  }

  protected onAssignmentSaved(complete: boolean): void {
    if (complete) {
      this.closePanel();
    }
    this.assetsResource.reload();
    this.reloadGroups();
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

    if (this.panel()?.groupId === group.id) {
      this.closePanel();
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
