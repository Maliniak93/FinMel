import { polishProblems, restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { bondResponse } from '../../../../testing/bond-fixtures';
import { BOND_STATUS } from '../../bonds/bond-interest';
import { DEPOSIT_STATUS } from '../../deposits/deposit-terms';
import { depositResponse } from '../../deposits/testing/deposit-fixtures';
import { bankAccountResponse } from '../../../../testing/bank-account-fixtures';
import { formatDate, formatMoney } from '../../../shared/format';
import { dateOnlyFromToday, jsonResponse, renderCard } from '../testing/dashboard-fixtures';
import { UpcomingCard } from './upcoming-card';

const EMPTY = jsonResponse([]);

describe('UpcomingCard', () => {
  afterEach(async () => {
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  it('renders items', async () => {
    const dueDeposit = depositResponse({
      name: 'Mature deposit',
      status: DEPOSIT_STATUS.Due,
      maturityDate: '2026-10-05',
    });
    const soonBond = bondResponse({
      name: 'Upcoming bond',
      status: BOND_STATUS.Active,
      maturityDate: dateOnlyFromToday(5),
      bookValue: 5000,
    });
    const interestBond = bondResponse({
      name: 'Interest bond',
      status: BOND_STATUS.InterestDue,
      duePeriodCount: 2,
      maturityDate: '2030-01-01',
    });
    const { fixture } = await renderCard(UpcomingCard, {
      deposits: jsonResponse([dueDeposit]),
      bonds: jsonResponse([soonBond, interestBond]),
      accounts: jsonResponse([
        bankAccountResponse({
          name: 'Bank interest',
          bank: { interestDue: true, duePeriodCount: 3 },
        }),
      ]),
    });
    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';
    const kinds = Array.from(element.querySelectorAll('.upcoming-card__kind')).map(textOf);

    expect(kinds).toContain('Deposit due');
    expect(kinds).toContain('Bond matures');
    expect(kinds).toContain('Bond interest due, periods: 2');
    expect(kinds).toContain('Bank interest due, months: 3');

    expect(text).toContain('Mature deposit');
    expect(text).toContain(formatDate('2026-10-05'));
    expect(text).toContain(formatMoney(dueDeposit.projection.finalAmount, 'PLN'));
    expect(text).toContain('Upcoming bond');
    expect(text).toContain(formatDate(soonBond.maturityDate));
    expect(text).toContain(formatMoney(5000, 'PLN'));
    expect(element.querySelector('a[href="/wealth/deposits"]')).not.toBeNull();
    expect(element.querySelector('a[href="/wealth/bonds"]')).not.toBeNull();
    expect(element.querySelector('a[href="/wealth/bank"]')).not.toBeNull();
  });

  it('renders the 30-day rows', async () => {
    const maturity = dateOnlyFromToday(28);
    const deposit = depositResponse({
      name: 'Lokata zapada',
      status: DEPOSIT_STATUS.Active,
      maturityDate: maturity,
    });
    const interestBond = bondResponse({
      name: 'Obligacje odsetki',
      status: BOND_STATUS.InterestDue,
      duePeriodCount: 1,
      maturityDate: '2030-01-01',
      bookValue: 5000,
    });
    const { fixture } = await renderCard(UpcomingCard, {
      deposits: jsonResponse([deposit]),
      bonds: jsonResponse([interestBond]),
      accounts: EMPTY,
    });
    const element = fixture.nativeElement as HTMLElement;
    await switchLanguage(fixture, 'pl');

    expect(textOf(element.querySelector('h2'))).toBe('Nadchodzące · 30 dni');
    const rows = Array.from(element.querySelectorAll<HTMLElement>('.upcoming-card__row'));
    expect(rows).toHaveLength(2);

    const depositRow = rows.find((row) => textOf(row).includes('Lokata zapada')) as HTMLElement;
    expect(textOf(depositRow.querySelector('.upcoming-card__date'))).toBe(formatDate(maturity));
    expect(depositRow.querySelector('a.upcoming-card__name')?.getAttribute('href')).toBe(
      '/wealth/deposits',
    );
    expect(textOf(depositRow.querySelector('.upcoming-card__kind'))).not.toBe('');

    const bondRow = rows.find((row) => textOf(row).includes('Obligacje odsetki')) as HTMLElement;
    expect(textOf(bondRow.querySelector('.upcoming-card__amount'))).toBe(
      formatMoney(5000, 'PLN').replace(/\s+/g, ' '),
    );
  });

  it('empty', async () => {
    const { fixture } = await renderCard(UpcomingCard, {
      deposits: EMPTY,
      bonds: EMPTY,
      accounts: EMPTY,
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.upcoming-card__empty')).not.toBeNull();
    expect(textOf(element.querySelector('.upcoming-card__empty'))).not.toBe('');
  });

  it('renders in Polish', async () => {
    const { fixture } = await renderCard(UpcomingCard, {
      deposits: EMPTY,
      bonds: EMPTY,
      accounts: EMPTY,
    });
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      textOf(element.querySelector('h2')),
      textOf(element.querySelector('.upcoming-card__empty')),
    ];

    const english = texts();
    expect(english.every((text) => text.length > 0)).toBe(true);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });

  it('renders kinds in Polish', async () => {
    const { fixture } = await renderCard(UpcomingCard, {
      deposits: jsonResponse([
        depositResponse({
          name: 'Mature deposit',
          status: DEPOSIT_STATUS.Due,
          maturityDate: '2026-10-05',
        }),
      ]),
      bonds: jsonResponse([
        bondResponse({
          name: 'Interest bond',
          status: BOND_STATUS.InterestDue,
          duePeriodCount: 2,
          maturityDate: '2030-01-01',
        }),
      ]),
      accounts: jsonResponse([
        bankAccountResponse({
          name: 'Bank interest',
          bank: { interestDue: true, duePeriodCount: 3 },
        }),
      ]),
    });
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => Array.from(element.querySelectorAll('.upcoming-card__kind')).map(textOf);

    const english = texts();
    expect(english).toHaveLength(3);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });

  it('renders the load error in Polish', async () => {
    const { fixture } = await renderCard(UpcomingCard, {
      deposits: jsonResponse({}, 500),
      bonds: EMPTY,
      accounts: EMPTY,
    });
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      textOf(element.querySelector('h2')),
      textOf(element.querySelector('[role="alert"] p')),
      textOf(element.querySelector('[role="alert"] button')),
    ];

    const english = texts();
    expect(english.every((text) => text.length > 0)).toBe(true);

    await switchLanguage(fixture, 'pl');
    (element.querySelector('[role="alert"] button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(polishProblems(english, texts())).toEqual([]);
  });
});
