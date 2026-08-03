import { Component, inject, resource, signal } from '@angular/core';
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
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosById,
  getApiPortfolioPortfolios,
  postApiPortfolioPortfoliosByIdArchive,
  type PortfolioResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { PortfolioFormDialog } from './portfolio-form-dialog/portfolio-form-dialog';

// MatDialog/MatSnackBar are injected as services only (never referenced as template directives),
// so MatDialogModule/MatSnackBarModule are deliberately NOT in `imports` below — importing them
// here would re-provide the real MatDialog/MatSnackBar at the component's own injector level,
// shadowing any TestBed-level override in specs (see portfolios.spec.ts).
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
  ],
  templateUrl: './portfolios.html',
  styleUrl: './portfolios.scss',
})
export class Portfolios {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly includeArchived = signal(false);
  protected readonly displayedColumns = ['name', 'currency', 'assetCount', 'status', 'actions'];

  protected readonly portfoliosResource = resource({
    params: () => ({ includeArchived: this.includeArchived() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfolios({
        query: { includeArchived: params.includeArchived },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load portfolios.');
      }
      return result.data ?? [];
    },
  });

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
            title: 'Archive this portfolio?',
            message: `"${portfolio.name}" will be hidden from your default list — nothing is deleted, and you can still see it by toggling "Show archived". Archiving can't be undone from here yet.`,
            confirmLabel: 'Archive',
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
        readProblemDetails(result.error).detail ?? 'Failed to archive portfolio.',
        'Dismiss',
      );
      return;
    }

    this.portfoliosResource.reload();
  }

  protected async remove(portfolio: PortfolioResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: 'Delete this portfolio?',
            message: `"${portfolio.name}" will be permanently deleted. This can't be undone.`,
            confirmLabel: 'Delete',
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
        readProblemDetails(result.error).detail ?? 'Failed to delete portfolio.',
        'Dismiss',
      );
      return;
    }

    this.portfoliosResource.reload();
  }
}
