import { polishProblems, restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { bondResponse } from '../../../../testing/bond-fixtures';
import { BOND_STATUS } from '../../bonds/bond-interest';
import { DEPOSIT_STATUS } from '../../deposits/deposit-terms';
import { depositResponse } from '../../deposits/testing/deposit-fixtures';
import { savingsAccountResponse } from '../../deposits/testing/savings-account-fixtures';
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
      savings: jsonResponse([
        savingsAccountResponse({ name: 'Savings interest', interestDue: true, duePeriodCount: 3 }),
      ]),
    });
    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';
    const kinds = Array.from(element.querySelectorAll('.upcoming-card__kind')).map(textOf);

    expect(kinds).toContain('Deposit due');
    expect(kinds).toContain('Bond matures');
    expect(kinds).toContain('Bond interest due, periods: 2');
    expect(kinds).toContain('Savings interest due, months: 3');

    expect(text).toContain('Mature deposit');
    expect(text).toContain(formatDate('2026-10-05'));
    expect(text).toContain(formatMoney(dueDeposit.projection.finalAmount, 'PLN'));
    expect(text).toContain('Upcoming bond');
    expect(text).toContain(formatDate(soonBond.maturityDate));
    expect(text).toContain(formatMoney(5000, 'PLN'));
    expect(element.querySelector('a[href="/deposits"]')).not.toBeNull();
    expect(element.querySelector('a[href="/bonds"]')).not.toBeNull();
  });

  it('empty', async () => {
    const { fixture } = await renderCard(UpcomingCard, {
      deposits: EMPTY,
      bonds: EMPTY,
      savings: EMPTY,
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.upcoming-card__empty')).not.toBeNull();
    expect(textOf(element.querySelector('.upcoming-card__empty'))).not.toBe('');
  });

  it('renders in Polish', async () => {
    const { fixture } = await renderCard(UpcomingCard, {
      deposits: EMPTY,
      bonds: EMPTY,
      savings: EMPTY,
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
      savings: jsonResponse([
        savingsAccountResponse({ name: 'Savings interest', interestDue: true, duePeriodCount: 3 }),
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
      savings: EMPTY,
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
