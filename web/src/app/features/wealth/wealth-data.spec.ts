import { TestBed } from '@angular/core/testing';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { client as reportingClient } from '../../api/reporting/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { ASOF, IDS, account, mockWealthApi, wealthApiData } from '../../../testing/wealth-fixtures';
import { BOND_STATUS } from '../bonds/bond-interest';
import { WealthData } from './wealth-data';

describe('WealthData', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    fetchSpy = mockWealthApi();
    TestBed.configureTestingModule({ providers: [provideI18nTesting()] });
  });

  afterEach(() => fetchSpy.mockRestore());

  async function loaded(): Promise<WealthData> {
    const data = TestBed.inject(WealthData);
    await vi.waitFor(() => expect(data.rows().length).toBeGreaterThan(5));
    return data;
  }

  it('missing and stale values', async () => {
    const data = await loaded();
    const byId = (id: string) => data.rows().find((row) => row.assetId === id || row.id === id)!;

    expect(byId(IDS.depositUnvalued).valuePln).toBeNull();
    expect(byId(IDS.metal).flags).toContain('stale');
    expect(byId(IDS.foreignJar).valuePln).toBeNull();
    expect(byId(IDS.foreignJar).flags).toContain('rateUnavailable');

    const valued = data
      .rows()
      .filter((row) => row.valuePln !== null)
      .reduce((sum, row) => sum + Number(row.valuePln), 0);
    expect(valued).toBe(115800);
  });

  it('values every type from its source', async () => {
    const data = await loaded();
    const value = (id: string) =>
      Number(data.rows().find((row) => row.id === id || row.assetId === id)!.valuePln);

    expect(value(IDS.brokerage)).toBe(2000);
    expect(value(IDS.bank)).toBe(45000);
    expect(value(IDS.wallet)).toBe(800);
    expect(value(IDS.deposit)).toBe(50000);
    expect(value(IDS.bond)).toBe(1000);
    expect(value(IDS.security)).toBe(14000);
    expect(value(IDS.metal)).toBe(3000);
  });

  it('leaves archived accounts and holdings out until showArchived is on', async () => {
    const data = await loaded();
    const names = () => data.rows().map((row) => row.name);

    expect(names()).not.toContain('Old broker');
    expect(names()).not.toContain('Old Corp');

    data.showArchived.set(true);
    await vi.waitFor(() => expect(names()).toContain('Old broker'));

    expect(names()).toContain('Old Corp');
    expect(data.rows().filter((row) => row.isArchived).length).toBe(2);
  });

  it('omits flagged bank accounts', async () => {
    const flaggedCashId = 'b5555555-5555-5555-5555-555555555555';
    const data = wealthApiData();
    data.accounts.push(
      account({
        id: '55555555-5555-5555-5555-555555555555',
        name: 'Revolut ROR',
        type: 2,
        cashBalance: 300,
        cashBalancePln: 300,
        outsideNetWorth: true,
        groupId: null,
        cashAssetId: flaggedCashId,
      }),
    );
    data.dashboard.byAsset.push({
      assetId: flaggedCashId,
      valuePln: 300,
      priceDate: null,
      isStale: false,
    });
    fetchSpy.mockRestore();
    fetchSpy = mockWealthApi({ data });

    const wealth = await loaded();

    expect(wealth.rows().map((row) => row.name)).not.toContain('Revolut ROR');
    expect(wealth.typeSums().bank).toBe(45000);
    expect(wealth.total()).toBe(115800);
  });

  it('bond and metal gains', async () => {
    const data = wealthApiData();
    const COI_ID = 'b0000000-0000-0000-0000-000000000002';
    const REDEEMED_ID = 'b0000000-0000-0000-0000-000000000003';
    const SILVER_ID = 'm0000000-0000-0000-0000-000000000002';
    data.bonds[0] = { ...data.bonds[0], costPln: 10000, paidInterestPln: 0 };
    data.bonds.push(
      {
        ...data.bonds[0],
        assetId: COI_ID,
        name: 'COI0928',
        seriesCode: 'COI0928',
        costPln: 10000,
        paidInterestPln: 150,
      },
      {
        ...data.bonds[0],
        assetId: REDEEMED_ID,
        name: 'EDO0730',
        seriesCode: 'EDO0730',
        costPln: 0,
        paidInterestPln: 0,
        status: BOND_STATUS.Redeemed,
      },
    );
    data.metals[0] = { ...data.metals[0], costPln: 2000 };
    data.metals.push({ ...data.metals[0], assetId: SILVER_ID, name: 'Srebro', costPln: null });
    data.dashboard.byAsset.push(
      { assetId: IDS.bond, valuePln: 10450, priceDate: null, isStale: false },
      { assetId: COI_ID, valuePln: 10200, priceDate: null, isStale: false },
      { assetId: REDEEMED_ID, valuePln: 0, priceDate: null, isStale: false },
      { assetId: IDS.metal, valuePln: 2300, priceDate: ASOF, isStale: false },
      { assetId: SILVER_ID, valuePln: 500, priceDate: null, isStale: false },
    );
    fetchSpy.mockRestore();
    fetchSpy = mockWealthApi({ data });

    const wealth = await loaded();
    const byName = (name: string) => wealth.rows().find((row) => row.name === name)!;

    expect(byName('EDO0935').gain).toEqual({ amount: 45000, percent: 4.5 });
    expect(byName('EDO0935').cost).toBe(1000000);
    expect(byName('COI0928').gain).toEqual({ amount: 35000, percent: 3.5 });
    expect(byName('Krugerrand').gain).toEqual({ amount: 30000, percent: 15 });
    expect(byName('Srebro').gain).toBeNull();
    expect(byName('Srebro').cost).toBeNull();
    expect(byName('EDO0730').gain).toBeNull();
    expect(byName('EDO0730').cost).toBeNull();
  });

  it('gain sums', async () => {
    const data = await loaded();

    expect(data.sectionGain('securities')).toEqual({ amount: 50000, percent: 3.7 });
    expect(data.sectionGain('bonds')).toEqual({ amount: 15000, percent: 16.67 });
    expect(data.sectionGain('metals')).toEqual({ amount: 50000, percent: 20 });
    for (const type of ['bank', 'accounts', 'deposits', 'cash'] as const) {
      expect(data.sectionGain(type), type).toBeNull();
    }
    expect(data.totalGain()).toEqual({ amount: 115000, percent: 6.8 });
  });
});
