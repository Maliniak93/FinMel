import { polishProblems, restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { formatMoney } from '../../../shared/format';
import { cashAccountsResponse, jsonResponse, renderCard } from '../testing/dashboard-fixtures';
import { CashCard } from './cash-card';

describe('CashCard', () => {
  afterEach(async () => {
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  it('shows totals per currency', async () => {
    const { fixture } = await renderCard(CashCard, {
      cash: jsonResponse(
        cashAccountsResponse([
          { currency: 'PLN', balance: 1234.5 },
          { currency: 'EUR', balance: 200 },
        ]),
      ),
    });
    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';

    expect(text).toContain(formatMoney(1234.5, 'PLN'));
    expect(text).toContain(formatMoney(200, 'EUR'));
    expect(element.querySelector('a[href="/cash"]')).not.toBeNull();
  });

  it('empty', async () => {
    const { fixture } = await renderCard(CashCard, {
      cash: jsonResponse(cashAccountsResponse([])),
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.cash-card__empty')).not.toBeNull();
    expect(textOf(element.querySelector('.cash-card__empty'))).not.toBe('');
  });

  it('renders in Polish', async () => {
    const { fixture } = await renderCard(CashCard, {
      cash: jsonResponse(cashAccountsResponse([])),
    });
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      textOf(element.querySelector('h2')),
      textOf(element.querySelector('.cash-card__empty')),
    ];

    const english = texts();
    expect(english.every((text) => text.length > 0)).toBe(true);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });
});
