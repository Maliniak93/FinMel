import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { menuItemLabel, rowMenuItems, showArchived } from '../../../../testing/archive';
import {
  ASOF,
  GROUP_ID,
  IDS,
  fetchedPaths,
  mockWealthApi,
  wealthApiData,
  type WealthApiOptions,
} from '../../../../testing/wealth-fixtures';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { client as reportingClient } from '../../../api/reporting/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { AccountFormDialog } from '../../accounts/account-form-dialog/account-form-dialog';
import { AssetFormDialog } from '../../assets/asset-form/asset-form-dialog/asset-form-dialog';
import { BondPurchaseDialog } from '../../bonds/bond-purchase-dialog/bond-purchase-dialog';
import { CashPlaceFormDialog } from '../../cash/cash-place-form-dialog/cash-place-form-dialog';
import { DepositFormDialog } from '../../deposits/deposit-form-dialog/deposit-form-dialog';
import { MetalFormDialog } from '../../metals/metal-form-dialog/metal-form-dialog';
import { formatDate, formatMoney, formatSignedMoney } from '../../../shared/format';
import { WealthAll } from './wealth-all';

const SECTION_ORDER = [
  'securities',
  'bonds',
  'deposits',
  'bank',
  'accounts',
  'metals',
  'cash',
] as const;

describe('WealthAll', () => {
  let fixture: ComponentFixture<WealthAll>;
  let component: WealthAll;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => fetchSpy.mockRestore());

  async function setup(options: WealthApiOptions = {}, waitForRows = true): Promise<void> {
    fetchSpy = mockWealthApi(options);
    dialog = { open: vi.fn(() => ({ afterClosed: () => of(true) })) };
    await TestBed.configureTestingModule({
      imports: [WealthAll],
      providers: [
        provideRouter([]),
        provideI18nTesting(),
        { provide: MatDialog, useValue: dialog },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(WealthAll);
    component = fixture.componentInstance;
    fixture.detectChanges();
    if (waitForRows) {
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(root().querySelectorAll('tr').length).toBeGreaterThan(5);
      });
    }
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function section(type: string): HTMLElement | null {
    return root().querySelector<HTMLElement>(`[data-section="${type}"]`);
  }

  function row(name: string): HTMLElement | undefined {
    return Array.from(root().querySelectorAll<HTMLElement>('tr')).find((candidate) =>
      candidate.querySelector('td')?.textContent?.includes(name),
    );
  }

  function total(): string {
    return root().querySelector('[data-testid="total"]')?.textContent ?? '';
  }

  it('renders sections and total', async () => {
    await setup();

    const order = Array.from(root().querySelectorAll('[data-section]')).map((element) =>
      element.getAttribute('data-section'),
    );
    expect(order).toEqual([...SECTION_ORDER]);
    expect(total()).toContain(formatMoney(115800));
    expect(root().textContent).toContain(`As of ${formatDate(ASOF)}`);
    expect(section('securities')!.textContent).toContain(formatMoney(14000));
    expect(section('cash')!.textContent).toContain(formatMoney(800));

    const security = row('CD Projekt')!;
    expect(security.textContent).toContain('XTB IKE');
    expect(security.textContent).toContain(formatMoney(14000));
    expect(security.textContent).toContain(formatSignedMoney(500));
    expect(row('Lokata bez wyceny')!.textContent).toContain('—');
  });

  it('search', async () => {
    await setup();

    component['search'].set('SAN');
    await settle();

    expect(row('Santander')).toBeDefined();
    expect(row('XTB IKE')).toBeUndefined();
    expect(section('bank')!.textContent).toContain(formatMoney(45000));
    expect(section('securities')).toBeNull();
    expect(total()).toContain(formatMoney(115800));
  });

  it('group filter', async () => {
    await setup();

    component['groupFilter'].set(GROUP_ID);
    await settle();
    expect(row('XTB IKE')).toBeDefined();
    expect(row('Santander')).toBeUndefined();

    component['groupFilter'].set('none');
    await settle();
    expect(row('XTB IKE')).toBeUndefined();
    expect(row('Santander')).toBeDefined();
  });

  it('archived toggle', async () => {
    await setup();
    expect(row('Old broker')).toBeUndefined();

    await showArchived(fixture);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(row('Old broker')).toBeDefined();
    });

    expect(row('Old Corp')).toBeDefined();
    expect(row('Old broker')!.textContent).toContain('Archived');
    expect(total()).toContain(formatMoney(117300));
  });

  const ENTRIES = [
    ['Investment account', AccountFormDialog, { type: 0 }],
    ['Bank account', AccountFormDialog, { type: 2 }],
    ['Cash place', CashPlaceFormDialog, { mode: 'create' }],
    ['Deposit', DepositFormDialog, undefined],
    ['Bond', BondPurchaseDialog, undefined],
    ['Stock', AssetFormDialog, { assetClass: 2 }],
    ['ETF', AssetFormDialog, { assetClass: 3 }],
    ['Precious metal', MetalFormDialog, undefined],
  ] as const;

  it.each(ENTRIES.map((entry, index) => [index, ...entry] as const))(
    'new asset menu: entry %i opens %s',
    async (index, _label, dialogType, data) => {
      await setup();
      const trigger = Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find(
        (button) => button.textContent?.includes('New asset'),
      )!;
      trigger.click();
      await settle();
      const items = Array.from(
        TestBed.inject(OverlayContainer)
          .getContainerElement()
          .querySelectorAll<HTMLElement>('.mat-mdc-menu-item'),
      );
      expect(items).toHaveLength(8);
      const before = fetchedPaths(fetchSpy).length;

      items[index].click();
      await settle();

      expect(dialog.open).toHaveBeenCalledTimes(1);
      const [type, config] = dialog.open.mock.calls[0] as [
        unknown,
        { data?: Record<string, unknown> },
      ];
      expect(type).toBe(dialogType);
      if (data) {
        expect(config.data).toMatchObject(data);
      }
      if (dialogType === BondPurchaseDialog) {
        expect(config.data?.['accountId']).toBeUndefined();
      }
      await vi.waitFor(() =>
        expect(fetchedPaths(fetchSpy).slice(before)).toContain('/api/portfolio/accounts'),
      );
    },
  );

  it('states: loading shows a skeleton', async () => {
    await setup({ hang: true }, false);

    expect(root().querySelector('app-skeleton')).not.toBeNull();
  });

  it('states: a failing source shows a retry error and keeps the other rows', async () => {
    await setup({ failing: ['/api/portfolio/deposits'] }, false);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root().querySelector('app-section-error')).not.toBeNull();
    });

    expect(row('Portfel')).toBeDefined();
    expect(row('Lokata Alior')).toBeUndefined();
    const before = fetchedPaths(fetchSpy).length;

    root().querySelector<HTMLButtonElement>('app-section-error button')!.click();

    await vi.waitFor(() =>
      expect(fetchedPaths(fetchSpy).slice(before)).toContain('/api/portfolio/deposits'),
    );
  });

  it('states: no assets shows the empty state with the new asset action', async () => {
    const empty = wealthApiData();
    empty.accounts = [];
    empty.cashPlaces = { places: [], totals: [], totalValuePln: 0, fxUnavailable: false };
    empty.deposits = [];
    empty.bonds = [];
    empty.securities = { holdings: [], totals: empty.securities.totals };
    empty.metals = [];
    await setup({ data: empty }, false);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root().querySelector('app-empty-state')).not.toBeNull();
    });

    expect(root().querySelector('app-empty-state')!.textContent).toContain(
      'You have no assets yet.',
    );
    expect(root().querySelector('table')).toBeNull();
    expect(root().textContent).toContain('New asset');
  });

  it('states: a filter with no match shows the empty filter message', async () => {
    await setup();

    component['search'].set('zzzz-nothing');
    await settle();

    expect(root().querySelector('app-empty-state')!.textContent).toContain(
      'Nothing matches the filters.',
    );
  });

  it('row links', async () => {
    await setup();
    const href = (name: string) =>
      row(name)!.querySelector<HTMLAnchorElement>('a')!.getAttribute('href');

    expect(href('XTB IKE')).toBe(`/wealth/accounts/${IDS.brokerage}`);
    expect(href('Portfel')).toBe('/wealth/cash');

    const items = await rowMenuItems(fixture, row('CD Projekt')!);
    const history = items.find((item) => menuItemLabel(item) === 'Transaction history')!;
    expect(history.getAttribute('href')).toBe(`/wealth/assets/${IDS.security}/transactions`);
    expect(items.map(menuItemLabel)).toContain('Open');
  });
});
