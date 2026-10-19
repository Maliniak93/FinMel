import {
  Component,
  computed,
  effect,
  inject,
  resource,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { startWith } from 'rxjs';

import {
  getApiPortfolioAccounts,
  postApiPortfolioAssets,
  putApiPortfolioAssetsById,
  type AssetClass,
  type AssetResponse,
} from '../../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../../core/auth/problem-details';
import { assignGroup } from '../../../../shared/group-select/assign-group';
import { ACCOUNT_TYPE } from '../../../accounts/account-type';
import { ASSET_CLASS } from '../../asset-class';
import { ASSET_FORM, INITIAL_TRANSACTION_KEY } from '../asset-form';
import { CashAssetForm } from '../forms/cash-asset-form/cash-asset-form';
import { SecurityAssetForm } from '../forms/security-asset-form/security-asset-form';

export interface AssetFormDialogData {
  assetClass: AssetClass;
  asset?: AssetResponse;
  instrumentId?: string;
  accountId?: string;
}

type AssetFormKind = 'cash' | 'security';

function formKindFor(assetClass: AssetClass): AssetFormKind {
  switch (Number(assetClass)) {
    case ASSET_CLASS.Stock:
    case ASSET_CLASS.Etf:
      return 'security';
    default:
      return 'cash';
  }
}

@Component({
  selector: 'app-asset-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    CashAssetForm,
    SecurityAssetForm,
    TranslocoPipe,
  ],
  templateUrl: './asset-form-dialog.html',
  styleUrl: './asset-form-dialog.scss',
})
export class AssetFormDialog {
  private readonly dialogRef = inject(MatDialogRef<AssetFormDialog>);
  protected readonly data = inject<AssetFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.asset;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly kind = formKindFor(this.data.assetClass);
  protected readonly choosesAccount = !this.data.asset && this.kind === 'security';

  protected readonly accountForm = new FormGroup({
    accountId: new FormControl(this.data.accountId ?? '', {
      nonNullable: true,
      validators: this.choosesAccount ? [Validators.required] : [],
    }),
  });

  protected readonly accountsResource = resource({
    params: () => (this.choosesAccount ? {} : undefined),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('assets.form.accountsLoadFailed'),
        );
      }
      return (result.data ?? []).filter(
        (account) => !account.isArchived && Number(account.type) === ACCOUNT_TYPE.Brokerage,
      );
    },
  });

  protected readonly noAccounts = computed(
    () => this.accountsResource.hasValue() && this.accountsResource.value().length === 0,
  );

  private readonly activeForm = viewChild(ASSET_FORM);

  private readonly selectedAccountId = toSignal(
    this.accountForm.controls.accountId.valueChanges.pipe(
      startWith(this.accountForm.controls.accountId.value),
    ),
    { requireSync: true },
  );

  private readonly accountDefaultGroup = computed(() => {
    if (!this.accountsResource.hasValue()) {
      return null;
    }
    const accountId = this.selectedAccountId();
    return (
      this.accountsResource.value().find((candidate) => candidate.id === accountId)?.groupId ?? null
    );
  });

  constructor() {
    effect(() => {
      const groupId = this.accountDefaultGroup();
      const group = this.activeForm()?.form.get('groupId');
      untracked(() => {
        if (this.choosesAccount && group && !group.dirty) {
          group.setValue(groupId);
        }
      });
    });
  }

  // A native submit: the FormGroups live in the child forms, so there is no [formGroup] here to raise (ngSubmit).
  protected submit(event: Event): void {
    event.preventDefault();
    void this.onSubmit();
  }

  protected async onSubmit(): Promise<void> {
    const active = this.activeForm();
    if (!active || this.submitting()) {
      return;
    }

    const blockedReason = active.submitBlockedReason?.() ?? null;
    if (blockedReason) {
      this.formError.set(blockedReason);
      return;
    }

    if (active.form.invalid || this.accountForm.invalid) {
      active.form.markAllAsTouched();
      this.accountForm.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const body = active.toBody();
    const result = this.data.asset
      ? await putApiPortfolioAssetsById({
          path: { id: this.data.asset.id },
          body,
        })
      : await postApiPortfolioAssets({
          body: this.choosesAccount
            ? { ...body, accountId: this.accountForm.controls.accountId.value }
            : body,
        });

    if (!result.error && this.data.asset) {
      const groupId: string | null = active.form.get('groupId')?.value ?? null;
      if (groupId !== (this.data.asset.groupId ?? null)) {
        const assignmentError = await assignGroup(this.data.asset.id, groupId);
        if (assignmentError) {
          this.submitting.set(false);
          this.formError.set(assignmentError);
          return;
        }
      }
    }

    this.submitting.set(false);

    if (result.error) {
      this.applyServerErrors(active.form, readProblemDetails(result.error));
      return;
    }

    this.dialogRef.close(this.data.asset ? true : result.data?.id);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  private applyServerErrors(form: FormGroup, problem: ApiProblemDetails): void {
    const mainFieldMatched = this.applyBlockFieldErrors(form, problem);
    const transaction = form.get(INITIAL_TRANSACTION_KEY);
    const transactionFieldMatched =
      transaction instanceof FormGroup &&
      this.applyInitialTransactionFieldErrors(transaction, problem);

    if (mainFieldMatched || transactionFieldMatched) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }

  private applyBlockFieldErrors(group: FormGroup, problem: ApiProblemDetails): boolean {
    let matched = applyFieldErrors(group, problem);
    for (const [name, control] of Object.entries(group.controls)) {
      if (control instanceof FormGroup && name !== INITIAL_TRANSACTION_KEY) {
        matched = this.applyBlockFieldErrors(control, problem) || matched;
      }
    }
    return matched;
  }

  private applyInitialTransactionFieldErrors(
    transaction: FormGroup,
    problem: ApiProblemDetails,
  ): boolean {
    if (!problem.errors) {
      return false;
    }

    let matched = false;
    for (const [field, messages] of Object.entries(problem.errors)) {
      if (!field.toLowerCase().includes('initialtransaction')) {
        continue;
      }
      const suffix = field.split('.').pop() ?? field;
      const controlName = Object.keys(transaction.controls).find(
        (name) => name.toLowerCase() === suffix.toLowerCase(),
      );
      if (controlName) {
        transaction.get(controlName)?.setErrors({ server: messages.join(' ') });
        matched = true;
      }
    }
    return matched;
  }
}
