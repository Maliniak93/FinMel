import { BOND_STATUS } from '../bonds/bond-interest';
import { DEPOSIT_STATUS } from '../deposits/deposit-terms';
import { depositResponse } from '../deposits/testing/deposit-fixtures';
import { savingsAccountResponse } from '../deposits/testing/savings-account-fixtures';
import { bondResponse } from '../../../testing/bond-fixtures';
import { upcomingItems } from './upcoming';

const TODAY = '2026-10-10';

function namesOf(items: ReturnType<typeof upcomingItems>): string[] {
  return items.map((item) => item.name);
}

describe('upcomingItems', () => {
  it('action needed', () => {
    const items = upcomingItems(
      [
        depositResponse({
          name: 'Due deposit',
          status: DEPOSIT_STATUS.Due,
          maturityDate: '2026-10-05',
        }),
      ],
      [
        bondResponse({
          name: 'Matured bond',
          status: BOND_STATUS.Matured,
          maturityDate: '2026-09-30',
        }),
        bondResponse({
          name: 'Interest bond',
          status: BOND_STATUS.InterestDue,
          duePeriodCount: 2,
          maturityDate: '2030-01-01',
        }),
      ],
      [savingsAccountResponse({ name: 'Savings interest', interestDue: true, duePeriodCount: 3 })],
      TODAY,
    );

    expect(items).toHaveLength(4);
    expect(items.every((item) => item.actionNeeded)).toBe(true);
    expect(items.find((item) => item.name === 'Interest bond')?.count).toBe(2);
    expect(items.find((item) => item.name === 'Savings interest')?.count).toBe(3);
  });

  it('skips archived', () => {
    const items = upcomingItems(
      [
        depositResponse({
          name: 'Archived deposit',
          status: DEPOSIT_STATUS.Due,
          isArchived: true,
        }),
        depositResponse({
          name: 'Archived maturing deposit',
          status: DEPOSIT_STATUS.Active,
          maturityDate: '2026-10-15',
          isArchived: true,
        }),
      ],
      [
        bondResponse({
          name: 'Archived-portfolio bond',
          status: BOND_STATUS.Matured,
          portfolioIsArchived: true,
        }),
      ],
      [
        savingsAccountResponse({
          name: 'Archived-portfolio savings',
          interestDue: true,
          duePeriodCount: 1,
          portfolioIsArchived: true,
        }),
      ],
      TODAY,
    );

    expect(items).toEqual([]);
  });

  it('maturities within 30 days', () => {
    const items = upcomingItems(
      [
        depositResponse({
          name: 'Deposit in 30 days',
          status: DEPOSIT_STATUS.Active,
          maturityDate: '2026-11-09',
        }),
      ],
      [
        bondResponse({
          name: 'Bond today',
          status: BOND_STATUS.Active,
          maturityDate: '2026-10-10',
        }),
        bondResponse({
          name: 'Bond in 31 days',
          status: BOND_STATUS.Active,
          maturityDate: '2026-11-10',
        }),
      ],
      [],
      TODAY,
    );

    expect(namesOf(items).sort()).toEqual(['Bond today', 'Deposit in 30 days']);
    expect(items.every((item) => !item.actionNeeded)).toBe(true);
  });

  it('ordering', () => {
    const items = upcomingItems(
      [
        depositResponse({
          name: 'Active deposit',
          status: DEPOSIT_STATUS.Active,
          maturityDate: '2026-10-20',
        }),
        depositResponse({
          name: 'Due deposit',
          status: DEPOSIT_STATUS.Due,
          maturityDate: '2026-10-05',
        }),
      ],
      [
        bondResponse({
          name: 'Bond interest',
          status: BOND_STATUS.InterestDue,
          duePeriodCount: 2,
          maturityDate: '2030-01-01',
        }),
        bondResponse({
          name: 'Active bond',
          status: BOND_STATUS.Active,
          maturityDate: '2026-10-15',
        }),
        bondResponse({
          name: 'Matured bond',
          status: BOND_STATUS.Matured,
          maturityDate: '2026-09-30',
        }),
      ],
      [savingsAccountResponse({ name: 'Savings interest', interestDue: true, duePeriodCount: 1 })],
      TODAY,
    );

    expect(namesOf(items)).toEqual([
      'Matured bond',
      'Due deposit',
      'Bond interest',
      'Savings interest',
      'Active bond',
      'Active deposit',
    ]);
  });
});
