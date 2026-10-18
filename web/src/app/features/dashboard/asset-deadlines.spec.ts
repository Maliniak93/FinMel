import { bondResponse } from '../../../testing/bond-fixtures';
import { depositResponse } from '../deposits/testing/deposit-fixtures';
import { assetDeadlines } from './asset-deadlines';

describe('assetDeadlines', () => {
  it('returns the active deposit end and the bond maturity inside the window only', () => {
    const items = assetDeadlines(
      [
        depositResponse({ name: 'Active deposit', maturityDate: '2027-03-22' }),
        depositResponse({ name: 'Archived deposit', maturityDate: '2027-03-10', isArchived: true }),
      ],
      [
        bondResponse({ name: 'March bond', maturityDate: '2027-03-30' }),
        bondResponse({ name: 'April bond', maturityDate: '2027-04-02' }),
      ],
      '2027-03-01',
      '2027-03-31',
    );

    expect(
      items
        .map((item) => ({ name: item.name, date: item.date }))
        .sort((a, b) => a.date.localeCompare(b.date)),
    ).toEqual([
      { name: 'Active deposit', date: '2027-03-22' },
      { name: 'March bond', date: '2027-03-30' },
    ]);
  });
});
