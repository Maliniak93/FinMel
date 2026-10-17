import {
  Component,
  PendingTasks,
  computed,
  effect,
  inject,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiReportingTargetAllocation,
  putApiReportingTargetAllocation,
  type TargetAllocationGroupResponse,
} from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { LanguageService } from '../../core/i18n/language';
import {
  formatDate,
  formatMoney,
  formatPercent,
  formatQuantity,
  formatSignedMoney,
  formatSignedPoints,
} from '../../shared/format';
import { ASSET_CLASS, ASSET_CLASSES, assetClassLabel } from '../assets/asset-class';

interface GroupControls {
  name: FormControl<string>;
  assetClasses: FormControl<number[]>;
  targetPercent: FormControl<number | null>;
  bandPp: FormControl<number | null>;
}

type GroupForm = FormGroup<GroupControls>;

const DEFAULT_BAND_PP = 5;

const PROPOSAL = [
  { nameKey: 'allocation.proposal.equities', classes: [ASSET_CLASS.Stock, ASSET_CLASS.Etf] },
  { nameKey: 'allocation.proposal.bonds', classes: [ASSET_CLASS.Bond, ASSET_CLASS.Deposit] },
  { nameKey: 'allocation.proposal.metals', classes: [ASSET_CLASS.PreciousMetal] },
] as const;

function notBlank(control: AbstractControl): ValidationErrors | null {
  return String(control.value ?? '').trim() === '' ? { blank: true } : null;
}

function oneDecimal(control: AbstractControl): ValidationErrors | null {
  const value = control.value as number | null;
  if (value === null || value === undefined) {
    return null;
  }
  const tenths = Number(value) * 10;
  return Math.abs(tenths - Math.round(tenths)) < 1e-6 ? null : { decimals: true };
}

function uniqueNames(array: AbstractControl): ValidationErrors | null {
  const names = (array as FormArray<GroupForm>).controls
    .map((group) => group.controls.name.value.trim().toLowerCase())
    .filter((name) => name !== '');
  return new Set(names).size === names.length ? null : { duplicateNames: true };
}

function newGroup(
  name: string,
  assetClasses: number[],
  targetPercent: number | null,
  bandPp: number,
): GroupForm {
  const percent = [Validators.required, Validators.min(0), Validators.max(100), oneDecimal];
  return new FormGroup<GroupControls>({
    name: new FormControl(name, {
      nonNullable: true,
      validators: [Validators.required, notBlank],
    }),
    assetClasses: new FormControl(assetClasses, {
      nonNullable: true,
      validators: [Validators.required],
    }),
    targetPercent: new FormControl<number | null>(targetPercent, { validators: percent }),
    bandPp: new FormControl<number | null>(bandPp, { validators: percent }),
  });
}

@Component({
  selector: 'app-allocation',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTooltipModule,
    TranslocoPipe,
  ],
  templateUrl: './allocation.html',
  styleUrl: './allocation.scss',
})
export class Allocation {
  private readonly language = inject(LanguageService).language;
  private readonly pendingTasks = inject(PendingTasks);

  protected readonly allocation = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiReportingTargetAllocation({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('allocation.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly rows = new FormArray<GroupForm>([], { validators: [uniqueNames] });
  protected readonly form = new FormGroup({ groups: this.rows });

  private readonly formChange = toSignal(this.rows.valueChanges, { initialValue: this.rows.value });
  private readonly proposalKeys = new Map<GroupForm, string>();

  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);

  protected readonly assetClasses = ASSET_CLASSES;
  protected readonly assetClassLabel = assetClassLabel;
  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;
  protected readonly formatSignedMoney = formatSignedMoney;
  protected readonly formatSignedPoints = formatSignedPoints;
  protected readonly formatDate = formatDate;

  protected readonly showEditor = computed(() => {
    if (!this.allocation.hasValue()) {
      return false;
    }
    return !this.allocation.value().isConfigured || this.editing();
  });

  protected readonly showAmounts = computed(() => {
    if (!this.allocation.hasValue()) {
      return false;
    }
    const data = this.allocation.value();
    return data.asOf !== null && Number(data.basePln) > 0;
  });

  protected readonly showOutside = computed(
    () =>
      this.allocation.hasValue() &&
      this.allocation.value().asOf !== null &&
      Number(this.allocation.value().outsidePln) !== 0,
  );

  private readonly selectedByRow = computed(() => {
    this.formChange();
    return this.rows.controls.map((group) => group.controls.assetClasses.value);
  });

  protected readonly editorRows = computed(() => {
    this.formChange();
    const data = this.allocation.hasValue() ? this.allocation.value() : null;
    const groshByClass = new Map(
      (data?.byAssetClass ?? []).map((entry) => [
        Number(entry.assetClass),
        Math.round(Number(entry.valuePln) * 100),
      ]),
    );
    const sumGrosh = (classes: Iterable<number>) =>
      [...classes].reduce(
        (sum, assetClass) => sum + (groshByClass.get(Number(assetClass)) ?? 0),
        0,
      );
    const picked = this.rows.controls.map((group) => group.controls.assetClasses.value);
    const baseGrosh = sumGrosh(new Set(picked.flat()));

    return this.rows.controls.map((control, index) => {
      const grosh = sumGrosh(picked[index]);
      return {
        control,
        valuePln: grosh / 100,
        percent: baseGrosh > 0 ? Math.round((grosh * 1000) / baseGrosh) / 10 : null,
      };
    });
  });

  protected readonly sumPercent = computed(() => {
    this.formChange();
    const tenths = this.rows.controls.reduce(
      (sum, group) => sum + Math.round(Number(group.controls.targetPercent.value ?? 0) * 10),
      0,
    );
    return tenths / 10;
  });

  protected readonly canSave = computed(() => {
    this.formChange();
    return (
      !this.saving() &&
      this.rows.length > 0 &&
      this.rows.valid &&
      Math.round(this.sumPercent() * 10) === 1000
    );
  });

  constructor() {
    effect(() => {
      const data = this.allocation.hasValue() ? this.allocation.value() : null;
      if (data && !data.isConfigured && this.rows.length === 0) {
        untracked(() => this.fillProposal());
      }
    });

    effect(() => {
      this.language();
      untracked(() => this.relabelProposal());
    });
  }

  protected isTaken(index: number, assetClass: number): boolean {
    return this.selectedByRow().some((picked, i) => i !== index && picked.includes(assetClass));
  }

  protected bandLabel(group: TargetAllocationGroupResponse): string {
    return `${formatQuantity(group.bandLowPercent)}–${formatPercent(group.bandHighPercent)}`;
  }

  protected startEdit(groups: readonly TargetAllocationGroupResponse[]): void {
    this.proposalKeys.clear();
    this.rows.clear();
    for (const group of groups) {
      this.rows.push(
        newGroup(
          group.name,
          group.assetClasses.map(Number),
          Number(group.targetPercent),
          Number(group.bandPp),
        ),
      );
    }
    this.saveError.set(null);
    this.editing.set(true);
  }

  protected cancel(): void {
    this.editing.set(false);
    this.saveError.set(null);
  }

  protected addGroup(): void {
    this.rows.push(newGroup('', [], null, DEFAULT_BAND_PP));
  }

  protected removeGroup(index: number): void {
    this.proposalKeys.delete(this.rows.at(index));
    this.rows.removeAt(index);
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    const done = this.pendingTasks.add();
    try {
      const groups = this.rows.controls.map((group) => {
        const value = group.getRawValue();
        return {
          name: value.name.trim(),
          assetClasses: [...value.assetClasses].sort((a, b) => a - b),
          targetPercent: Number(value.targetPercent),
          bandPp: Number(value.bandPp),
        };
      });
      const result = await putApiReportingTargetAllocation({ body: { groups } });
      if (result.error) {
        this.saveError.set(readProblemDetails(result.error).detail ?? translate('errors.generic'));
        return;
      }
      this.editing.set(false);
      this.allocation.reload();
    } finally {
      this.saving.set(false);
      done();
    }
  }

  private fillProposal(): void {
    this.proposalKeys.clear();
    this.rows.clear();
    for (const entry of PROPOSAL) {
      const group = newGroup(translate(entry.nameKey), [...entry.classes], null, DEFAULT_BAND_PP);
      this.proposalKeys.set(group, entry.nameKey);
      this.rows.push(group);
    }
  }

  private relabelProposal(): void {
    for (const [group, nameKey] of this.proposalKeys) {
      if (group.controls.name.pristine) {
        group.controls.name.setValue(translate(nameKey));
      }
    }
  }
}
