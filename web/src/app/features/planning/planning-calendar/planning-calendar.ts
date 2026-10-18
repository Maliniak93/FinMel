import { BreakpointObserver } from '@angular/cdk/layout';
import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, output, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import {
  getApiPortfolioBonds,
  getApiPortfolioDeposits,
  type IncomeOccurrenceResponse,
  type PlanningMonthResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { PlanningState } from '../../../core/planning/planning-state';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatDate, formatMoney } from '../../../shared/format';
import { assetDeadlines } from '../../dashboard/asset-deadlines';
import { currentWarsawDate } from '../recurrence';
import {
  SettleOccurrenceDialog,
  type SettleOccurrenceDialogData,
} from '../settle-occurrence-dialog/settle-occurrence-dialog';

const MAX_ITEMS_IN_CELL = 3;
const NARROW_VIEWPORT = '(max-width: 599.98px)';

type CalendarItemKind = 'income' | 'payment' | 'deposit' | 'bond';
type CalendarItemStatus = 'pending' | 'overdue' | 'settled' | 'skipped';

interface CalendarItem {
  key: string;
  kind: CalendarItemKind;
  name: string;
  status: CalendarItemStatus | null;
  amount: number | string;
  currency: string;
  actualDate: string | null;
  settleable: IncomeOccurrenceResponse | null;
}

interface CalendarCell {
  date: string | null;
  day: number;
  dimmed: boolean;
  today: boolean;
  items: CalendarItem[];
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

const STATUS_KEYS: Record<CalendarItemStatus, string> = {
  pending: 'planning.calendar.status.pending',
  overdue: 'planning.calendar.status.overdue',
  settled: 'planning.calendar.status.settled',
  skipped: 'planning.calendar.status.skipped',
};

const KIND_KEYS: Record<CalendarItemKind, string> = {
  income: 'planning.calendar.kind.income',
  payment: 'planning.calendar.kind.payment',
  deposit: 'planning.calendar.kind.deposit',
  bond: 'planning.calendar.kind.bond',
};

function incomeStatus(occurrence: IncomeOccurrenceResponse): CalendarItemStatus {
  if (occurrence.status === 'Settled') {
    return 'settled';
  }
  if (occurrence.status === 'Skipped') {
    return 'skipped';
  }
  return occurrence.overdue ? 'overdue' : 'pending';
}

// MatDialogModule stays out of imports: it would shadow the TestBed provider override.
@Component({
  selector: 'app-planning-calendar',
  imports: [NgTemplateOutlet, MatButtonModule, TranslocoPipe],
  templateUrl: './planning-calendar.html',
  styleUrl: './planning-calendar.scss',
})
export class PlanningCalendar {
  private readonly dialog = inject(MatDialog);
  private readonly planningState = inject(PlanningState);

  readonly month = input.required<string>();
  readonly data = input.required<PlanningMonthResponse>();
  readonly saved = output<void>();

  private readonly depositsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioDeposits({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('planning.calendar.deadlinesFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  private readonly bondsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioBonds({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('planning.calendar.deadlinesFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly narrow = toSignal(
    inject(BreakpointObserver)
      .observe(NARROW_VIEWPORT)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  protected readonly deadlinesFailed = computed(
    () => !!this.depositsResource.error() || !!this.bondsResource.error(),
  );

  protected readonly weekdayKeys = WEEKDAY_KEYS;
  protected readonly maxItemsInCell = MAX_ITEMS_IN_CELL;

  private readonly selectedDate = signal<string | null>(null);

  private readonly itemsByDate = computed(() => {
    const month = this.month();
    const data = this.data();
    const byDate = new Map<string, CalendarItem[]>();
    const add = (date: string, item: CalendarItem) => {
      const items = byDate.get(date);
      if (items) {
        items.push(item);
      } else {
        byDate.set(date, [item]);
      }
    };

    for (const occurrence of data.incomes.occurrences) {
      const status = incomeStatus(occurrence);
      const settled = status === 'settled';
      add(occurrence.date, {
        key: `income-${occurrence.recurringIncomeId}-${occurrence.nominalMonth}`,
        kind: 'income',
        name: occurrence.name,
        status,
        amount: settled ? (occurrence.actualAmount ?? occurrence.amount) : occurrence.amount,
        currency: occurrence.currency,
        actualDate: settled ? (occurrence.actualDate ?? null) : null,
        settleable: status === 'pending' || status === 'overdue' ? occurrence : null,
      });
    }

    for (const payment of data.expenses.payments) {
      add(payment.date, {
        key: `payment-${payment.recurringExpenseId}-${payment.nominalMonth}`,
        kind: 'payment',
        name: payment.name,
        status: null,
        amount: payment.amount,
        currency: 'PLN',
        actualDate: null,
        settleable: null,
      });
    }

    const deposits = this.depositsResource.hasValue() ? this.depositsResource.value() : [];
    const bonds = this.bondsResource.hasValue() ? this.bondsResource.value() : [];
    const first = `${month}-01`;
    const last = toDateOnly(new Date(Number(month.slice(0, 4)), Number(month.slice(5, 7)), 0));
    assetDeadlines(deposits, bonds, first, last).forEach((deadline, index) =>
      add(deadline.date, {
        key: `${deadline.kind}-${deadline.name}-${deadline.date}-${index}`,
        kind: deadline.kind,
        name: deadline.name,
        status: null,
        amount: deadline.amount,
        currency: deadline.currency,
        actualDate: null,
        settleable: null,
      }),
    );

    return byDate;
  });

  protected readonly cells = computed<CalendarCell[]>(() => {
    const month = this.month();
    const firstDay = fromDateOnly(`${month}-01`);
    const daysInMonth = new Date(firstDay.getFullYear(), firstDay.getMonth() + 1, 0).getDate();
    const leading = (firstDay.getDay() + 6) % 7;
    const total = Math.ceil((leading + daysInMonth) / 7) * 7;
    const nonBusinessDays = new Set<string>(this.data().nonBusinessDays);
    const today = toDateOnly(currentWarsawDate());
    const itemsByDate = this.itemsByDate();

    return Array.from({ length: total }, (_, index) => {
      const day = index - leading + 1;
      if (day < 1 || day > daysInMonth) {
        return { date: null, day: 0, dimmed: false, today: false, items: [] };
      }

      const date = `${month}-${String(day).padStart(2, '0')}`;
      return {
        date,
        day,
        dimmed: nonBusinessDays.has(date),
        today: date === today,
        items: itemsByDate.get(date) ?? [],
      };
    });
  });

  protected readonly selectedDay = computed(() => {
    const date = this.selectedDate();
    return date?.startsWith(`${this.month()}-`) ? date : null;
  });

  protected readonly selectedItems = computed(() => {
    const date = this.selectedDay();
    return date ? (this.itemsByDate().get(date) ?? []) : [];
  });

  protected readonly formatDate = formatDate;
  protected readonly formatMoney = formatMoney;

  protected statusKey(status: CalendarItemStatus): string {
    return STATUS_KEYS[status];
  }

  protected kindKey(kind: CalendarItemKind): string {
    return KIND_KEYS[kind];
  }

  protected select(date: string | null): void {
    if (date) {
      this.selectedDate.set(date);
    }
  }

  protected openSettleDialog(occurrence: IncomeOccurrenceResponse, event: Event): void {
    event.stopPropagation();
    const data: SettleOccurrenceDialogData = { occurrence };
    const ref = this.dialog.open(SettleOccurrenceDialog, { width: '420px', data });
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.planningState.reload();
        this.saved.emit();
      }
    });
  }
}
