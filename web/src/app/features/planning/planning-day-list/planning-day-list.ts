import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';

import type {
  AssetGroupResponse,
  BondResponse,
  DepositResponse,
  ExpensePaymentResponse,
  GroupContributionStatus,
  IncomeOccurrenceResponse,
  OneOffExpenseResponse,
  OneOffIncomeResponse,
  PlanningMonthResponse,
} from '../../../api/portfolio';
import { groupColor } from '../../../shared/colors';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatDate, formatMoney } from '../../../shared/format';
import { StatusChip, type StatusChipTone } from '../../../shared/status-chip/status-chip';
import { type AssetDeadline, assetDeadlines } from '../../dashboard/asset-deadlines';
import { currentWarsawDate } from '../recurrence';

export type IncomeRow =
  | { oneOff: false; key: string; item: IncomeOccurrenceResponse }
  | { oneOff: true; key: string; item: OneOffIncomeResponse };

export type ExpenseRow =
  | { oneOff: false; key: string; item: ExpensePaymentResponse }
  | { oneOff: true; key: string; item: OneOffExpenseResponse };

type IncomeStatus = 'pending' | 'overdue' | 'settled' | 'skipped';

interface StatusView {
  key: string;
  icon: string;
  tone: StatusChipTone;
}

type DayRow =
  | { kind: 'income'; key: string; row: IncomeRow }
  | { kind: 'payment'; key: string; row: ExpenseRow }
  | { kind: 'deadline'; key: string; deadline: AssetDeadline };

interface Day {
  date: string;
  dayNumber: number;
  weekdayKey: string;
  today: boolean;
  rows: DayRow[];
}

const WEEKDAY_KEYS = [
  'planning.calendar.weekday.mon',
  'planning.calendar.weekday.tue',
  'planning.calendar.weekday.wed',
  'planning.calendar.weekday.thu',
  'planning.calendar.weekday.fri',
  'planning.calendar.weekday.sat',
  'planning.calendar.weekday.sun',
];

const STATUS_VIEWS: Record<IncomeStatus, StatusView> = {
  pending: { key: 'planning.calendar.status.pending', icon: 'schedule', tone: 'neutral' },
  overdue: { key: 'planning.calendar.status.overdue', icon: 'error', tone: 'negative' },
  settled: { key: 'planning.calendar.status.settled', icon: 'check_circle', tone: 'positive' },
  skipped: { key: 'planning.calendar.status.skipped', icon: 'block', tone: 'neutral' },
};

const CONTRIBUTION_STATUS_VIEWS: Record<GroupContributionStatus, StatusView> = {
  Unsettled: { key: 'planning.contribution.status.unsettled', icon: 'schedule', tone: 'neutral' },
  Partial: { key: 'planning.contribution.status.partial', icon: 'pending', tone: 'warning' },
  Settled: {
    key: 'planning.contribution.status.settled',
    icon: 'check_circle',
    tone: 'positive',
  },
};

const KIND_KEYS = {
  deposit: 'planning.calendar.kind.deposit',
  bond: 'planning.calendar.kind.bond',
} as const;

@Component({
  selector: 'app-planning-day-list',
  imports: [MatButtonModule, MatIconModule, StatusChip, TranslocoPipe],
  templateUrl: './planning-day-list.html',
  styleUrl: './planning-day-list.scss',
})
export class PlanningDayList {
  readonly month = input.required<string>();
  readonly data = input.required<PlanningMonthResponse>();
  readonly deposits = input<DepositResponse[]>([]);
  readonly bonds = input<BondResponse[]>([]);
  readonly groups = input<AssetGroupResponse[]>([]);

  readonly settle = output<IncomeRow>();
  readonly skip = output<IncomeOccurrenceResponse>();
  readonly undo = output<IncomeRow>();
  readonly settleExpense = output<ExpenseRow>();
  readonly undoExpense = output<ExpenseRow>();

  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;

  protected readonly contributionStatusViews = CONTRIBUTION_STATUS_VIEWS;

  protected readonly contributions = computed(() => this.data().expenses.groupContributions);

  protected readonly days = computed<Day[]>(() => {
    const month = this.month();
    const data = this.data();
    const byDate = new Map<string, DayRow[]>();
    const add = (date: string, row: DayRow) => {
      const rows = byDate.get(date);
      if (rows) {
        rows.push(row);
      } else {
        byDate.set(date, [row]);
      }
    };

    for (const item of data.incomes.occurrences) {
      add(item.date, {
        kind: 'income',
        key: `income-${item.recurringIncomeId}-${item.nominalMonth}`,
        row: { oneOff: false, key: item.recurringIncomeId + item.nominalMonth, item },
      });
    }

    for (const item of data.incomes.oneOffs.filter((entry) => entry.inMonth)) {
      add(item.date, {
        kind: 'income',
        key: `income-one-off-${item.id}`,
        row: { oneOff: true, key: `one-off-${item.id}`, item },
      });
    }

    for (const payment of data.expenses.payments) {
      add(payment.date, {
        kind: 'payment',
        key: `payment-${payment.recurringExpenseId}-${payment.nominalMonth}`,
        row: {
          oneOff: false,
          key: payment.recurringExpenseId + payment.nominalMonth,
          item: payment,
        },
      });
    }

    for (const oneOff of data.expenses.oneOffs.filter((entry) => entry.inMonth)) {
      add(oneOff.date, {
        kind: 'payment',
        key: `payment-one-off-${oneOff.id}`,
        row: { oneOff: true, key: `one-off-${oneOff.id}`, item: oneOff },
      });
    }

    const first = `${month}-01`;
    const last = toDateOnly(new Date(Number(month.slice(0, 4)), Number(month.slice(5, 7)), 0));
    assetDeadlines(this.deposits(), this.bonds(), first, last).forEach((deadline, index) =>
      add(deadline.date, {
        kind: 'deadline',
        key: `${deadline.kind}-${deadline.name}-${deadline.date}-${index}`,
        deadline,
      }),
    );

    const today = toDateOnly(currentWarsawDate());
    return [...byDate.keys()].sort().map((date) => ({
      date,
      dayNumber: Number(date.slice(8)),
      weekdayKey: WEEKDAY_KEYS[(fromDateOnly(date).getDay() + 6) % 7],
      today: date === today,
      rows: byDate.get(date) ?? [],
    }));
  });

  protected readonly empty = computed(
    () => this.days().length === 0 && this.contributions().length === 0,
  );

  protected statusOf(item: Pick<IncomeOccurrenceResponse, 'status' | 'overdue'>): StatusView {
    if (item.status === 'Settled') {
      return STATUS_VIEWS.settled;
    }
    if (item.status === 'Skipped') {
      return STATUS_VIEWS.skipped;
    }
    return item.overdue ? STATUS_VIEWS.overdue : STATUS_VIEWS.pending;
  }

  protected expenseStatusOf(item: Pick<ExpensePaymentResponse, 'status'>): StatusView {
    return item.status === 'Settled' ? STATUS_VIEWS.settled : STATUS_VIEWS.pending;
  }

  protected kindKey(kind: AssetDeadline['kind']): string {
    return KIND_KEYS[kind];
  }

  protected colorOf(groupId: string): string {
    return groupColor(this.groups().find((group) => group.id === groupId)?.color);
  }

  protected hasValue(value: number | string | null | undefined): value is number | string {
    return value !== null && value !== undefined;
  }

  protected negative(amount: number | string): number {
    return -Number(amount);
  }

  protected isPlanned(amount: number | string): boolean {
    return Number(amount) > 0;
  }
}
