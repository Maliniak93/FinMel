import { polishProblems, restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { formatMoney, formatPercent } from '../../../shared/format';
import {
  ARCHIVED_ID,
  RETIREMENT_ID,
  SAVINGS_ID,
  jsonResponse,
  portfolioResponse,
  renderCard,
} from '../testing/dashboard-fixtures';
import type { DashboardResponse } from '../../../api/reporting';
import { PortfoliosCard } from './portfolios-card';

const portfolios = [
  portfolioResponse({ id: RETIREMENT_ID, name: 'Retirement' }),
  portfolioResponse({ id: SAVINGS_ID, name: 'Savings' }),
  portfolioResponse({ id: ARCHIVED_ID, name: 'Old brokerage', isArchived: true }),
];

const withArchived: DashboardResponse = {
  netWorthPln: 1500,
  asOf: '2026-08-09',
  isStale: false,
  byAssetClass: [],
  byPortfolio: [
    {
      portfolioId: SAVINGS_ID,
      valuePln: 250,
      snapshotDate: '2026-08-09',
      isStale: false,
      percentage: 16.67,
    },
    {
      portfolioId: ARCHIVED_ID,
      valuePln: 500,
      snapshotDate: '2026-08-09',
      isStale: false,
      percentage: 33.33,
    },
    {
      portfolioId: RETIREMENT_ID,
      valuePln: 750,
      snapshotDate: '2026-08-09',
      isStale: false,
      percentage: 50,
    },
  ],
};

describe('PortfoliosCard', () => {
  afterEach(async () => {
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  it('lists active portfolios with value and share', async () => {
    const { fixture } = await renderCard(PortfoliosCard, {
      portfolios: jsonResponse(portfolios),
      dashboard: jsonResponse(withArchived),
    });
    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';

    expect(text.indexOf('Retirement')).toBeGreaterThanOrEqual(0);
    expect(text.indexOf('Retirement')).toBeLessThan(text.indexOf('Savings'));
    expect(text).toContain(formatMoney(750));
    expect(text).toContain(formatPercent(50));
    expect(text).toContain(formatMoney(250));
    expect(text).toContain(formatPercent(16.67));
    expect(text).not.toContain('Old brokerage');
    expect(
      element.querySelector(`a[href="/portfolios/${RETIREMENT_ID}/assets"]`)?.textContent,
    ).toContain('Retirement');
    expect(
      element.querySelector(`a[href="/portfolios/${SAVINGS_ID}/assets"]`)?.textContent,
    ).toContain('Savings');
  });

  it('renders in Polish', async () => {
    const { fixture } = await renderCard(PortfoliosCard, {
      portfolios: jsonResponse([]),
    });
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      textOf(element.querySelector('h2')),
      textOf(element.querySelector('.portfolios-card__empty')),
    ];

    const english = texts();
    expect(english.every((text) => text.length > 0)).toBe(true);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });
});
