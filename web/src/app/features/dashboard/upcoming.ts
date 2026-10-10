import type { BondResponse, DepositResponse, SavingsAccountResponse } from '../../api/portfolio';
import { fromDateOnly, toDateOnly } from '../../shared/date-only';
import { BOND_STATUS } from '../bonds/bond-interest';
import { DEPOSIT_STATUS } from '../deposits/deposit-terms';

const HORIZON_DAYS = 30;

export type UpcomingKind =
  | 'depositDue'
  | 'depositMaturing'
  | 'bondMatured'
  | 'bondMaturing'
  | 'bondInterestDue'
  | 'savingsInterestDue';

export interface UpcomingItem {
  name: string;
  kind: UpcomingKind;
  actionNeeded: boolean;
  date: string | null;
  count: number | null;
  amount: number | string | null;
  currency: string;
  link: '/deposits' | '/bonds';
}

function isActive(item: { isArchived: boolean; portfolioIsArchived: boolean }): boolean {
  return !item.isArchived && !item.portfolioIsArchived;
}

function compareItems(a: UpcomingItem, b: UpcomingItem): number {
  if (a.actionNeeded !== b.actionNeeded) {
    return a.actionNeeded ? -1 : 1;
  }
  if (a.date !== b.date) {
    if (a.date === null) {
      return 1;
    }
    if (b.date === null) {
      return -1;
    }
    return a.date < b.date ? -1 : 1;
  }
  return a.name.localeCompare(b.name);
}

export function upcomingItems(
  deposits: DepositResponse[],
  bonds: BondResponse[],
  savings: SavingsAccountResponse[],
  today: string,
): UpcomingItem[] {
  const horizonDate = fromDateOnly(today);
  horizonDate.setDate(horizonDate.getDate() + HORIZON_DAYS);
  const horizon = toDateOnly(horizonDate);
  const inWindow = (date: string) => date >= today && date <= horizon;

  const items: UpcomingItem[] = [];

  for (const deposit of deposits.filter(isActive)) {
    const status = Number(deposit.status);
    const base = {
      name: deposit.name,
      date: deposit.maturityDate,
      count: null,
      amount: deposit.projection.finalAmount,
      currency: deposit.currency,
      link: '/deposits',
    } as const;
    if (status === DEPOSIT_STATUS.Due) {
      items.push({ ...base, kind: 'depositDue', actionNeeded: true });
    } else if (status === DEPOSIT_STATUS.Active && inWindow(deposit.maturityDate)) {
      items.push({ ...base, kind: 'depositMaturing', actionNeeded: false });
    }
  }

  for (const bond of bonds.filter(isActive)) {
    const status = Number(bond.status);
    const base = {
      name: bond.name,
      amount: bond.bookValue,
      currency: 'PLN',
      link: '/bonds',
    } as const;
    if (status === BOND_STATUS.Matured) {
      items.push({
        ...base,
        kind: 'bondMatured',
        actionNeeded: true,
        date: bond.maturityDate,
        count: null,
      });
    } else if (status === BOND_STATUS.InterestDue) {
      items.push({
        ...base,
        kind: 'bondInterestDue',
        actionNeeded: true,
        date: null,
        count: Number(bond.duePeriodCount),
      });
    } else if (status === BOND_STATUS.Active && inWindow(bond.maturityDate)) {
      items.push({
        ...base,
        kind: 'bondMaturing',
        actionNeeded: false,
        date: bond.maturityDate,
        count: null,
      });
    }
  }

  for (const account of savings.filter(isActive)) {
    if (account.interestDue) {
      items.push({
        name: account.name,
        kind: 'savingsInterestDue',
        actionNeeded: true,
        date: null,
        count: Number(account.duePeriodCount),
        amount: null,
        currency: account.currency,
        link: '/deposits',
      });
    }
  }

  return items.sort(compareItems);
}
