import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AssetGroupResponse, AssignableAssetResponse } from '../../api/portfolio';
import type { DashboardResponse } from '../../api/reporting';
import { client as reportingClient } from '../../api/reporting/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../shared/format';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
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

  afterEach(() => {
    release?.();
    release = undefined;
    fetchSpy.mockRestore();
    TestBed.inject(OverlayContainer).getContainerElement().replaceChildren();
  });

  async function setup(options: SetupOptions = {}): Promise<void> {
    const groups = options.groups ?? [blueGroup, greenGroup];
    const assets = options.assets ?? assignableAssets;
    const dashboard = options.dashboard ?? groupsDashboard;
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

  it('delete closes the open block', async () => {
    await setup();
    await selectGroup('Emerytura');
    expect(element().querySelector('[data-testid="save-assignment"]')).toBeTruthy();

    await openMenuItem('Emerytura', 'delete-group');

    await vi.waitFor(() => expect(deleteCalls()).toBe(1));
    await settle();
    expect(element().querySelector('[data-testid="save-assignment"]')).toBeNull();
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

    fixture.componentInstance['classFilter'].set(4);
    await settle();
    expect(visibleAssetNames()).toHaveLength(2);

    byTestId(element(), 'select-all-shown').click();
    await settle();
    expect(checkbox('Member Bond').checked).toBe(true);
    expect(checkbox('Old Bond').checked).toBe(true);

    fixture.componentInstance['classFilter'].set(null);
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
