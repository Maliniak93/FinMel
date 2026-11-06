import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { restoreEnglish, switchLanguage } from '../../../testing/i18n';
import { readStylesheet } from '../../../testing/stylesheet';
import { account, mockWealthApi, wealthApiData } from '../../../testing/wealth-fixtures';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { client as reportingClient } from '../../api/reporting/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../../shared/format';
import { WealthLayout } from './wealth-layout';

const TABS = [
  ['', 'All'],
  ['accounts', 'Investment accounts'],
  ['bank', 'Bank accounts'],
  ['cash', 'Cash'],
  ['deposits', 'Deposits'],
  ['bonds', 'Bonds'],
  ['securities', 'Stocks & ETFs'],
  ['metals', 'Precious metals'],
] as const;

const href = (path: string) => (path ? `/wealth/${path}` : '/wealth');

describe('WealthLayout', () => {
  let harness: RouterTestingHarness;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  beforeEach(async () => {
    fetchSpy = mockWealthApi();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'wealth',
            component: WealthLayout,
            children: TABS.map(([path]) => ({ path, pathMatch: 'full' as const, children: [] })),
          },
        ]),
        provideI18nTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  it.each(TABS)('renders every tab: /wealth/%s selects "%s"', async (path, label) => {
    await harness.navigateByUrl(href(path), WealthLayout);
    harness.detectChanges();
    const element = harness.routeNativeElement as HTMLElement;

    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Wealth');
    const nav = element.querySelector('nav[mat-tab-nav-bar]')!;
    expect(nav.getAttribute('aria-label')).toBe('Asset types');

    const links = Array.from(nav.querySelectorAll<HTMLAnchorElement>('a'));
    expect(links.map((a) => a.getAttribute('href'))).toEqual(TABS.map(([p]) => href(p)));
    links.forEach((a, index) => expect(a.textContent).toContain(TABS[index][1]));
    const selected = links.filter((a) => a.getAttribute('aria-selected') === 'true');
    expect(selected).toHaveLength(1);
    expect(selected[0].textContent).toContain(label);
  });

  it('renders the tab bar', async () => {
    await harness.navigateByUrl('/wealth', WealthLayout);
    harness.detectChanges();
    const element = harness.routeNativeElement as HTMLElement;
    expect(element.querySelector('nav[mat-tab-nav-bar]')).not.toBeNull();

    const styles = readStylesheet('src/styles.scss');
    const at = styles.indexOf('mat.tabs-overrides(');
    expect(at).toBeGreaterThanOrEqual(0);
    const tabs = styles.slice(at, styles.indexOf('  );', at));
    expect(tabs).toMatch(/17px/);
    expect(tabs).toMatch(/2px/);
    expect(tabs).toMatch(/1px/);
    expect(tabs).toContain('--sk-accent-stroke');
    expect(tabs).toContain('--mat-sys-outline-variant');
  });

  it('tab captions', async () => {
    await harness.navigateByUrl('/wealth/cash', WealthLayout);
    const element = harness.routeNativeElement as HTMLElement;
    const links = () => Array.from(element.querySelectorAll<HTMLAnchorElement>('nav a'));

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(links()[2].textContent).toContain(formatMoney(45000));
    });

    const captions = links().map((a) => a.textContent ?? '');
    expect(captions[0]).toContain('All');
    expect(captions[1]).toContain('2 accounts');
    expect(captions[2]).toContain(formatMoney(45000));
    expect(captions[3]).toContain(formatMoney(800));
    expect(captions[4]).toContain(formatMoney(50000));
    expect(captions[5]).toContain(formatMoney(1000));
    expect(captions[6]).toContain(formatMoney(14000));
    expect(captions[7]).toContain(formatMoney(3000));
  });

  it('shows the note under the bank tab only with an off-wealth bank account', async () => {
    const data = wealthApiData();
    data.accounts.push(
      account({
        id: '56565656-5656-5656-5656-565656565656',
        name: 'Revolut ROR',
        type: 2,
        cashBalance: 300,
        outsideNetWorth: true,
        groupId: null,
        cashAssetId: 'b5656565-5656-5656-5656-565656565656',
      }),
    );
    fetchSpy.mockRestore();
    fetchSpy = mockWealthApi({ data });

    await switchLanguage(harness.fixture, 'pl');
    await harness.navigateByUrl('/wealth/cash', WealthLayout);
    const element = harness.routeNativeElement as HTMLElement;
    const bankTab = () => element.querySelectorAll<HTMLAnchorElement>('nav a')[2];

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(bankTab().textContent).toContain(formatMoney(45000));
    });
    expect(bankTab().textContent).toContain('bez kont poza majątkiem');
  });

  it('shows no note under the bank tab without an off-wealth bank account', async () => {
    await switchLanguage(harness.fixture, 'pl');
    await harness.navigateByUrl('/wealth/cash', WealthLayout);
    const element = harness.routeNativeElement as HTMLElement;
    const bankTab = () => element.querySelectorAll<HTMLAnchorElement>('nav a')[2];

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(bankTab().textContent).toContain(formatMoney(45000));
    });
    expect(bankTab().textContent).not.toContain('bez kont poza majątkiem');
  });
});
