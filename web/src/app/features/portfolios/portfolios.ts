import { Component, computed, inject, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  deleteApiPortfolioPortfoliosById,
  getApiPortfolioPortfolios,
  postApiPortfolioPortfoliosByIdArchive,
  postApiPortfolioPortfoliosByIdRestore,
  type PortfolioResponse,
} from '../../api/portfolio';
import { getApiReportingDashboard, type DashboardPortfolioValue } from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatMoney } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { PortfolioFormDialog } from './portfolio-form-dialog/portfolio-form-dialog';

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-portfolios',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatTableModule,
    MatTooltipModule,
    RouterLink,
    TranslocoPipe,
  ],
  templateUrl: './portfolios.html',
  styleUrl: './portfolios.scss',
})
export class Portfolios {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly includeArchived = signal(false);

  protected readonly displayedColumns = computed(() =>
    this.includeArchived()
      ? ['name', 'currency', 'totalValue', 'status', 'actions']
      : ['name', 'currency', 'totalValue', 'actions'],
  );

  protected readonly portfoliosResource = resource({
    params: () => ({ includeArchived: this.includeArchived() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfolios({
        query: { includeArchived: params.includeArchived },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('portfolios.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly dashboardResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiReportingDashboard({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('portfolios.valuesLoadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly portfolioValues = computed(() => {
    const map = new Map<string, DashboardPortfolioValue>();
    if (this.dashboardResource.hasValue()) {
      for (const entry of this.dashboardResource.value().byPortfolio) {
        map.set(entry.portfolioId, entry);
      }
    }
    return map;
  });

  protected readonly formatMoney = formatMoney;

  protected valueEntryFor(portfolioId: string): DashboardPortfolioValue | undefined {
    return this.portfolioValues().get(portfolioId);
  }

  protected descriptionTooltip(portfolio: PortfolioResponse): string | null {
    const trimmed = portfolio.description?.trim();
    if (!trimmed) {
      return null;
    }
    return trimmed.length > 200 ? `${trimmed.slice(0, 200).trimEnd()}…` : trimmed;
  }

  protected openCreateDialog(): void {
    const ref = this.dialog.open(PortfolioFormDialog, { width: '480px', data: {} });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.portfoliosResource.reload();
      }
    });
  }

  protected openEditDialog(portfolio: PortfolioResponse): void {
    const ref = this.dialog.open(PortfolioFormDialog, { width: '480px', data: { portfolio } });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.portfoliosResource.reload();
      }
    });
  }

  protected async archive(portfolio: PortfolioResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('portfolios.archive.title'),
            message: translate('portfolios.archive.message', { name: portfolio.name }),
            confirmLabel: translate('common.archive'),
          },
        })
        .afterClosed(),
    );

    if (!confirmed) {
      return;
    }

    const result = await postApiPortfolioPortfoliosByIdArchive({ path: { id: portfolio.id } });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('portfolios.archive.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAfterLifecycleChange();
  }

  protected async restore(portfolio: PortfolioResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('portfolios.restore.title'),
            message: translate('portfolios.restore.message', { name: portfolio.name }),
            confirmLabel: translate('common.restore'),
          },
        })
        .afterClosed(),
    );

    if (!confirmed) {
      return;
    }

    const result = await postApiPortfolioPortfoliosByIdRestore({ path: { id: portfolio.id } });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('portfolios.restore.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAfterLifecycleChange();
  }

  protected async remove(portfolio: PortfolioResponse): Promise<void> {
    const assetCount = Number(portfolio.assetCount);
    const message =
      assetCount > 0
        ? translate('portfolios.delete.messageWithAssets', {
            name: portfolio.name,
            count: assetCount,
          })
        : translate('portfolios.delete.message', { name: portfolio.name });
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('portfolios.delete.title'),
            message,
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );

    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioPortfoliosById({ path: { id: portfolio.id } });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('portfolios.delete.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAfterLifecycleChange();
  }

  private reloadAfterLifecycleChange(): void {
    this.portfoliosResource.reload();
    this.dashboardResource.reload();
  }
}
