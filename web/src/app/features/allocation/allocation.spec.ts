import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import type { HarnessLoader } from '@angular/cdk/testing';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { TRANSLATIONS, restoreEnglish, switchLanguage, textOf } from '../../../testing/i18n';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { client as reportingClient } from '../../api/reporting/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../../shared/format';
import { ASSET_CLASS } from '../assets/asset-class';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { jsonResponse } from '../dashboard/testing/dashboard-fixtures';
import { Allocation } from './allocation';
import {
  configuredAllocation,
  emeryturaGroup,
  emeryturaGroupId,
  emptyGroupAllocation,
  groupComparisonAllocation,
  groupConfiguredAllocation,
  inwestycjeGroup,
  inwestycjeGroupId,
  mockTargetAllocationApi,
  notConfiguredAllocation,
  savedBody,
  type TargetAllocationApi,
  unknownGroupId,
  zeroBaseAllocation,
} from './testing/target-allocation-fixtures';

const pl = (key: string) => TRANSLATIONS.pl[key] as string;

function plain(text: string): string {
  return text.replace(/\s+/g, ' ').replaceAll(String.fromCharCode(0x2212), '-');
}

describe('Allocation', () => {
  let fixture: ComponentFixture<Allocation>;
  let loader: HarnessLoader;
  let api: TargetAllocationApi;

  beforeAll(() => {
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    api?.fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function render(
    options: Parameters<typeof mockTargetAllocationApi>[0],
    language: 'en' | 'pl' = 'pl',
  ): Promise<void> {
    api = mockTargetAllocationApi(options);
    await TestBed.configureTestingModule({
      imports: [Allocation],
      providers: [provideRouter([]), provideI18nTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(Allocation);
    await fixture.whenStable();
    if (language === 'pl') {
      await switchLanguage(fixture, 'pl');
    }
    loader = TestbedHarnessEnvironment.loader(fixture);
  }

  const root = () => fixture.nativeElement as HTMLElement;
  const all = (testId: string) =>
    Array.from(root().querySelectorAll<HTMLElement>(`[data-testid="${testId}"]`));
  const one = (testId: string) => root().querySelector<HTMLElement>(`[data-testid="${testId}"]`);
  const saveButton = () => one('save') as HTMLButtonElement;

  const inputs = (testId: string) =>
    loader.getAllHarnesses(MatInputHarness.with({ selector: `[data-testid="${testId}"]` }));
  const selects = () =>
    loader.getAllHarnesses(MatSelectHarness.with({ selector: '[data-testid="group-classes"]' }));

  async function setTargets(values: string[]): Promise<void> {
    const targets = await inputs('group-target');
    for (let i = 0; i < values.length; i++) {
      await targets[i].setValue(values[i]);
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('renders deviations of a saved target', async () => {
    await render({ get: () => jsonResponse(configuredAllocation) });

    expect(one('allocation-editor')).toBeNull();
    const rows = all('group-row').map((row) => plain(textOf(row)));
    expect(rows).toHaveLength(3);

    expect(rows[0]).toContain('Akcje/ETF');
    expect(rows[0]).toContain('61,1');
    expect(rows[0]).toContain('45–55');
    expect(rows[0]).toMatch(/\+11,1 pp/);
    expect(rows[0]).toMatch(/\+ ?20 000/);
    expect(all('out-of-band')).toHaveLength(2);
    expect(all('group-row')[0].querySelector('[data-testid="out-of-band"]')).not.toBeNull();

    expect(rows[1]).toContain('33,3');
    expect(rows[1]).toMatch(/-12 000/);
    expect(all('group-row')[1].querySelector('[data-testid="out-of-band"]')).not.toBeNull();

    expect(rows[2]).toContain('5,6');
    expect(rows[2]).toMatch(/-8 ?000/);
    expect(all('group-row')[2].querySelector('[data-testid="out-of-band"]')).toBeNull();

    const outside = plain(textOf(one('outside-row')));
    expect(outside).toContain('Poza alokacją');
    expect(outside).toContain(plain(formatMoney(20000)));
    expect(outside).not.toContain('%');

    const summary = plain(textOf(one('summary')));
    expect(summary).toContain(plain(formatMoney(180000)));
    expect(summary).toContain(plain(formatMoney(200000)));
    expect(plain(textOf(one('edit')))).toContain('Edytuj');
  });

  it('opens the editor with the proposal', async () => {
    await render({ get: () => jsonResponse(notConfiguredAllocation) });

    expect(one('allocation-editor')).not.toBeNull();
    expect(one('allocation-view')).toBeNull();
    expect(one('cancel')).toBeNull();

    const rows = all('editor-row').map((row) => plain(textOf(row)));
    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain(plain(formatMoney(110000)));
    expect(rows[0]).toContain('61,1');
    expect(rows[1]).toContain(plain(formatMoney(60000)));
    expect(rows[1]).toContain('33,3');
    expect(rows[2]).toContain(plain(formatMoney(10000)));
    expect(rows[2]).toContain('5,6');

    const names = (await inputs('group-name')).map((input) => input.getValue());
    expect(await Promise.all(names)).toEqual(['Akcje/ETF', 'Obligacje', 'Kruszce']);
    const targets = await Promise.all((await inputs('group-target')).map((i) => i.getValue()));
    expect(targets).toEqual(['', '', '']);
    const bands = await Promise.all((await inputs('group-band')).map((i) => i.getValue()));
    expect(bands).toEqual(['5', '5', '5']);

    const picked = await Promise.all((await selects()).map((select) => select.getValueText()));
    expect(picked[0]).toContain(pl('enums.assetClass.stock'));
    expect(picked[0]).toContain(pl('enums.assetClass.etf'));
    expect(picked[1]).toContain(pl('enums.assetClass.bond'));
    expect(picked[1]).toContain(pl('enums.assetClass.deposit'));
    expect(picked[2]).toContain(pl('enums.assetClass.preciousMetal'));
    for (const text of picked) {
      expect(text).not.toContain(pl('enums.assetClass.cash'));
      expect(text).not.toContain(pl('enums.assetClass.bankAccount'));
    }
  });

  it('blocks invalid targets', async () => {
    await render({ get: () => jsonResponse(notConfiguredAllocation) });

    await setTargets(['50', '40', '10']);
    expect(saveButton().disabled).toBe(false);

    await setTargets(['50', '40', '9.9']);
    expect(plain(textOf(one('sum')))).toContain('99,9');
    expect(saveButton().disabled).toBe(true);

    await setTargets(['50', '40', '10']);
    expect(saveButton().disabled).toBe(false);

    await setTargets(['50', '37.7', '12.25']);
    expect(saveButton().disabled).toBe(true);
    await setTargets(['50', '40', '10']);
    expect(saveButton().disabled).toBe(false);

    const names = await inputs('group-name');
    await names[2].setValue(' akcje/etf');
    fixture.detectChanges();
    expect(saveButton().disabled).toBe(true);
    await names[2].setValue('Kruszce');
    expect(saveButton().disabled).toBe(false);

    const picks = await selects();
    await picks[2].open();
    const taken = await picks[2].getOptions({ text: pl('enums.assetClass.stock') });
    expect(await taken[0].isDisabled()).toBe(true);
    const own = await picks[2].getOptions({ text: pl('enums.assetClass.preciousMetal') });
    expect(await own[0].isDisabled()).toBe(false);
    await own[0].click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(saveButton().disabled).toBe(true);
  });

  it('saves and handles a rejected save', async () => {
    await render({
      get: (call) => jsonResponse(call === 1 ? notConfiguredAllocation : configuredAllocation),
      put: (attempt) =>
        attempt === 1
          ? jsonResponse(
              { title: 'Validation failed', detail: 'Targets must add up to 100.', status: 400 },
              400,
            )
          : jsonResponse(configuredAllocation),
    });

    await setTargets(['50', '40', '10']);
    saveButton().click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.puts).toHaveLength(1);
    expect(root().textContent).toContain('Targets must add up to 100.');
    expect(one('allocation-editor')).not.toBeNull();
    const kept = await Promise.all((await inputs('group-target')).map((i) => i.getValue()));
    expect(kept).toEqual(['50', '40', '10']);

    saveButton().click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.puts).toHaveLength(2);
    expect(savedBody(api.puts[1])).toEqual([
      { name: 'Akcje/ETF', assetClasses: [2, 3], targetPercent: 50, bandPp: 5 },
      { name: 'Obligacje', assetClasses: [1, 4], targetPercent: 40, bandPp: 5 },
      { name: 'Kruszce', assetClasses: [6], targetPercent: 10, bandPp: 5 },
    ]);
    expect(api.gets()).toBeGreaterThanOrEqual(2);
    expect(one('allocation-editor')).toBeNull();
    expect(one('allocation-view')).not.toBeNull();
    expect(all('group-row')).toHaveLength(3);
  });

  function expectNoGroupAmounts(): void {
    expect(one('allocation-view')).not.toBeNull();
    const headers = Array.from(root().querySelectorAll('th')).map((th) => plain(textOf(th)));
    expect(headers).toContain(pl('allocation.columns.group'));
    expect(headers).toContain(pl('allocation.columns.target'));
    expect(headers).toContain(pl('allocation.columns.band'));
    for (const key of ['value', 'current', 'deviationPp', 'deviationPln']) {
      expect(headers).not.toContain(pl(`allocation.columns.${key}`));
    }
    const rows = all('group-row').map((row) => plain(textOf(row)));
    expect(rows).toHaveLength(3);
    for (const row of rows) {
      expect(row).not.toContain('zł');
      expect(row).not.toMatch(/\bpp\b/);
    }
    expect(all('out-of-band')).toHaveLength(0);
    expect(one('summary')).toBeNull();
  }

  function expectNoAmounts(): void {
    expectNoGroupAmounts();
    expect(one('outside-row')).toBeNull();
  }

  it('shows the outside row when the base is zero', async () => {
    await render({
      get: () =>
        jsonResponse(
          zeroBaseAllocation({
            totalPln: 5000,
            outsidePln: 5000,
            outsideAssetClasses: [ASSET_CLASS.BankAccount],
          }),
        ),
    });

    expect(plain(textOf(root()))).toContain('brak aktywów w alokacji');
    expectNoGroupAmounts();
    const outside = plain(textOf(one('outside-row')));
    expect(outside).toContain('Poza alokacją');
    expect(outside).toContain('Konta bankowe');
    expect(outside).toContain(plain(formatMoney(5000)));
    expect(outside).not.toContain('%');
  });

  it('shows valuation states', async () => {
    await render({
      get: () =>
        jsonResponse(
          zeroBaseAllocation({
            asOf: null,
            totalPln: 0,
            outsidePln: 5000,
            outsideAssetClasses: [ASSET_CLASS.BankAccount],
          }),
        ),
    });
    expect(plain(textOf(root()))).toContain('brak wyceny');
    expectNoAmounts();
    api.fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await render({ get: () => jsonResponse(zeroBaseAllocation()) });
    expect(plain(textOf(root()))).toContain('brak aktywów w alokacji');
    expect(plain(textOf(root()))).not.toContain('brak wyceny');
    expectNoAmounts();
    api.fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await render({ get: () => jsonResponse({ ...configuredAllocation, isStale: true }) });
    const chip = root().querySelector('mat-chip');
    expect(chip).not.toBeNull();
    expect(plain(textOf(chip))).toBe(plain(pl('dashboard.stale')));
  });
});

describe('Allocation scope', () => {
  let harness: RouterTestingHarness;
  let api: TargetAllocationApi;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let confirmed: boolean;

  beforeAll(() => {
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    api?.fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function open(
    url: string,
    options: Parameters<typeof mockTargetAllocationApi>[0],
  ): Promise<void> {
    confirmed = true;
    api = mockTargetAllocationApi(options);
    dialog = { open: vi.fn(() => ({ afterClosed: () => of(confirmed) })) };
    await TestBed.configureTestingModule({
      providers: [
        provideI18nTesting(),
        provideRouter([{ path: 'allocation', component: Allocation }], withComponentInputBinding()),
        { provide: MatDialog, useValue: dialog },
      ],
    }).compileComponents();
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, Allocation);
    await settle();
    await switchLanguage(harness.fixture, 'pl');
    await settle();
  }

  async function settle(): Promise<void> {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  const root = () => harness.fixture.nativeElement as HTMLElement;
  const all = (testId: string) =>
    Array.from(root().querySelectorAll<HTMLElement>(`[data-testid="${testId}"]`));
  const one = (testId: string) => root().querySelector<HTMLElement>(`[data-testid="${testId}"]`);
  const chips = () => all('scope-chip');
  const chip = (name: string) => {
    const found = chips().find((candidate) => plain(textOf(candidate)).includes(name));
    if (!found) {
      throw new Error(`No scope chip '${name}'.`);
    }
    return found;
  };
  const checked = () =>
    chips()
      .filter((candidate) => candidate.getAttribute('aria-checked') === 'true')
      .map((candidate) => plain(textOf(candidate)));
  const loader = () => TestbedHarnessEnvironment.loader(harness.fixture);
  const inputs = (testId: string) =>
    loader().getAllHarnesses(MatInputHarness.with({ selector: `[data-testid="${testId}"]` }));

  async function press(element: HTMLElement | null): Promise<void> {
    if (!element) {
      throw new Error('Nothing to press.');
    }
    element.click();
    await settle();
  }

  const bothGroups = [emeryturaGroup, inwestycjeGroup];

  it('shows the scope switcher', async () => {
    await open('/allocation', {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
    });

    expect(one('scope-switcher')?.getAttribute('role')).toBe('radiogroup');
    const names = chips().map((candidate) => plain(textOf(candidate)));
    expect(names).toHaveLength(3);
    expect(names[0]).toContain('Całość');
    expect(names[1]).toContain('Emerytura');
    expect(names[2]).toContain('Inwestycje');
    expect(chips().every((candidate) => candidate.getAttribute('role') === 'radio')).toBe(true);
    expect(checked()).toEqual([names[0]]);
    expect(chip('Emerytura').querySelector('[data-testid="scope-swatch"]')).not.toBeNull();
    expect(all('group-row')).toHaveLength(3);
    expect(api.groupGets).toHaveLength(0);
  });

  it('hides the switcher without groups', async () => {
    await open('/allocation', { get: () => jsonResponse(configuredAllocation), groups: [] });

    expect(one('scope-switcher')).toBeNull();
    expect(all('scope-chip')).toHaveLength(0);
    expect(all('group-row')).toHaveLength(3);
  });

  it('keeps the scope in the url', async () => {
    await open('/allocation', {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse(groupConfiguredAllocation),
    });
    const router = TestBed.inject(Router);

    await press(chip('Emerytura'));
    expect(router.url).toContain(`group=${emeryturaGroupId}`);
    expect(api.groupGets).toContain(emeryturaGroupId);
    expect(checked()).toEqual([expect.stringContaining('Emerytura')]);

    await press(chip('Całość'));
    expect(router.url).not.toContain('group=');
    expect(checked()).toEqual([expect.stringContaining('Całość')]);

    api.groupGets.length = 0;
    await harness.navigateByUrl(`/allocation?group=${inwestycjeGroupId}`, Allocation);
    await settle();
    expect(checked()).toEqual([expect.stringContaining('Inwestycje')]);
    expect(api.groupGets).toContain(inwestycjeGroupId);
  });

  it('unknown group falls back to the whole', async () => {
    await open(`/allocation?group=${unknownGroupId}`, {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
    });

    expect(checked()).toEqual([expect.stringContaining('Całość')]);
    expect(api.groupGets).toHaveLength(0);
    expect(root().querySelector('[role="alert"]')).toBeNull();
    expect(all('group-row')).toHaveLength(3);
    expect(one('plan-note')).toBeNull();
  });

  it('shows the empty state for a group', async () => {
    await open(`/allocation?group=${emeryturaGroupId}`, {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse({ ...notConfiguredAllocation, totalPln: 35000 }),
    });

    expect(plain(textOf(one('create-allocation')))).toContain('Utwórz alokację');
    expect(plain(textOf(one('copy-from-whole')))).toContain('Skopiuj z całości');
    expect(one('plan-note')).not.toBeNull();
    expect(one('allocation-view')).toBeNull();
    expect(one('delete-allocation')).toBeNull();

    await press(one('create-allocation'));
    expect(one('allocation-editor')).not.toBeNull();
    expect(all('editor-row')).toHaveLength(0);
    expect(api.groupPuts).toHaveLength(0);
  });

  it('copies from the whole without saving', async () => {
    await open(`/allocation?group=${emeryturaGroupId}`, {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse(notConfiguredAllocation),
    });

    await press(one('copy-from-whole'));

    expect(one('allocation-editor')).not.toBeNull();
    const names = await Promise.all((await inputs('group-name')).map((i) => i.getValue()));
    expect(names).toEqual(['Akcje/ETF', 'Obligacje', 'Kruszce']);
    const targets = await Promise.all((await inputs('group-target')).map((i) => i.getValue()));
    expect(targets).toEqual(['50', '40', '10']);
    const bands = await Promise.all((await inputs('group-band')).map((i) => i.getValue()));
    expect(bands).toEqual(['5', '5', '5']);
    expect(api.groupPuts).toHaveLength(0);
    expect(api.puts).toHaveLength(0);

    api.fetchSpy.mockRestore();
    TestBed.resetTestingModule();
    await open(`/allocation?group=${emeryturaGroupId}`, {
      get: () => jsonResponse(notConfiguredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse(notConfiguredAllocation),
    });
    expect(one('create-allocation')).not.toBeNull();
    expect(one('copy-from-whole')).toBeNull();
  });

  it('saves a group allocation', async () => {
    let saved = false;
    await open(`/allocation?group=${emeryturaGroupId}`, {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse(saved ? groupConfiguredAllocation : notConfiguredAllocation),
      groupPut: () => {
        saved = true;
        return new Response(null, { status: 204 });
      },
    });
    await press(one('copy-from-whole'));

    const save = () => one('save') as HTMLButtonElement;
    const targets = await inputs('group-target');
    await targets[2].setValue('9.9');
    harness.fixture.detectChanges();
    expect(plain(textOf(one('sum')))).toContain('99,9');
    expect(save().disabled).toBe(true);
    await targets[2].setValue('10');
    harness.fixture.detectChanges();
    expect(save().disabled).toBe(false);

    await press(save());

    expect(api.puts).toHaveLength(0);
    expect(api.groupPuts).toHaveLength(1);
    expect(api.groupPuts[0].groupId).toBe(emeryturaGroupId);
    expect(savedBody(api.groupPuts[0].body)).toEqual([
      { name: 'Akcje/ETF', assetClasses: [2, 3], targetPercent: 50, bandPp: 5 },
      { name: 'Obligacje', assetClasses: [1, 4], targetPercent: 40, bandPp: 5 },
      { name: 'Kruszce', assetClasses: [6], targetPercent: 10, bandPp: 5 },
    ]);
    expect(one('allocation-editor')).toBeNull();
    expect(one('allocation-view')).not.toBeNull();
    expect(all('group-row')).toHaveLength(1);
  });

  it('renders the group comparison', async () => {
    await open(`/allocation?group=${emeryturaGroupId}`, {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse(groupComparisonAllocation),
    });

    const rows = all('group-row').map((row) => plain(textOf(row)));
    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain('54,5');
    expect(rows[0]).toContain('45–55');
    expect(rows[0]).toMatch(/\+4,5 pp/);
    expect(rows[0]).toMatch(/\+ ?4 ?500/);
    expect(rows[1]).toContain('36,4');
    expect(rows[1]).toMatch(/-3 ?600/);
    expect(rows[2]).toContain('9,1');
    expect(rows[2]).toContain('7–13');
    const outside = plain(textOf(one('outside-row')));
    expect(outside).toContain('Poza alokacją');
    expect(outside).toContain(plain(formatMoney(5000)));
  });

  it('shows targets for an empty group', async () => {
    await open(`/allocation?group=${emeryturaGroupId}`, {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse(emptyGroupAllocation()),
    });

    const rows = all('group-row').map((row) => plain(textOf(row)));
    expect(rows).toHaveLength(1);
    expect(rows[0]).toContain('60');
    expect(rows[0]).toContain('55–65');
    expect(rows[0]).not.toContain('zł');
    expect(rows[0]).not.toMatch(/\bpp\b/);
    const headers = Array.from(root().querySelectorAll('th')).map((th) => plain(textOf(th)));
    for (const key of ['value', 'current', 'deviationPp', 'deviationPln']) {
      expect(headers).not.toContain(pl(`allocation.columns.${key}`));
    }
    expect(one('summary')).toBeNull();
    expect(one('group-no-value')).not.toBeNull();
  });

  it('deletes a group allocation', async () => {
    let deleted = false;
    await open(`/allocation?group=${emeryturaGroupId}`, {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse(deleted ? notConfiguredAllocation : groupConfiguredAllocation),
      groupDelete: () => {
        deleted = true;
        return new Response(null, { status: 204 });
      },
    });

    confirmed = false;
    await press(one('delete-allocation'));
    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    expect(api.groupDeletes).toHaveLength(0);

    confirmed = true;
    await press(one('delete-allocation'));
    await vi.waitFor(() => expect(api.groupDeletes).toEqual([emeryturaGroupId]));
    await settle();
    expect(one('create-allocation')).not.toBeNull();
    expect(one('delete-allocation')).toBeNull();

    await press(chip('Całość'));
    expect(one('allocation-view')).not.toBeNull();
    expect(one('delete-allocation')).toBeNull();
  });

  it('shows the plan note only for a group', async () => {
    await open(`/allocation?group=${emeryturaGroupId}`, {
      get: () => jsonResponse(configuredAllocation),
      groups: bothGroups,
      groupGet: () => jsonResponse(groupConfiguredAllocation),
    });
    expect(one('plan-note')).not.toBeNull();

    await press(chip('Całość'));
    expect(one('plan-note')).toBeNull();
  });
});
