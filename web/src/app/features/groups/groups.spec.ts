import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { _getAnimationsState, MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AssetGroupResponse, AssetGroupTreeItem } from '../../api/portfolio';
import type { DashboardResponse } from '../../api/reporting';
import { client as reportingClient } from '../../api/reporting/client.gen';
import { stubViewport } from '../../../testing/match-media';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../shared/format';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { Groups } from './groups';
import { GroupFormDialog } from './group-form-dialog/group-form-dialog';
import {
  archivedAssetId,
  blueGroup,
  blueGroupId,
  freeAssetId,
  greenGroup,
  greenGroupId,
  groupsDashboard,
  memberAssetId,
  otherGroupAssetId,
  skarbonki,
  treeAccountId,
  treeAccountWithThreeChildren,
  treeAccount,
  treeAsset,
  treeChild,
  treeChildOneId,
  treeChildThreeId,
  treeChildTwoId,
} from './testing/group-fixtures';

// The members view and the edit tree both read this tree: a member bond in B, a free stock and an archived bond without a group.
const defaultTree: AssetGroupTreeItem[] = [
  treeAccount(
    treeAccountId,
    'Obligacje',
    [treeChild(memberAssetId, 'Member Bond', { assetClass: 4, groupId: blueGroupId })],
    { groupId: blueGroupId },
  ),
  treeAsset(freeAssetId, 'Free Stock', { assetClass: 2 }),
  treeAsset(otherGroupAssetId, 'Cushion Cash', { assetClass: 0, groupId: greenGroupId }),
  treeAsset(archivedAssetId, 'Old Bond', { assetClass: 4, isArchived: true }),
];

const cashXId = 'c0c0c0c0-0000-0000-0000-000000000001';
const cashYId = 'c0c0c0c0-0000-0000-0000-000000000002';
const etfOneId = 'e0e0e0e0-0000-0000-0000-000000000001';
const etfTwoId = 'e0e0e0e0-0000-0000-0000-000000000002';

interface SetupOptions {
  groups?: AssetGroupResponse[];
  tree?: AssetGroupTreeItem[];
  dashboard?: DashboardResponse | 'fail';
  groupList?: 'ok' | 'fail' | 'pending';
  afterAssignment?: {
    groups: AssetGroupResponse[];
    tree: AssetGroupTreeItem[];
    dashboard: DashboardResponse;
  };
}

describe('Groups', () => {
  let fixture: ComponentFixture<Groups>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let afterClosed: unknown;
  let release: (() => void) | undefined;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
    // Material caches reduced motion per module: resolve it as enabled, as an earlier spec can.
    const viewport = stubViewport(1280);
    TestBed.runInInjectionContext(() => _getAnimationsState());
    viewport.restore();
    TestBed.resetTestingModule();
  });

  const originalMatchMedia = window.matchMedia;

  function setViewport(width: number): void {
    window.matchMedia = ((query: string) => {
      const min = /min-width:\s*([\d.]+)px/.exec(query);
      const max = /max-width:\s*([\d.]+)px/.exec(query);
      const matches = (!min || width >= Number(min[1])) && (!max || width <= Number(max[1]));
      return {
        matches,
        media: query,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
        dispatchEvent: () => false,
        onchange: null,
      };
    }) as typeof window.matchMedia;
  }

  afterEach(async () => {
    window.matchMedia = originalMatchMedia;
    release?.();
    release = undefined;
    fetchSpy.mockRestore();
    TestBed.inject(OverlayContainer).getContainerElement().replaceChildren();
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  });

  async function setup(options: SetupOptions = {}): Promise<void> {
    let groups = options.groups ?? [blueGroup, greenGroup];
    let tree = options.tree ?? defaultTree;
    let dashboard = options.dashboard ?? groupsDashboard;
    const gate =
      options.groupList === 'pending'
        ? new Promise<void>((resolve) => {
            release = resolve;
          })
        : null;
    afterClosed = true;
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      const url = requestUrl(input);
      if (url.includes('/api/reporting/dashboard')) {
        return dashboard === 'fail'
          ? jsonResponse({ detail: 'Reporting down.' }, 500)
          : jsonResponse(dashboard);
      }
      if (url.includes('/api/portfolio/asset-groups/tree')) {
        return jsonResponse(tree);
      }
      if (/\/api\/portfolio\/asset-groups\/[^/]+\/members$/.test(url)) {
        if (options.afterAssignment) {
          groups = options.afterAssignment.groups;
          tree = options.afterAssignment.tree;
          dashboard = options.afterAssignment.dashboard;
        }
        return new Response(null, { status: 204 });
      }
      if (url.includes('/api/portfolio/asset-groups')) {
        if (request.method !== 'GET') {
          return new Response(null, { status: 204 });
        }
        if (gate) {
          await gate;
        }
        return options.groupList === 'fail'
          ? jsonResponse({ detail: 'Groups down.' }, 500)
          : jsonResponse(groups);
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });

    dialog = { open: vi.fn().mockImplementation(() => ({ afterClosed: () => of(afterClosed) })) };

    await TestBed.configureTestingModule({
      imports: [Groups],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Groups);
    if (gate) {
      fixture.detectChanges();
      return;
    }
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function byTestId(root: ParentNode, id: string): HTMLElement {
    const found = root.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    if (!found) {
      throw new Error(`No [data-testid=${id}].`);
    }
    return found;
  }

  function groupCard(name: string): HTMLElement {
    const card = Array.from(
      element().querySelectorAll<HTMLElement>('[data-testid="group-card"]'),
    ).find((r) => (byTestId(r, 'group-name').textContent ?? '').trim() === name);
    if (!card) {
      throw new Error(`No card for '${name}'.`);
    }
    return card;
  }

  async function openMenuItem(
    name: string,
    itemTestId: 'edit-group' | 'delete-group',
  ): Promise<void> {
    byTestId(groupCard(name), 'group-menu').click();
    fixture.detectChanges();
    await fixture.whenStable();
    const item = TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelector<HTMLElement>(`[data-testid="${itemTestId}"]`);
    if (!item) {
      throw new Error(`No menu item ${itemTestId}.`);
    }
    item.click();
    await settle();
  }

  function deleteCalls(): number {
    return fetchSpy.mock.calls.filter((call: unknown[]) => (call[0] as Request).method === 'DELETE')
      .length;
  }

  async function selectGroup(name: string): Promise<void> {
    byTestId(groupCard(name), 'select-group').click();
    await settle();
  }

  async function openEdit(): Promise<void> {
    byTestId(panel(), 'edit-assignment').click();
    await settle();
  }

  async function toggle(name: string): Promise<void> {
    rowCheckbox(name).click();
    await settle();
  }

  function groupLists(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        (call[0] as Request).method === 'GET' &&
        /\/api\/portfolio\/asset-groups$/.test(requestUrl(call[0])),
    ).length;
  }

  it('puts the title, the add button and the counter line in the page header', async () => {
    await setup();

    const header = element().querySelector('app-page-header')!;
    expect(element().querySelectorAll('h1')).toHaveLength(1);
    expect(header.querySelector('h1')?.textContent?.trim()).toBe('Groups');
    expect(header.querySelector('button[data-testid="add-group"]')).not.toBeNull();
    expect(
      header.querySelector('.page-header__subtitle [data-testid="group-counter"]'),
    ).not.toBeNull();
  });

  it('renders groups', async () => {
    await setup();

    expect(element().querySelectorAll('[data-testid="group-card"]')).toHaveLength(2);
    const blue = groupCard('Emerytura');
    expect(byTestId(blue, 'group-value').textContent).toContain(formatMoney(4000));
    expect(byTestId(blue, 'group-count').textContent).toContain('3');
    expect(blue.querySelector('[role="progressbar"]')).toBeNull();
    const green = groupCard('Poduszka');
    expect(byTestId(green, 'group-count').textContent).toContain('1');
    expect(byTestId(green, 'group-value').textContent).toContain(formatMoney(1500));
    expect(byTestId(green, 'group-value').textContent).toContain(formatMoney(30000));
    expect(byTestId(green, 'group-progress-caption').textContent).toContain(formatMoney(28500));
    expect(element().querySelector('[data-testid="group-empty"]')).toBeNull();
    expect(byTestId(element(), 'add-group').hasAttribute('disabled')).toBe(false);
  });

  it('summary strip matches the net worth', async () => {
    await setup({
      groups: [
        { ...blueGroup, monthlyContributionPln: 500 },
        { ...greenGroup, monthlyContributionPln: 1000 },
      ],
    });

    const strip = byTestId(element(), 'summary-strip');
    expect(byTestId(strip, 'strip-grouped').textContent).toContain(formatMoney(5500));
    expect(byTestId(strip, 'strip-ungrouped').textContent).toContain(formatMoney(800));
    expect(byTestId(strip, 'strip-contribution').textContent).toContain(formatMoney(1500));
  });

  it('summary strip shows zero when nothing is ungrouped', async () => {
    await setup({
      dashboard: {
        ...groupsDashboard,
        netWorthPln: 5500,
        byGroup: groupsDashboard.byGroup.filter((entry) => entry.groupId),
      },
    });

    expect(byTestId(element(), 'strip-ungrouped').textContent).toContain(formatMoney(0));
  });

  it('renders the summary strip', async () => {
    await setup();

    const strip = byTestId(element(), 'summary-strip');
    const items = Array.from(strip.querySelectorAll<HTMLElement>('.summary-strip__item'));
    expect(items).toHaveLength(3);
    expect(byTestId(items[0], 'strip-grouped').textContent).toContain(formatMoney(5500));
    expect(byTestId(items[1], 'strip-ungrouped').textContent).toContain(formatMoney(800));
    const contribution = byTestId(items[2], 'strip-contribution');
    const link = items[2].querySelector<HTMLAnchorElement>('.summary-strip__link');
    expect(link?.getAttribute('href')).toBe('/planning');
    expect(
      contribution.compareDocumentPosition(link!) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('shows the ungrouped row only when there are ungrouped assets', async () => {
    await setup();

    const card = byTestId(element(), 'ungrouped-card');
    const list = element().querySelector('.groups-page__list')!;
    expect(list.contains(card)).toBe(false);
    expect(list.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('hides the ungrouped row when there are no ungrouped assets', async () => {
    await setup({
      tree: defaultTree.filter((item) => item.assetId !== freeAssetId),
      dashboard: {
        ...groupsDashboard,
        byGroup: groupsDashboard.byGroup.filter((entry) => entry.groupId),
      },
    });

    expect(element().querySelectorAll('[data-testid="group-card"]')).toHaveLength(2);
    expect(element().querySelector('[data-testid="ungrouped-card"]')).toBeNull();
  });

  it('ungrouped card', async () => {
    await setup();

    const card = byTestId(element(), 'ungrouped-card');
    expect(byTestId(card, 'ungrouped-value').textContent).toContain(formatMoney(800));
    expect(byTestId(card, 'ungrouped-count').textContent).toContain('1');
    expect(card.querySelector('[data-testid="group-menu"]')).toBeNull();
    expect(card.querySelector('[data-testid="select-group"]')).toBeNull();
    expect(byTestId(element(), 'group-counter').textContent).toMatch(/^\D*2\D+12\D*$/);
  });

  it('loading', async () => {
    await setup({ groupList: 'pending' });

    expect(element().querySelector('app-skeleton')).toBeTruthy();
    expect(element().querySelectorAll('[data-testid="group-card"]')).toHaveLength(0);
  });

  it('empty', async () => {
    await setup({ groups: [] });

    expect(element().querySelectorAll('[data-testid="group-card"]')).toHaveLength(0);
    expect(element().querySelector('[data-testid="summary-strip"]')).toBeNull();
    expect(element().querySelector('[data-testid="ungrouped-card"]')).toBeNull();
    expect(byTestId(element(), 'group-empty').querySelector('button')).toBeTruthy();
    expect(byTestId(element(), 'add-group').hasAttribute('disabled')).toBe(false);
  });

  it('group list error', async () => {
    await setup({ groupList: 'fail' });

    const error = element().querySelector('app-section-error');
    expect(error).toBeTruthy();
    const before = groupLists();
    error!.querySelector('button')!.click();
    await settle();
    await vi.waitFor(() => expect(groupLists()).toBeGreaterThan(before));
    expect(element().querySelectorAll('[data-testid="group-card"]')).toHaveLength(0);
  });

  it('dashboard unavailable', async () => {
    await setup({
      dashboard: 'fail',
      groups: [{ ...blueGroup, monthlyContributionPln: 500 }, greenGroup],
    });

    expect(element().querySelectorAll('[data-testid="group-card"]')).toHaveLength(2);
    expect(byTestId(groupCard('Emerytura'), 'group-unavailable')).toBeTruthy();
    expect(byTestId(groupCard('Poduszka'), 'group-unavailable')).toBeTruthy();
    expect(byTestId(groupCard('Emerytura'), 'group-contribution').textContent).toContain(
      formatMoney(500),
    );
    expect(byTestId(groupCard('Poduszka'), 'group-count').textContent).toContain('1');
    const strip = byTestId(element(), 'summary-strip');
    expect(byTestId(strip, 'strip-grouped').textContent).toContain('—');
    expect(byTestId(strip, 'strip-ungrouped').textContent).toContain('—');
    expect(byTestId(strip, 'strip-contribution').textContent).toContain(formatMoney(500));
  });

  it('opens the create dialog and reloads the list', async () => {
    await setup();
    const before = groupLists();

    byTestId(element(), 'add-group').click();
    await settle();

    expect(dialog.open).toHaveBeenCalledWith(
      GroupFormDialog,
      expect.objectContaining({
        data: expect.objectContaining({ mode: 'create', groups: [blueGroup, greenGroup] }),
      }),
    );
    await vi.waitFor(() => expect(groupLists()).toBeGreaterThan(before));
  });

  it('opens the group dialog 540 px wide', async () => {
    await setup();

    byTestId(element(), 'add-group').click();
    await settle();

    expect(dialog.open).toHaveBeenCalledWith(
      GroupFormDialog,
      expect.objectContaining({ width: '540px' }),
    );
  });

  it('counter and limit', async () => {
    const colors = ['blue', 'green', 'orange', 'purple', 'red', 'teal'];
    const twelve = Array.from({ length: 12 }, (_, index) => ({
      ...blueGroup,
      id: `00000000-0000-0000-0000-${String(index).padStart(12, '0')}`,
      name: `Group ${index}`,
      color: colors[index % colors.length],
    }));
    await setup({ groups: twelve });

    expect(byTestId(element(), 'add-group').hasAttribute('disabled')).toBe(true);
    expect(byTestId(element(), 'group-counter').textContent).toMatch(/^\D*12\D+12\D*$/);
  });

  it('menu edit and delete', async () => {
    await setup();

    await openMenuItem('Emerytura', 'edit-group');
    expect(dialog.open).toHaveBeenCalledWith(
      GroupFormDialog,
      expect.objectContaining({
        data: expect.objectContaining({ mode: 'edit', group: blueGroup }),
      }),
    );

    await openMenuItem('Emerytura', 'delete-group');
    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    const confirm = dialog.open.mock.calls.find((call) => call[0] === ConfirmDialog)!;
    expect((confirm[1].data as { message: string }).message).toContain('3');
    await vi.waitFor(() =>
      expect(
        fetchSpy.mock.calls.some(
          (call: unknown[]) =>
            (call[0] as Request).method === 'DELETE' &&
            requestUrl(call[0]).includes(`/api/portfolio/asset-groups/${blueGroupId}`),
        ),
      ).toBe(true),
    );
  });

  it('does not delete when the confirmation is declined', async () => {
    await setup();
    afterClosed = false;

    await openMenuItem('Emerytura', 'delete-group');

    expect(deleteCalls()).toBe(0);
  });

  it('delete dialog text and focus', async () => {
    await setup({ groups: [blueGroup, { ...greenGroup, assetCount: 0 }] });
    await TestBed.inject(LanguageService).setLanguage('pl');

    await openMenuItem('Emerytura', 'delete-group');
    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    await vi.waitFor(() =>
      expect(overlay.querySelector('[data-testid="delete-group"]')).toBeNull(),
    );
    await openMenuItem('Poduszka', 'delete-group');

    const confirms = dialog.open.mock.calls
      .filter((call) => call[0] === ConfirmDialog)
      .map((call) => call[1].data);
    expect(confirms).toEqual([
      {
        title: 'Usunąć grupę?',
        message:
          'Grupa "Emerytura" ma aktywa: 3. Jej historia trafi do „Bez grupy", a aktywa oraz domyślne grupy rachunków i miejsc wrócą do „Bez grupy".',
        confirmLabel: 'Usuń',
        destructive: true,
      },
      {
        title: 'Usunąć grupę?',
        message: 'Grupa "Poduszka" nie ma aktywów. Jej historia trafi do „Bez grupy".',
        confirmLabel: 'Usuń',
        destructive: true,
      },
    ]);
  });

  function panel(): HTMLElement {
    return byTestId(element(), 'assets-panel');
  }

  function panelOpen(): boolean {
    return panel().classList.contains('mat-drawer-opened');
  }

  it('delete closes the open panel', async () => {
    setViewport(1280);
    await setup();
    await selectGroup('Emerytura');
    await openEdit();
    expect(panelOpen()).toBe(true);
    expect(element().querySelector('[data-testid="save-assignment"]')).toBeTruthy();

    await openMenuItem('Emerytura', 'delete-group');

    await vi.waitFor(() => expect(deleteCalls()).toBe(1));
    await settle();
    expect(element().querySelector('[data-testid="save-assignment"]')).toBeNull();
    expect(panelOpen()).toBe(false);
  });

  it('assets button opens the panel in members mode', async () => {
    setViewport(1280);
    await setup({ tree: [treeAccountWithThreeChildren(blueGroupId), ...skarbonki(1)] });
    expect(panelOpen()).toBe(false);

    await selectGroup('Emerytura');

    expect(panelOpen()).toBe(true);
    expect(byTestId(panel(), 'assets-panel-title').textContent).toContain('Emerytura');
    expect(byTestId(panel(), 'members-view')).toBeTruthy();
    expect(treeRow('IKE Obligacje').querySelector('input[type=checkbox]')).toBeNull();
    expect(byTestId(panel(), 'asset-filter')).toBeTruthy();
    expect(panel().querySelector('[data-testid="save-assignment"]')).toBeNull();
    expect(byTestId(panel(), 'edit-assignment')).toBeTruthy();
  });

  it('members view sums account rows', async () => {
    setViewport(1280);
    await setup({
      tree: [
        treeAccount(
          'acc-x',
          'IKE eMakler',
          [
            treeChild(etfOneId, 'VWCE.DE', { assetClass: 3, groupId: blueGroupId }),
            treeChild(etfTwoId, 'CDR.WA', { assetClass: 3, groupId: blueGroupId }),
          ],
          { groupId: blueGroupId, cashAssetId: cashXId },
        ),
        treeAccount(
          'acc-y',
          'IKE Obligacje',
          [
            treeChild(treeChildOneId, 'OTS0126', {
              assetClass: 4,
              groupId: blueGroupId,
              isException: true,
            }),
          ],
          { cashAssetId: cashYId },
        ),
      ],
      dashboard: {
        ...groupsDashboard,
        byAsset: [
          { assetId: cashXId, valuePln: 1200, priceDate: '2026-10-24', isStale: false },
          { assetId: etfOneId, valuePln: 500, priceDate: '2026-10-24', isStale: false },
          { assetId: etfTwoId, valuePln: 300, priceDate: '2026-10-24', isStale: false },
          { assetId: treeChildOneId, valuePln: 250, priceDate: '2026-10-24', isStale: false },
          { assetId: cashYId, valuePln: 900, priceDate: '2026-10-24', isStale: false },
        ],
      },
    });

    await selectGroup('Emerytura');

    expect(treeRows().map((row) => byTestId(row, 'row-name').textContent?.trim())).toEqual([
      'IKE eMakler',
      'VWCE.DE',
      'CDR.WA',
      'IKE Obligacje',
      'OTS0126',
    ]);
    expect(byTestId(treeRow('IKE eMakler'), 'row-value').textContent).toContain(formatMoney(2000));
    expect(byTestId(treeRow('VWCE.DE'), 'row-value').textContent).toContain(formatMoney(500));
    expect(byTestId(treeRow('CDR.WA'), 'row-value').textContent).toContain(formatMoney(300));
    expect(byTestId(treeRow('IKE Obligacje'), 'row-value').textContent).toContain(formatMoney(250));
    expect(byTestId(treeRow('OTS0126'), 'row-value').textContent).toContain(formatMoney(250));
  });

  it('switches between members and edit', async () => {
    setViewport(1280);
    const tree = [treeAccountWithThreeChildren(blueGroupId), ...skarbonki(1)];
    await setup({
      tree,
      afterAssignment: { groups: [blueGroup, greenGroup], tree, dashboard: groupsDashboard },
    });
    await selectGroup('Emerytura');
    expect(byTestId(panel(), 'members-view')).toBeTruthy();
    expect(element().querySelector('[data-testid="save-assignment"]')).toBeNull();

    await openEdit();
    expect(byTestId(element(), 'save-assignment')).toBeTruthy();
    expect(rowCheckbox('Skarbonka 1')).toBeTruthy();
    await toggle('Skarbonka 1');
    byTestId(element(), 'save-assignment').click();
    await settle();

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(element().querySelector('[data-testid="save-assignment"]')).toBeNull();
      expect(byTestId(panel(), 'members-view')).toBeTruthy();
    });
    expect(panelOpen()).toBe(true);
  });

  it('save refreshes the cards', async () => {
    setViewport(1280);
    const tree = [treeAccountWithThreeChildren(blueGroupId), ...skarbonki(1)];
    await setup({
      tree,
      afterAssignment: {
        groups: [{ ...blueGroup, assetCount: 4 }, greenGroup],
        tree: [
          treeAccountWithThreeChildren(blueGroupId),
          ...skarbonki(1).map((item) => ({ ...item, groupId: blueGroupId })),
        ],
        dashboard: {
          ...groupsDashboard,
          byGroup: groupsDashboard.byGroup.map((entry) =>
            entry.groupId === blueGroupId ? { ...entry, valuePln: 4800 } : entry,
          ),
        },
      },
    });
    await selectGroup('Emerytura');
    await openEdit();

    await toggle('Skarbonka 1');
    byTestId(element(), 'save-assignment').click();
    await settle();

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(element().querySelector('[data-testid="save-assignment"]')).toBeNull();
      expect(byTestId(groupCard('Emerytura'), 'group-count').textContent).toContain('4');
      expect(byTestId(groupCard('Emerytura'), 'group-value').textContent).toContain(
        formatMoney(4800),
      );
    });
  });

  it('ungrouped panel is members only', async () => {
    setViewport(1280);
    await setup();

    byTestId(byTestId(element(), 'ungrouped-card'), 'ungrouped-assets').click();
    await settle();

    expect(panelOpen()).toBe(true);
    expect(byTestId(panel(), 'assets-panel-title').textContent).toContain('No group');
    expect(byTestId(panel(), 'members-view')).toBeTruthy();
    expect(treeRows().map((row) => byTestId(row, 'row-name').textContent?.trim())).toEqual([
      'Free Stock',
      'Old Bond',
    ]);
    expect(panel().querySelector('input[type=checkbox]')).toBeNull();
    expect(panel().querySelector('[data-testid="edit-assignment"]')).toBeNull();
    expect(panel().querySelector('[data-testid="save-assignment"]')).toBeNull();
    expect(byTestId(panel(), 'asset-filter')).toBeTruthy();
    expect(byTestId(panel(), 'ungrouped-hint')).toBeTruthy();
  });

  it('members view paginates', async () => {
    setViewport(1280);
    await setup({
      tree: skarbonki(21).map((item) => ({ ...item, groupId: blueGroupId })),
    });
    await selectGroup('Emerytura');

    expect(byTestId(panel(), 'members-view')).toBeTruthy();
    expect(treeRows()).toHaveLength(20);
    await nextPage();
    expect(treeRows()).toHaveLength(1);
    await previousPage();
    expect(treeRows()).toHaveLength(20);
  });

  it('panel closes and restores focus', async () => {
    setViewport(1280);
    await setup({
      tree: [
        treeAccountWithThreeChildren(),
        treeAsset('cushion-cash', 'Cushion Cash', { groupId: greenGroupId }),
        ...skarbonki(1),
      ],
    });
    const opener = byTestId(groupCard('Emerytura'), 'select-group');
    opener.focus();

    opener.click();
    await settle();
    await vi.waitFor(() => expect(panel().contains(document.activeElement)).toBe(true), {
      timeout: 5000,
    });

    await openEdit();
    await toggle('Skarbonka 1');
    byTestId(panel(), 'close-assets-panel').click();
    await settle();
    expect(panelOpen()).toBe(false);
    await vi.waitFor(() => expect(document.activeElement).toBe(opener), { timeout: 5000 });

    await selectGroup('Emerytura');
    await openEdit();
    expect(rowCheckbox('Skarbonka 1').checked).toBe(false);
    await vi.waitFor(() => expect(panel().contains(document.activeElement)).toBe(true), {
      timeout: 5000,
    });

    (document.activeElement as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true }),
    );
    await settle();
    expect(panelOpen()).toBe(false);
    await vi.waitFor(() => expect(document.activeElement).toBe(opener), { timeout: 5000 });

    await selectGroup('Emerytura');
    await openEdit();
    await toggle('Skarbonka 1');
    await selectGroup('Poduszka');
    await openEdit();
    expect(panelOpen()).toBe(true);
    expect(byTestId(panel(), 'assets-panel-title').textContent).toContain('Poduszka');
    expect(rowCheckbox('Skarbonka 1').checked).toBe(false);
    expect(rowCheckbox('Cushion Cash').checked).toBe(true);
  });

  it('GroupsPanel_ReopenAfterTransitionsEnabled_TakesFocus', async () => {
    const transitionTimers: (() => void)[] = [];
    const realSetTimeout = globalThis.setTimeout;
    const timers = vi.spyOn(globalThis, 'setTimeout').mockImplementation(((
      handler: () => void,
      delay?: number,
    ) => {
      if (delay !== 200) {
        return realSetTimeout(handler, delay);
      }
      transitionTimers.push(handler);
      return 0;
    }) as never);
    try {
      setViewport(1280);
      await setup();
      const opener = byTestId(groupCard('Emerytura'), 'select-group');

      opener.click();
      await settle();
      await vi.waitFor(() => expect(panel().contains(document.activeElement)).toBe(true), {
        timeout: 5000,
      });

      byTestId(panel(), 'close-assets-panel').click();
      await settle();
      expect(panelOpen()).toBe(false);
      transitionTimers.splice(0).forEach((run) => run());

      await selectGroup('Emerytura');
      await vi.waitFor(() => expect(panel().contains(document.activeElement)).toBe(true), {
        timeout: 5000,
      });
    } finally {
      timers.mockRestore();
    }
  });

  it('panel mode follows the breakpoint', async () => {
    setViewport(959);
    await setup();
    await selectGroup('Emerytura');
    expect(panelOpen()).toBe(true);
    expect(panel().classList.contains('mat-drawer-over')).toBe(true);
    expect(panel().classList.contains('mat-drawer-side')).toBe(false);
    TestBed.inject(OverlayContainer).getContainerElement().replaceChildren();
    fixture.destroy();
    fetchSpy.mockRestore();

    TestBed.resetTestingModule();
    setViewport(960);
    await setup();
    await selectGroup('Emerytura');
    expect(panel().classList.contains('mat-drawer-side')).toBe(true);
    expect(panel().classList.contains('mat-drawer-over')).toBe(false);
  });

  function treeRows(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('[data-testid="tree-row"]'));
  }

  function treeRow(name: string): HTMLElement {
    const row = treeRows().find((r) => (byTestId(r, 'row-name').textContent ?? '').trim() === name);
    if (!row) {
      throw new Error(`No tree row for '${name}'.`);
    }
    return row;
  }

  function rowCheckbox(name: string): HTMLInputElement {
    return treeRow(name).querySelector<HTMLInputElement>('input[type=checkbox]')!;
  }

  function paginator(): HTMLElement {
    return panel().querySelector<HTMLElement>('mat-paginator')!;
  }

  async function nextPage(): Promise<void> {
    paginator().querySelector<HTMLElement>('.mat-mdc-paginator-navigation-next')!.click();
    await settle();
  }

  async function previousPage(): Promise<void> {
    paginator().querySelector<HTMLElement>('.mat-mdc-paginator-navigation-previous')!.click();
    await settle();
  }

  async function toggleExpand(name: string): Promise<void> {
    byTestId(treeRow(name), 'expand-toggle').click();
    await settle();
  }

  async function typeFilter(value: string): Promise<void> {
    const filter = byTestId(element(), 'asset-filter') as HTMLInputElement;
    filter.value = value;
    filter.dispatchEvent(new Event('input'));
    await settle();
  }

  function memberWrites(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((r: Request) => r.method === 'PUT' && /\/asset-groups\/[^/]+\/members$/.test(r.url));
  }

  function treeLists(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        (call[0] as Request).method === 'GET' &&
        requestUrl(call[0]).includes('/api/portfolio/asset-groups/tree'),
    ).length;
  }

  it('edit tree paginates with expanded rows', async () => {
    await setup({ tree: [treeAccountWithThreeChildren(), ...skarbonki(20)] });
    await selectGroup('Emerytura');
    await openEdit();

    expect(treeRows()).toHaveLength(20);
    expect(paginator().querySelector('mat-select')).toBeNull();
    await nextPage();
    expect(treeRows()).toHaveLength(1);
    await previousPage();

    await toggleExpand('IKE Obligacje');

    expect(treeRows()).toHaveLength(20);
    expect(treeRow('OTS0126')).toBeTruthy();
    await nextPage();
    expect(treeRows()).toHaveLength(4);
    expect(paginator().textContent).toContain('24');
  });

  it('keeps expansion across page and filter', async () => {
    await setup({ tree: [treeAccountWithThreeChildren(), ...skarbonki(20)] });
    await selectGroup('Emerytura');
    await openEdit();
    await toggleExpand('IKE Obligacje');

    await nextPage();
    await previousPage();
    expect(treeRow('OTS0126')).toBeTruthy();

    await typeFilter('Skarbonka 3');
    await typeFilter('');
    expect(treeRow('OTS0126')).toBeTruthy();
  });

  it('greyed row with chip can be checked', async () => {
    await setup({
      tree: [treeAccountWithThreeChildren(greenGroupId), ...skarbonki(1)],
    });
    await selectGroup('Emerytura');
    await openEdit();

    const account = treeRow('IKE Obligacje');
    expect(account.dataset['greyed']).toBe('true');
    expect(byTestId(account, 'asset-group-chip').textContent).toContain('Poduszka');
    expect(rowCheckbox('IKE Obligacje').checked).toBe(false);

    await toggleExpand('IKE Obligacje');
    const child = treeRow('OTS0126');
    expect(child.dataset['greyed']).toBe('true');
    expect(byTestId(child, 'asset-group-chip').textContent).toContain('Poduszka');
    expect(rowCheckbox('OTS0126').checked).toBe(false);
    expect(rowCheckbox('OTS0126').disabled).toBe(false);

    await settle();
    rowCheckbox('OTS0126').click();
    await settle();
    expect(rowCheckbox('OTS0126').checked).toBe(true);
  });

  it('saves desired state through members', async () => {
    await setup({ tree: [treeAccountWithThreeChildren(blueGroupId), ...skarbonki(1)] });
    await selectGroup('Emerytura');
    await openEdit();
    await toggleExpand('IKE Obligacje');
    expect(rowCheckbox('IKE Obligacje').checked).toBe(true);
    expect(rowCheckbox('ROR0126').checked).toBe(true);
    const treeListsBefore = treeLists();
    const groupListsBefore = groupLists();

    rowCheckbox('ROR0126').click();
    await settle();
    byTestId(element(), 'save-assignment').click();
    await settle();

    await vi.waitFor(() => expect(memberWrites()).toHaveLength(1));
    const [write] = memberWrites();
    expect(write.url).toContain(`/asset-groups/${blueGroupId}/members`);
    const body = await write.clone().json();
    expect(body.accountIds).toContain(treeAccountId);
    expect(body.assetIds).not.toContain(treeChildTwoId);
    expect(body.assetIds).toContain(treeChildOneId);
    expect(body.assetIds).toContain(treeChildThreeId);
    await vi.waitFor(() => {
      expect(treeLists()).toBeGreaterThan(treeListsBefore);
      expect(groupLists()).toBeGreaterThan(groupListsBefore);
    });
  });

  it('checking an account leaves an exception child alone', async () => {
    const account = treeAccount(treeAccountId, 'IKE Obligacje', [
      treeChild(treeChildOneId, 'OTS0126', { assetClass: 4 }),
      treeChild(treeChildTwoId, 'ROR0126', {
        assetClass: 4,
        groupId: greenGroupId,
        isException: true,
      }),
    ]);
    await setup({ tree: [account] });
    await selectGroup('Emerytura');
    await openEdit();
    await toggleExpand('IKE Obligacje');

    rowCheckbox('IKE Obligacje').click();
    await settle();
    expect(rowCheckbox('OTS0126').checked).toBe(true);
    expect(rowCheckbox('ROR0126').checked).toBe(false);

    byTestId(element(), 'save-assignment').click();
    await settle();

    await vi.waitFor(() => expect(memberWrites()).toHaveLength(1));
    const body = await memberWrites()[0].clone().json();
    expect(body.accountIds).toContain(treeAccountId);
    expect(body.assetIds).toContain(treeChildOneId);
    expect(body.assetIds).not.toContain(treeChildTwoId);
  });
});
