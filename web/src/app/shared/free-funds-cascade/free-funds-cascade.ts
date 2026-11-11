import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import { groupColor } from '../colors';
import { formatMoney } from '../format';
import { SplitBar, type SplitBarSegment } from '../split-bar/split-bar';

const FULL_PERCENT = 100;

export interface CascadeAmounts {
  readonly incomes: number;
  readonly expenses: number;
  readonly contributions: number;
  readonly free: number;
}

export interface CascadeLabels {
  readonly incomes: string;
  readonly expenses: string;
  readonly contributions: string;
  readonly free: string;
  readonly deficit: string;
  readonly plan?: string;
  readonly actual?: string;
}

export interface CascadeContribution {
  readonly groupId: string;
  readonly name: string;
  readonly amountPln: number | string;
}

export interface CascadeGroup {
  readonly id: string;
  readonly color?: string | null;
}

export interface CascadeSplitRow {
  readonly key: string;
  readonly label: string;
  readonly amountPln: number;
}

type ColumnKey = 'plan' | 'actual';
type RowKey = 'incomes' | 'expenses' | 'contributions' | 'free';

interface CascadeRow {
  readonly key: RowKey;
  readonly label: string;
  readonly amount: number;
  readonly percent: number;
  readonly deficit: boolean;
  readonly testId: string;
  readonly barTestId: string;
  readonly deficitTestId: string;
}

interface CascadeColumn {
  readonly key: ColumnKey;
  readonly title: string | null;
  readonly rows: readonly CascadeRow[];
}

const ROW_KEYS: readonly RowKey[] = ['incomes', 'expenses', 'contributions', 'free'];

@Component({
  selector: 'app-free-funds-cascade',
  imports: [MatIconModule, SplitBar],
  templateUrl: './free-funds-cascade.html',
  styleUrl: './free-funds-cascade.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FreeFundsCascade {
  readonly labels = input.required<CascadeLabels>();
  readonly plan = input.required<CascadeAmounts>();
  readonly actual = input<CascadeAmounts | null>(null);
  readonly contributions = input<readonly CascadeContribution[]>([]);
  readonly groups = input<readonly CascadeGroup[]>([]);
  readonly split = input<readonly CascadeSplitRow[]>([]);
  readonly deposits = input<readonly CascadeSplitRow[]>([]);
  readonly planDeficit = input<boolean | null>(null);
  readonly testIds = input<Readonly<Record<string, string>>>({});

  protected readonly formatMoney = formatMoney;

  protected readonly segments = computed<SplitBarSegment[]>(() => {
    const groups = this.groups();
    return this.contributions().map((contribution) => ({
      key: contribution.groupId,
      label: contribution.name,
      value: Number(contribution.amountPln),
      color: groupColor(groups.find((group) => group.id === contribution.groupId)?.color),
    }));
  });

  protected readonly columns = computed<CascadeColumn[]>(() => {
    const labels = this.labels();
    const plan = this.plan();
    const actual = this.actual();
    const axis = Math.max(
      ...[plan, actual].flatMap((amounts) =>
        amounts ? ROW_KEYS.map((key) => Math.abs(amounts[key])) : [],
      ),
    );
    const planDeficit = this.planDeficit();

    const build = (
      key: ColumnKey,
      amounts: CascadeAmounts,
      title: string | null,
      deficit: boolean,
    ): CascadeColumn => ({
      key,
      title,
      rows: ROW_KEYS.map((rowKey) => ({
        key: rowKey,
        label: labels[rowKey],
        amount: amounts[rowKey],
        percent:
          axis > 0 ? Math.min(FULL_PERCENT, (Math.abs(amounts[rowKey]) / axis) * FULL_PERCENT) : 0,
        deficit: rowKey === 'free' && deficit,
        testId: this.testId(`${key}-${rowKey}`),
        barTestId: `cascade-bar-${key}-${rowKey}`,
        deficitTestId: this.testId(key === 'plan' ? 'free-deficit' : 'actual-free-deficit'),
      })),
    });

    const columns = [
      build('plan', plan, actual ? (labels.plan ?? null) : null, planDeficit ?? plan.free < 0),
    ];
    if (actual) {
      columns.push(build('actual', actual, labels.actual ?? null, actual.free < 0));
    }
    return columns;
  });

  private testId(key: string): string {
    return this.testIds()[key] ?? `cascade-${key}`;
  }
}
