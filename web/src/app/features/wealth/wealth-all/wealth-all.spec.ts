import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import en from '../../../../i18n/en.json';
import { menuItemLabel, rowMenuItems, showArchived } from '../../../../testing/archive';
import { mediaBlock, readStylesheet, rulesFor } from '../../../../testing/stylesheet';
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
import { BOND_STATUS } from '../../bonds/bond-interest';
import { BondPurchaseDialog } from '../../bonds/bond-purchase-dialog/bond-purchase-dialog';
import { CashPlaceFormDialog } from '../../cash/cash-place-form-dialog/cash-place-form-dialog';
import { DepositFormDialog } from '../../deposits/deposit-form-dialog/deposit-form-dialog';
import { MetalFormDialog } from '../../metals/metal-form-dialog/metal-form-dialog';
import { formatDate, formatMoney, formatPercent, formatSignedMoney } from '../../../shared/format';
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

  function toggle(type: string): HTMLButtonElement {
    return section(type)!.querySelector<HTMLButtonElement>('button.wealth-all__toggle')!;
  }

  function toggles(): HTMLButtonElement[] {
    return Array.from(
      root().querySelectorAll<HTMLButtonElement>('[data-section] button.wealth-all__toggle'),
    );
  }

  function openRows(scope: ParentNode = root()): HTMLElement[] {
    return Array.from(scope.querySelectorAll<HTMLElement>('tr.wealth-all__row')).filter(
      (candidate) => candidate.closest('[inert]') === null,
    );
  }

  function rowCount(): number {
    return openRows().length;
  }

  async function expandAll(): Promise<void> {
    for (const button of toggles()) {
      if (button.getAttribute('aria-expanded') === 'false') {
        button.click();
      }
    }
    await settle();
  }

  function row(name: string): HTMLElement | undefined {
    return openRows().find((candidate) =>
      candidate.querySelector('td')?.textContent?.includes(name),
    );
  }

  function total(): string {
    return root().querySelector('[data-testid="total"]')?.textContent ?? '';
  }

  function columnIndex(cell: HTMLTableCellElement): number {
    let index = 0;
    for (
      let sibling = cell.previousElementSibling;
      sibling;
      sibling = sibling.previousElementSibling
    ) {
      index += (sibling as HTMLTableCellElement).colSpan;
    }
    return index;
  }

  function declared(rules: string, property: string): string | undefined {
    return rules.match(new RegExp(`(?:^|[\\s;{])${property}:\\s*([^;]+);`))?.[1].trim();
  }

  it('puts the title, the as-of line and the new asset button in the page header', async () => {
    await setup();

    const header = root().querySelector('app-page-header')!;
    expect(root().querySelectorAll('app-page-header h1')).toHaveLength(1);
    expect(header.querySelector('h1')?.textContent?.trim()).toBe('Wealth');
    expect(header.querySelector('.page-header__subtitle')?.textContent).toContain(
      `As of ${formatDate(ASOF)}`,
    );
    expect(header.querySelector('button[actions]')?.textContent).toContain('New asset');
  });

  it('renders sections and total', async () => {
    await setup();
    await expandAll();

    const order = Array.from(root().querySelectorAll('[data-section]')).map((element) =>
      element.getAttribute('data-section'),
    );
    expect(order).toEqual([...SECTION_ORDER]);
    expect(total()).toContain(formatMoney(115800));
    expect(root().textContent).toContain(`As of ${formatDate(ASOF)}`);
    expect(section('securities')!.textContent).toContain(formatMoney(14000));
    expect(section('cash')!.textContent).toContain(formatMoney(800));
    expect(root().textContent).toContain(formatSignedMoney(1150));

    const security = row('CD Projekt')!;
    expect(security.textContent).toContain('XTB IKE');
    expect(security.textContent).toContain(formatMoney(14000));
    expect(security.textContent).toContain(formatSignedMoney(500));
    expect(row('Lokata bez wyceny')!.textContent).toContain('—');
  });

  it('starts collapsed', async () => {
    await setup();

    expect(rowCount()).toBe(0);
    expect(toggles()).toHaveLength(SECTION_ORDER.length);
    for (const button of toggles()) {
      expect(button.getAttribute('aria-expanded')).toBe('false');
    }
    expect(section('securities')!.textContent).toContain(formatMoney(14000));
    expect(section('securities')!.textContent).toContain(formatSignedMoney(500));
    expect(section('bonds')!.textContent).toContain(formatSignedMoney(150));
    expect(section('metals')!.textContent).toContain(formatSignedMoney(500));
  });

  it('section toggle label comes from the translations', async () => {
    await setup();

    expect(toggle('bonds').getAttribute('aria-label')).toBe(en.wealth.section.expand);
    toggle('bonds').click();
    await settle();
    expect(toggle('bonds').getAttribute('aria-label')).toBe(en.wealth.section.collapse);
  });

  it('toggles a section', async () => {
    await setup();

    expect(toggle('bonds').tagName).toBe('BUTTON');
    toggle('bonds').click();
    await settle();
    expect(toggle('bonds').getAttribute('aria-expanded')).toBe('true');
    expect(row('EDO0935')).toBeDefined();
    expect(toggle('securities').getAttribute('aria-expanded')).toBe('false');

    toggle('bonds').click();
    await settle();
    expect(toggle('bonds').getAttribute('aria-expanded')).toBe('false');
    expect(row('EDO0935')).toBeUndefined();
  });

  it('rapid toggles end in a consistent state', async () => {
    await setup();

    toggle('bonds').click();
    toggle('bonds').click();
    await settle();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(openRows(section('bonds')!)).toHaveLength(0);
    });
    expect(toggle('bonds').getAttribute('aria-expanded')).toBe('false');

    toggle('bonds').click();
    await settle();
    expect(toggle('bonds').getAttribute('aria-expanded')).toBe('true');
    expect(openRows(section('bonds')!)).toHaveLength(1);
    expect(row('EDO0935')).toBeDefined();
  });

  it('search expands matches and resets', async () => {
    await setup();

    component['search'].set('SAN');
    await settle();

    expect(toggle('bank').getAttribute('aria-expanded')).toBe('true');
    expect(row('Santander')).toBeDefined();
    expect(row('XTB IKE')).toBeUndefined();
    expect(section('bank')!.textContent).toContain(formatMoney(45000));
    expect(section('securities')).toBeNull();
    expect(total()).toContain(formatMoney(115800));

    toggle('bank').click();
    await settle();
    expect(row('Santander')).toBeUndefined();

    component['search'].set('SANT');
    await settle();
    expect(toggle('bank').getAttribute('aria-expanded')).toBe('true');
    expect(row('Santander')).toBeDefined();

    component['search'].set('');
    await settle();
    expect(rowCount()).toBe(0);
    for (const button of toggles()) {
      expect(button.getAttribute('aria-expanded')).toBe('false');
    }
  });

  it('stock gain', async () => {
    await setup();
    await expandAll();

    const valueCell = row('CD Projekt')!.querySelectorAll('td')[4];
    const gain = valueCell.querySelector('app-gain');
    expect(gain).not.toBeNull();
    expect(gain!.querySelector('mat-icon')).toBeNull();
    expect(valueCell.textContent).toContain(formatSignedMoney(500));
    const percent = formatPercent(3.7);
    expect(
      [`(${percent})`, `(+${percent})`].some((part) => valueCell.textContent!.includes(part)),
    ).toBe(true);
  });

  it('group filter', async () => {
    await setup();
    await expandAll();

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
    await expandAll();
    expect(row('Old broker')).toBeUndefined();

    await showArchived(fixture);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(row('Old broker')).toBeDefined();
    });
    await expandAll();

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
    await expandAll();

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

  it('renders ruled rows and a total row', async () => {
    await setup();
    await expandAll();

    expect(root().querySelectorAll('tr.wealth-all__row').length).toBeGreaterThan(0);
    expect(total()).toContain(formatMoney(115800));

    const css = readStylesheet('src/app/features/wealth/wealth-all/wealth-all.scss');
    expect(rulesFor(css, /thead/)).toMatch(
      /border-bottom:\s*1px solid var\(--mat-sys-outline-variant\)/,
    );
    expect(rulesFor(css, /__row|tbody tr/)).toMatch(
      /border-bottom:\s*1px solid var\(--mat-sys-outline-variant\)/,
    );
  });

  it('section row keeps its sum in the value column', async () => {
    await setup();
    const headers = Array.from(root().querySelectorAll<HTMLTableCellElement>('thead th'));
    const valueHeader = headers.find(
      (cell) => cell.textContent?.trim() === en.wealth.column.value,
    )!;
    const actionsHeader = headers[headers.length - 1];

    const sectionSum = () =>
      Array.from(
        section('securities')!.querySelectorAll<HTMLTableCellElement>(
          'tr.wealth-all__section > th',
        ),
      ).find((cell) => cell.querySelector('app-money'))!;
    expect(columnIndex(sectionSum())).toBe(columnIndex(valueHeader));
    const trailing = sectionSum().nextElementSibling as HTMLTableCellElement;
    expect(trailing.textContent?.trim()).toBe('');
    expect(columnIndex(trailing)).toBe(columnIndex(actionsHeader));

    toggle('securities').click();
    await settle();
    const valueCell = section('securities')!.querySelector<HTMLTableCellElement>(
      'tr.wealth-all__row td.wealth-all__num',
    )!;
    expect(columnIndex(valueCell)).toBe(columnIndex(valueHeader));
    expect(columnIndex(sectionSum())).toBe(columnIndex(valueCell));
  });

  it('columns have fixed widths and right-aligned values', () => {
    const css = readStylesheet('src/app/features/wealth/wealth-all/wealth-all.scss');

    expect(css).toMatch(/\.wealth-all__table\s*\{[^}]*table-layout:\s*fixed/);
    for (const selector of [
      /__col--name|first-child/,
      /__col--place/,
      /__col--group/,
      /__col--status/,
      /__num/,
      /__actions/,
    ]) {
      expect(rulesFor(css, selector)).toMatch(/(?:^|[\s;])width:\s*\S+/);
    }
    expect(rulesFor(css, /__name|__col--place|th,\s*td/)).toMatch(/text-overflow:\s*ellipsis/);
    expect(rulesFor(css, /__num/)).toMatch(/text-align:\s*right/);
    expect(rulesFor(css, /th,\s*td/)).not.toMatch(/text-align:\s*left/);
  });

  it('section rows animate with the shared motion tokens', () => {
    const tokens = readStylesheet('src/skarbiec-tokens.css');
    expect(tokens).toMatch(/--sk-motion-duration:\s*140ms;/);
    expect(tokens).toMatch(/--sk-motion-easing:\s*cubic-bezier\(0\.35,\s*0,\s*0\.25,\s*1\);/);

    const html = readStylesheet('src/app/features/wealth/wealth-all/wealth-all.html');
    expect(html).toContain('<app-collapse');
    expect(html).not.toMatch(/animate\.(enter|leave)/);

    const css = readStylesheet('src/app/features/wealth/wealth-all/wealth-all.scss');
    expect(css).not.toMatch(/@keyframes/);
    const media = css.indexOf('@media (prefers-reduced-motion: no-preference)');
    expect(media).toBeGreaterThanOrEqual(0);
    expect(css.slice(0, media)).not.toMatch(/var\(--sk-motion-/);
    const motion = mediaBlock(css, '(prefers-reduced-motion: no-preference)');
    expect(motion).toContain('var(--sk-motion-duration)');
    expect(motion).toContain('var(--sk-motion-easing)');

    const collapse = readStylesheet('src/app/shared/collapse/collapse.scss');
    const collapseMotion = mediaBlock(collapse, '(prefers-reduced-motion: no-preference)');
    expect(collapseMotion).toContain('var(--sk-motion-duration)');
    expect(collapseMotion).toContain('var(--sk-motion-easing)');
    expect(collapse).toMatch(/grid-template-rows:\s*0fr/);
  });

  it('section header line is centred on one axis', async () => {
    await setup();
    const css = readStylesheet('src/app/features/wealth/wealth-all/wealth-all.scss');
    const button = toggle('securities');
    const line = button.parentElement!;
    const link = section('securities')!.querySelector<HTMLElement>('.wealth-all__section-link')!;

    expect(link.parentElement).toBe(line);
    const lineClass = line.classList[0];
    expect(lineClass).toBeDefined();
    const lineRules = rulesFor(css, new RegExp(`\\.${lineClass}\\s*$`));
    expect(lineRules).toMatch(/display:\s*(inline-)?flex/);
    expect(lineRules).toMatch(/align-items:\s*center/);

    expect(button.querySelector('mat-icon')).not.toBeNull();
    const chevronRules = rulesFor(css, /__toggle\s+mat-icon|__chevron/);
    const size = declared(chevronRules, 'width');
    expect(size).toBeDefined();
    expect(declared(chevronRules, 'height')).toBe(size);
    expect(size).toBe('24px');
    expect(declared(chevronRules, 'font-size')).toBe('var(--sk-text-2xl)');
  });

  it('shows interest to settle in the error tone', async () => {
    const data = wealthApiData();
    data.bonds = data.bonds.map((bond) => ({ ...bond, status: BOND_STATUS.InterestDue }));
    await setup({ data });
    await expandAll();

    const bondRow = row('EDO0935')!;
    expect(bondRow.querySelector('.status-chip--negative')?.textContent).toContain(
      'Interest to settle',
    );
    expect(bondRow.querySelector('.status-chip--warning')).toBeNull();
  });

  it('marks the place, group and status cells', async () => {
    const data = wealthApiData();
    data.bonds = data.bonds.map((bond) => ({ ...bond, status: BOND_STATUS.InterestDue }));
    await setup({ data });
    await expandAll();

    for (const column of ['place', 'group', 'status']) {
      expect(root().querySelector(`thead .wealth-all__col--${column}`), column).not.toBeNull();
    }

    const bondRow = row('EDO0935')!;
    expect(bondRow.querySelector('td.wealth-all__col--place')).not.toBeNull();
    expect(bondRow.querySelector('td.wealth-all__col--group')).not.toBeNull();
    expect(
      bondRow.querySelector('td.wealth-all__col--status .status-chip--negative')?.textContent,
    ).toContain('Interest to settle');
  });

  it('row links', async () => {
    await setup();
    await expandAll();
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
