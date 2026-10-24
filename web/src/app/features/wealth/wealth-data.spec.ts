import { TestBed } from '@angular/core/testing';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { client as reportingClient } from '../../api/reporting/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { IDS, mockWealthApi } from '../../../testing/wealth-fixtures';
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
});
