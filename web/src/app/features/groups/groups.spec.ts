import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AssetGroupResponse, AssignableAssetResponse } from '../../api/portfolio';
import type { DashboardResponse } from '../../api/reporting';
import { client as reportingClient } from '../../api/reporting/client.gen';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../shared/format';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { GroupAssetsPanel } from './group-assets-panel/group-assets-panel';
import { Groups } from './groups';
import { GroupFormDialog } from './group-form-dialog/group-form-dialog';
import {
  assignableAssets,
  blueGroup,
  blueGroupId,
  freeAssetId,
  greenGroup,
  groupsDashboard,
  memberAssetId,
  otherGroupAssetId,
} from './testing/group-fixtures';

interface SetupOptions {
  groups?: AssetGroupResponse[];
  assets?: AssignableAssetResponse[];
  dashboard?: DashboardResponse | 'fail';
  groupList?: 'ok' | 'fail' | 'pending';
  afterAssignment?: {
    groups: AssetGroupResponse[];
    assets: AssignableAssetResponse[];
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
    let assets = options.assets ?? assignableAssets;
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
      if (url.includes('/api/portfolio/asset-groups/assets')) {
        return jsonResponse(assets);
      }
      if (url.includes('/api/portfolio/asset-groups/assignment')) {
        if (options.afterAssignment) {
          groups = options.afterAssignment.groups;
          assets = options.afterAssignment.assets;
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

  function assetRow(name: string): HTMLElement {
    const row = Array.from(
      element().querySelectorAll<HTMLElement>('[data-testid="asset-row"]'),
    ).find((r) => (r.textContent ?? '').includes(name));
    if (!row) {
      throw new Error(`No asset row for '${name}'.`);
    }
    return row;
  }

  function checkbox(name: string): HTMLInputElement {
    return assetRow(name).querySelector<HTMLInputElement>('input[type=checkbox]')!;
  }

  function visibleAssetNames(): string[] {
    return Array.from(element().querySelectorAll<HTMLElement>('[data-testid="asset-row"]')).map(
      (r) => (r.textContent ?? '').trim(),
    );
  }

  async function selectGroup(name: string): Promise<void> {
    byTestId(groupCard(name), 'select-group').click();
    await settle();
  }

  async function toggle(name: string): Promise<void> {
    checkbox(name).click();
    await settle();
  }

  function groupLists(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        (call[0] as Request).method === 'GET' &&
        /\/api\/portfolio\/asset-groups$/.test(requestUrl(call[0])),
    ).length;
  }

  function assetLists(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        (call[0] as Request).method === 'GET' &&
        requestUrl(call[0]).includes('/api/portfolio/asset-groups/assets'),
    ).length;
  }

  function assignmentWrites(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((r: Request) => r.method === 'PUT' && r.url.includes('/asset-groups/assignment'));
  }

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

  it('planning link', async () => {
    await setup();

    const link = byTestId(element(), 'summary-strip').querySelector<HTMLAnchorElement>('a');
    expect(link?.getAttribute('href')).toBe('/planning');
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

  function panelComponent(): GroupAssetsPanel {
    return fixture.debugElement.query(By.directive(GroupAssetsPanel)).componentInstance;
  }

  function panelOpen(): boolean {
    return panel().classList.contains('mat-drawer-opened');
  }

  it('delete closes the open panel', async () => {
    setViewport(1280);
    await setup();
    await selectGroup('Emerytura');
    expect(panelOpen()).toBe(true);
    expect(element().querySelector('[data-testid="save-assignment"]')).toBeTruthy();

    await openMenuItem('Emerytura', 'delete-group');

    await vi.waitFor(() => expect(deleteCalls()).toBe(1));
    await settle();
    expect(element().querySelector('[data-testid="save-assignment"]')).toBeNull();
    expect(panelOpen()).toBe(false);
  });

  it('assets button opens the panel', async () => {
    setViewport(1280);
    await setup();
    expect(panelOpen()).toBe(false);

    await selectGroup('Emerytura');

    expect(panelOpen()).toBe(true);
    expect(byTestId(panel(), 'assets-panel-title').textContent).toContain('Emerytura');
    expect(checkbox('Member Bond').checked).toBe(true);
    expect(byTestId(panel(), 'asset-filter')).toBeTruthy();
    expect(byTestId(panel(), 'select-all-shown')).toBeTruthy();
    expect(panel().querySelector('mat-select')).toBeTruthy();
    const list = element().querySelector('.groups-page__list')!;
    expect(list.querySelector('[data-testid="asset-row"]')).toBeNull();
    expect(element().querySelectorAll('[data-testid="save-assignment"]')).toHaveLength(1);
    expect(panel().contains(byTestId(element(), 'save-assignment'))).toBe(true);
  });

  it('save refreshes the cards', async () => {
    setViewport(1280);
    await setup({
      afterAssignment: {
        groups: [{ ...blueGroup, assetCount: 4 }, greenGroup],
        assets: assignableAssets.map((asset) =>
          asset.assetId === freeAssetId ? { ...asset, groupId: blueGroupId } : asset,
        ),
        dashboard: {
          ...groupsDashboard,
          byGroup: groupsDashboard.byGroup.map((entry) =>
            entry.groupId === blueGroupId ? { ...entry, valuePln: 4800 } : entry,
          ),
        },
      },
    });
    await selectGroup('Emerytura');

    await toggle('Free Stock');
    byTestId(element(), 'save-assignment').click();
    await settle();

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(panelOpen()).toBe(false);
      expect(byTestId(groupCard('Emerytura'), 'group-count').textContent).toContain('4');
      expect(byTestId(groupCard('Emerytura'), 'group-value').textContent).toContain(
        formatMoney(4800),
      );
    });
  });

  it('ungrouped is read only', async () => {
    setViewport(1280);
    await setup();

    byTestId(byTestId(element(), 'ungrouped-card'), 'ungrouped-assets').click();
    await settle();

    expect(panelOpen()).toBe(true);
    expect(byTestId(panel(), 'assets-panel-title').textContent).toContain('No group');
    expect(visibleAssetNames()).toHaveLength(2);
    expect(visibleAssetNames().some((name) => name.includes('Free Stock'))).toBe(true);
    expect(visibleAssetNames().some((name) => name.includes('Old Bond'))).toBe(true);
    expect(visibleAssetNames().some((name) => name.includes('Member Bond'))).toBe(false);
    expect(panel().querySelector('input[type=checkbox]')).toBeNull();
    expect(panel().querySelector('[data-testid="save-assignment"]')).toBeNull();
    expect(byTestId(panel(), 'asset-filter')).toBeTruthy();
    expect(byTestId(panel(), 'ungrouped-hint')).toBeTruthy();
  });

  it('panel closes and restores focus', async () => {
    setViewport(1280);
    await setup();
    const opener = byTestId(groupCard('Emerytura'), 'select-group');
    opener.focus();

    opener.click();
    await settle();
    await vi.waitFor(() => expect(panel().contains(document.activeElement)).toBe(true));

    await toggle('Free Stock');
    byTestId(panel(), 'close-assets-panel').click();
    await settle();
    expect(panelOpen()).toBe(false);
    await vi.waitFor(() => expect(document.activeElement).toBe(opener));

    await selectGroup('Emerytura');
    expect(checkbox('Free Stock').checked).toBe(false);
    await vi.waitFor(() => expect(panel().contains(document.activeElement)).toBe(true));

    (document.activeElement as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true }),
    );
    await settle();
    expect(panelOpen()).toBe(false);
    await vi.waitFor(() => expect(document.activeElement).toBe(opener));

    await selectGroup('Emerytura');
    await toggle('Free Stock');
    await selectGroup('Poduszka');
    expect(panelOpen()).toBe(true);
    expect(byTestId(panel(), 'assets-panel-title').textContent).toContain('Poduszka');
    expect(checkbox('Free Stock').checked).toBe(false);
    expect(checkbox('Cushion Cash').checked).toBe(true);
  });

  it('panel mode follows the breakpoint', async () => {
    setViewport(800);
    await setup();
    await selectGroup('Emerytura');
    expect(panelOpen()).toBe(true);
    expect(panel().classList.contains('mat-drawer-over')).toBe(true);
    expect(panel().classList.contains('mat-drawer-side')).toBe(false);
    TestBed.inject(OverlayContainer).getContainerElement().replaceChildren();
    fixture.destroy();
    fetchSpy.mockRestore();

    TestBed.resetTestingModule();
    setViewport(1280);
    await setup();
    await selectGroup('Emerytura');
    expect(panel().classList.contains('mat-drawer-side')).toBe(true);
    expect(panel().classList.contains('mat-drawer-over')).toBe(false);
  });

  it('lists assignable assets', async () => {
    await setup();

    await selectGroup('Emerytura');

    expect(element().querySelectorAll('[data-testid="asset-row"]')).toHaveLength(4);
    expect(checkbox('Member Bond').checked).toBe(true);
    expect(checkbox('Free Stock').checked).toBe(false);
    expect(checkbox('Cushion Cash').checked).toBe(false);
    expect(assetRow('Member Bond').textContent).toContain('Obligacje');
    expect(assetRow('Cushion Cash').textContent).toContain('Safe');
    expect(byTestId(assetRow('Cushion Cash'), 'asset-group-chip').textContent).toContain(
      'Poduszka',
    );
    expect(assetRow('Member Bond').querySelector('[data-testid="asset-group-chip"]')).toBeNull();
    expect(assetRow('Free Stock').querySelector('[data-testid="asset-group-chip"]')).toBeNull();
    expect(byTestId(assetRow('Old Bond'), 'asset-archived')).toBeTruthy();
    expect(assetRow('Free Stock').querySelector('[data-testid="asset-archived"]')).toBeNull();
  });

  it('filters and selects all', async () => {
    await setup();
    await selectGroup('Poduszka');

    const filter = byTestId(element(), 'asset-filter') as HTMLInputElement;
    filter.value = 'bond';
    filter.dispatchEvent(new Event('input'));
    await settle();
    expect(visibleAssetNames().some((n) => n.includes('Free Stock'))).toBe(false);
    expect(visibleAssetNames()).toHaveLength(2);

    filter.value = '';
    filter.dispatchEvent(new Event('input'));
    await settle();
    expect(visibleAssetNames()).toHaveLength(4);

    panelComponent()['classFilter'].set(4);
    await settle();
    expect(visibleAssetNames()).toHaveLength(2);

    byTestId(element(), 'select-all-shown').click();
    await settle();
    expect(checkbox('Member Bond').checked).toBe(true);
    expect(checkbox('Old Bond').checked).toBe(true);

    panelComponent()['classFilter'].set(null);
    await settle();
    expect(checkbox('Free Stock').checked).toBe(false);
    expect(checkbox('Cushion Cash').checked).toBe(true);
  });

  it('saves the assignment diff', async () => {
    await setup();
    await selectGroup('Emerytura');
    const assetListsBefore = assetLists();

    await toggle('Member Bond');
    await toggle('Free Stock');
    await toggle('Cushion Cash');
    byTestId(element(), 'save-assignment').click();
    await settle();

    await vi.waitFor(() => expect(assignmentWrites()).toHaveLength(2));
    const bodies = await Promise.all(assignmentWrites().map((r) => r.clone().json()));
    expect(bodies).toContainEqual({
      assetIds: expect.arrayContaining([freeAssetId, otherGroupAssetId]),
      groupId: blueGroupId,
    });
    expect(bodies).toContainEqual({ assetIds: [memberAssetId], groupId: null });
    const added = bodies.find((b) => b.groupId === blueGroupId);
    expect(added.assetIds).toHaveLength(2);
    await vi.waitFor(() => expect(assetLists()).toBeGreaterThan(assetListsBefore));
  });

  it('skips an empty side of the assignment diff', async () => {
    await setup();
    await selectGroup('Emerytura');

    await toggle('Free Stock');
    byTestId(element(), 'save-assignment').click();
    await settle();

    await vi.waitFor(() => expect(assignmentWrites()).toHaveLength(1));
    expect(await assignmentWrites()[0].clone().json()).toEqual({
      assetIds: [freeAssetId],
      groupId: blueGroupId,
    });
  });

  it('sends nothing when the selection did not change', async () => {
    await setup();
    await selectGroup('Emerytura');

    byTestId(element(), 'save-assignment').click();
    await settle();

    expect(assignmentWrites()).toHaveLength(0);
  });
});
