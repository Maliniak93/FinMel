import { polishProblems, restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { formatMoney } from '../../../shared/format';
import { cashPlacesResponse, jsonResponse, renderCard } from '../testing/dashboard-fixtures';
import { CashCard } from './cash-card';

describe('CashCard', () => {
  afterEach(async () => {
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  it('shows totals per currency', async () => {
    const { fixture } = await renderCard(CashCard, {
      cash: jsonResponse(
        cashPlacesResponse(
          [
            { currency: 'PLN', balance: 1234.5 },
            { currency: 'EUR', balance: 200 },
          ],
          1634.5,
        ),
      ),
    });
    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';

    expect(text).toContain(formatMoney(1234.5, 'PLN'));
    expect(text).toContain(formatMoney(200, 'EUR'));
    expect(element.querySelector('.cash-card__total')?.textContent).toContain(formatMoney(1634.5));
    expect(element.querySelector('a[href="/wealth/cash"]')).not.toBeNull();
  });

  it('lists currencies with the PLN equivalent and a Razem row', async () => {
    const { fixture } = await renderCard(CashCard, {
      cash: jsonResponse({
        ...cashPlacesResponse(
          [
            { currency: 'PLN', balance: 800 },
            { currency: 'EUR', balance: 420 },
          ],
          2600,
        ),
        places: [
          {
            id: '90000000-0000-0000-0000-000000000001',
            name: 'Portfel',
            isArchived: false,
            valuePln: 2600,
            balances: [
              {
                currency: 'PLN',
                cashAssetId: '91000000-0000-0000-0000-000000000001',
                balance: 800,
                valuePln: 800,
              },
              {
                currency: 'EUR',
                cashAssetId: '91000000-0000-0000-0000-000000000002',
                balance: 420,
                valuePln: 1800,
              },
            ],
          },
        ],
      }),
    });
    await switchLanguage(fixture, 'pl');
    const element = fixture.nativeElement as HTMLElement;
    const rows = [...element.querySelectorAll('li')].map((row) =>
      (row.textContent ?? '').replace(/s+/g, ' '),
    );

    expect(element.querySelector('h3')).not.toBeNull();
    expect(element.querySelector('h2')).toBeNull();
    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain(formatMoney(800, 'PLN').replace(/s+/g, ' '));
    expect(rows[1]).toContain(formatMoney(420, 'EUR').replace(/s+/g, ' '));
    expect(rows[1]).toContain('≈');
    expect(rows[1]).toContain(formatMoney(1800).replace(/s+/g, ' '));
    expect(rows[2]).toContain('Razem');
    expect(rows[2]).toContain(formatMoney(2600).replace(/s+/g, ' '));
  });

  it('empty', async () => {
    const { fixture } = await renderCard(CashCard, {
      cash: jsonResponse(cashPlacesResponse([])),
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.cash-card__empty')).not.toBeNull();
    expect(textOf(element.querySelector('.cash-card__empty'))).not.toBe('');
  });

  it('renders in Polish', async () => {
    const { fixture } = await renderCard(CashCard, {
      cash: jsonResponse(cashPlacesResponse([])),
    });
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      textOf(element.querySelector('h3')),
      textOf(element.querySelector('.cash-card__empty')),
    ];

    const english = texts();
    expect(english.every((text) => text.length > 0)).toBe(true);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });
});
