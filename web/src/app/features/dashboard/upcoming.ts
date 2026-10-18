import type { AccountResponse, BondResponse, DepositResponse } from '../../api/portfolio';
import { fromDateOnly, toDateOnly } from '../../shared/date-only';
import { BOND_STATUS } from '../bonds/bond-interest';
import { DEPOSIT_STATUS } from '../deposits/deposit-terms';
import { assetDeadlines } from './asset-deadlines';

const HORIZON_DAYS = 30;

export type UpcomingKind =
  | 'depositDue'
  | 'depositMaturing'
  | 'bondMatured'
  | 'bondMaturing'
  | 'bondInterestDue'
  | 'bankInterestDue';

export interface UpcomingItem {
  name: string;
  kind: UpcomingKind;
  actionNeeded: boolean;
  date: string | null;
  count: number | null;
  amount: number | string | null;
  currency: string;
  link: '/deposits' | '/bonds' | '/bank-accounts';
}

function isActive(item: { isArchived: boolean }): boolean {
  return !item.isArchived;
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
  bankAccounts: AccountResponse[],
  today: string,
): UpcomingItem[] {
  const horizonDate = fromDateOnly(today);
  horizonDate.setDate(horizonDate.getDate() + HORIZON_DAYS);
  const horizon = toDateOnly(horizonDate);

  const items: UpcomingItem[] = [];

  for (const deposit of deposits.filter(isActive)) {
    if (Number(deposit.status) === DEPOSIT_STATUS.Due) {
      items.push({
        name: deposit.name,
        kind: 'depositDue',
        actionNeeded: true,
        date: deposit.maturityDate,
        count: null,
        amount: deposit.projection.finalAmount,
        currency: deposit.currency,
        link: '/deposits',
      });
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
    }
  }

  for (const deadline of assetDeadlines(deposits, bonds, today, horizon)) {
    if (deadline.kind === 'deposit' && deadline.status === DEPOSIT_STATUS.Active) {
      items.push({
        name: deadline.name,
        kind: 'depositMaturing',
        actionNeeded: false,
        date: deadline.date,
        count: null,
        amount: deadline.amount,
        currency: deadline.currency,
        link: deadline.link,
      });
    } else if (deadline.kind === 'bond' && deadline.status === BOND_STATUS.Active) {
      items.push({
        name: deadline.name,
        kind: 'bondMaturing',
        actionNeeded: false,
        date: deadline.date,
        count: null,
        amount: deadline.amount,
        currency: deadline.currency,
        link: deadline.link,
      });
    }
  }

  for (const account of bankAccounts.filter(isActive)) {
    if (account.bank?.interestDue) {
      items.push({
        name: account.name,
        kind: 'bankInterestDue',
        actionNeeded: true,
        date: null,
        count: Number(account.bank.duePeriodCount),
        amount: null,
        currency: account.currency,
        link: '/bank-accounts',
      });
    }
  }

  return items.sort(compareItems);
}
