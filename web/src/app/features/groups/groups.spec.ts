import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AssetGroupResponse, AssignableAssetResponse } from '../../api/portfolio';
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
  memberAssetId,
  otherGroupAssetId,
} from './testing/group-fixtures';

describe('Groups', () => {
  let fixture: ComponentFixture<Groups>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let afterClosed: unknown;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(
    groups: AssetGroupResponse[] = [blueGroup, greenGroup],
    assets: AssignableAssetResponse[] = assignableAssets,
  ): Promise<void> {
    afterClosed = true;
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      const url = requestUrl(input);
      if (url.includes('/api/portfolio/asset-groups/assets')) {
        return jsonResponse(assets);
      }
      if (url.includes('/api/portfolio/asset-groups/assignment')) {
        return new Response(null, { status: 204 });
      }
      if (url.includes('/api/portfolio/asset-groups')) {
        return request.method === 'GET'
          ? jsonResponse(groups)
          : new Response(null, { status: 204 });
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

  function groupRow(name: string): HTMLElement {
    const row = Array.from(
      element().querySelectorAll<HTMLElement>('[data-testid="group-row"]'),
    ).find((r) => (byTestId(r, 'group-name').textContent ?? '').trim() === name);
    if (!row) {
      throw new Error(`No row for '${name}'.`);
    }
    return row;
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
    byTestId(groupRow(name), 'select-group').click();
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

    expect(element().querySelectorAll('[data-testid="group-row"]')).toHaveLength(2);
    const blue = groupRow('Emerytura');
    expect(byTestId(blue, 'group-swatch')).toBeTruthy();
    expect(byTestId(blue, 'group-count').textContent).toContain('3');
    expect(blue.querySelector('[data-testid="group-target"]')).toBeNull();
    const green = groupRow('Poduszka');
    expect(byTestId(green, 'group-count').textContent).toContain('1');
    expect(byTestId(green, 'group-target').textContent).toContain(formatMoney(30000));
    expect(element().querySelector('[data-testid="group-empty"]')).toBeNull();
    expect(byTestId(element(), 'add-group').hasAttribute('disabled')).toBe(false);
  });

  it('shows empty state', async () => {
    await setup([]);

    expect(element().querySelectorAll('[data-testid="group-row"]')).toHaveLength(0);
    expect(byTestId(element(), 'group-empty')).toBeTruthy();
    expect(byTestId(element(), 'add-group').hasAttribute('disabled')).toBe(false);
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

  it('disables add at the limit', async () => {
    const colors = ['blue', 'green', 'orange', 'purple', 'red', 'teal'];
    const twelve = Array.from({ length: 12 }, (_, index) => ({
      ...blueGroup,
      id: `00000000-0000-0000-0000-${String(index).padStart(12, '0')}`,
      name: `Group ${index}`,
      color: colors[index % colors.length],
    }));
    await setup(twelve);

    expect(byTestId(element(), 'add-group').hasAttribute('disabled')).toBe(true);
    expect(byTestId(element(), 'group-limit-hint')).toBeTruthy();
  });

  it('hides the limit hint below the limit', async () => {
    await setup();

    expect(element().querySelector('[data-testid="group-limit-hint"]')).toBeNull();
  });

  it('confirms deletion with the count', async () => {
    await setup();

    byTestId(groupRow('Emerytura'), 'delete-group').click();
    await settle();

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    const data = dialog.open.mock.calls[0][1].data as { message: string };
    expect(data.message).toContain('3');
    expect(data.message).toMatch(/history/i);
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

    byTestId(groupRow('Emerytura'), 'delete-group').click();
    await settle();

    expect(
      fetchSpy.mock.calls.some((call: unknown[]) => (call[0] as Request).method === 'DELETE'),
    ).toBe(false);
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
