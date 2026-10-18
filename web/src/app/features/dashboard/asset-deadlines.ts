import type { BondResponse, DepositResponse } from '../../api/portfolio';

export interface AssetDeadline {
  kind: 'deposit' | 'bond';
  name: string;
  date: string;
  amount: number | string;
  currency: string;
  status: number;
  link: '/deposits' | '/bonds';
}

export function assetDeadlines(
  deposits: DepositResponse[],
  bonds: BondResponse[],
  from: string,
  to: string,
): AssetDeadline[] {
  const inWindow = (date: string) => date >= from && date <= to;
  const items: AssetDeadline[] = [];

  for (const deposit of deposits) {
    if (!deposit.isArchived && inWindow(deposit.maturityDate)) {
      items.push({
        kind: 'deposit',
        name: deposit.name,
        date: deposit.maturityDate,
        amount: deposit.projection.finalAmount,
        currency: deposit.currency,
        status: Number(deposit.status),
        link: '/deposits',
      });
    }
  }

  for (const bond of bonds) {
    if (!bond.isArchived && inWindow(bond.maturityDate)) {
      items.push({
        kind: 'bond',
        name: bond.name,
        date: bond.maturityDate,
        amount: bond.bookValue,
        currency: 'PLN',
        status: Number(bond.status),
        link: '/bonds',
      });
    }
  }

  return items;
}
