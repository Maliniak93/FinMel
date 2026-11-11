import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';

import pl from '../../../../i18n/pl.json';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../../core/i18n/language';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  pickSelectOption,
  selectOptionLabels,
  selectTriggerState,
} from '../../deposits/testing/deposit-fixtures';
import {
  accountAId,
  accountBId,
  archivedContributionGroup,
  blueGroup,
  contributedGroup,
  greenGroup,
  groupAccounts,
} from '../testing/group-fixtures';
import { GroupFormDialog, type GroupFormDialogData } from './group-form-dialog';

describe('GroupFormDialog', () => {
  let fixture: ComponentFixture<GroupFormDialog>;
  let component: GroupFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  });

  async function setup(
    data: GroupFormDialogData,
    writeResponse: () => Response = () => jsonResponse(greenGroup, 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => writeResponse());

    await TestBed.configureTestingModule({
      imports: [GroupFormDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(GroupFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function writes(): Request[] {
    return fetchSpy.mock.calls.map((call: unknown[]) => call[0] as Request);
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function swatch(color: string): HTMLElement {
    const found = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      `[role=radiogroup] [role=radio][data-color="${color}"]`,
    );
    if (!found) {
      throw new Error(`No swatch '${color}'.`);
    }
    return found;
  }

  function isDisabled(element: HTMLElement): boolean {
    return element.hasAttribute('disabled') || element.getAttribute('aria-disabled') === 'true';
  }

  function errorText(id: string): string {
    return (
      (fixture.nativeElement as HTMLElement)
        .querySelector(`[data-testid="${id}"]`)
        ?.textContent?.trim() ?? ''
    );
  }

  function contributionRows(): HTMLElement[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        '[data-testid="contribution-row"]',
      ),
    );
  }

  function rowError(index: number): string {
    const row = contributionRows()[index];
    return row.querySelector('[data-testid="contribution-error"]')?.textContent?.trim() ?? '';
  }

  function setAmount(index: number, value: string): void {
    const input = contributionRows()[index].querySelector<HTMLInputElement>(
      'input[formcontrolname="amountPln"]',
    );
    if (!input) {
      throw new Error(`No amount input in row ${index}.`);
    }
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function addContributionRow(amount: string, account: string): Promise<void> {
    const index = contributionRows().length;
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="add-contribution-row"]')!
      .click();
    await settle();
    setAmount(index, amount);
    await pickSelectOption(fixture, 'accountId', account, index);
  }

  it('creates a group', async () => {
    await setup({ mode: 'create', groups: [blueGroup], accounts: groupAccounts });
    component['form'].controls.name.setValue('  Poduszka ');
    component['form'].controls.color.setValue('orange');
    component['form'].controls.targetPln.setValue(30000);

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(request.url).toContain('/api/portfolio/asset-groups');
    expect(await request.json()).toEqual({
      name: 'Poduszka',
      color: 'orange',
      targetPln: 30000,
      kind: 0,
      contributions: [],
      classAllocation: [],
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('refuses a blank name without calling the API', async () => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts });
    component['form'].controls.color.setValue('blue');

    await component['onSubmit']();

    expect(writes()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('updates through PUT on edit', async () => {
    await setup(
      {
        mode: 'edit',
        group: contributedGroup,
        groups: [blueGroup, contributedGroup],
        accounts: groupAccounts,
      },
      () => jsonResponse(contributedGroup),
    );
    expect(component['form'].controls.name.value).toBe('Poduszka');
    expect(component['form'].controls.color.value).toBe('green');
    component['form'].controls.name.setValue('Poduszka finansowa');
    component['form'].controls.color.setValue('purple');

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(`/api/portfolio/asset-groups/${contributedGroup.id}`);
    expect(await request.json()).toEqual({
      name: 'Poduszka finansowa',
      color: 'purple',
      targetPln: 30000,
      kind: 1,
      contributions: [
        { amountPln: 2000, accountId: accountAId },
        { amountPln: 500, accountId: null },
      ],
      classAllocation: [],
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('disables used colours', async () => {
    await setup({ mode: 'create', groups: [blueGroup, greenGroup], accounts: groupAccounts });

    expect(isDisabled(swatch('blue'))).toBe(true);
    expect(isDisabled(swatch('green'))).toBe(true);
    expect(isDisabled(swatch('orange'))).toBe(false);
    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll(
        '.group-form-dialog__palette [role=radio]',
      ),
    ).toHaveLength(12);
  });

  it('keeps the own colour selectable on edit', async () => {
    await setup({
      mode: 'edit',
      group: greenGroup,
      groups: [blueGroup, greenGroup],
      accounts: groupAccounts,
    });

    expect(isDisabled(swatch('green'))).toBe(false);
    expect(isDisabled(swatch('blue'))).toBe(true);
  });

  it.each([
    ['Conflict.DuplicateName', 'name-error'],
    ['Conflict.GroupColorTaken', 'color-error'],
    ['Validation.InvalidTarget', 'target-error'],
  ])('maps server errors: %s', async (errorCode, testId) => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts }, () =>
      jsonResponse({ title: 'Conflict', status: 409, detail: 'Rejected.', errorCode }, 409),
    );
    component['form'].controls.name.setValue('Poduszka');
    component['form'].controls.color.setValue('orange');

    await component['onSubmit']();
    await settle();

    expect(errorText(testId)).not.toBe('');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  function setAllocation(rows: [number, number | null][]): void {
    const allocation = component['form'].controls.allocation;
    allocation.clear();
    for (const [assetClass, percent] of rows) {
      component['addAllocationRow']();
      const row = allocation.at(allocation.length - 1);
      row.controls.assetClass.setValue(assetClass);
      row.controls.percent.setValue(percent);
    }
  }

  async function submitNamed(): Promise<void> {
    component['form'].controls.name.setValue('Emerytura');
    component['form'].controls.color.setValue('orange');
    await component['onSubmit']();
  }

  it('blocks save for a cushion without a target, an allocation off 100 or a repeated class', async () => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts });

    component['form'].controls.kind.setValue(1);
    component['form'].controls.targetPln.setValue(null);
    await submitNamed();
    expect(writes()).toEqual([]);

    component['form'].controls.kind.setValue(0);
    setAllocation([
      [1, 50],
      [9, 40],
    ]);
    await submitNamed();
    expect(writes()).toEqual([]);

    setAllocation([
      [1, 50],
      [1, 50],
    ]);
    await submitNamed();
    expect(writes()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('posts kind, contribution and allocation from a valid form', async () => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts });
    component['form'].controls.kind.setValue(0);
    await addContributionRow('2000', 'Konto A');
    await addContributionRow('2000', 'Konto B');
    setAllocation([
      [1, 50],
      [9, 50],
    ]);

    await submitNamed();

    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(await request.json()).toEqual({
      name: 'Emerytura',
      color: 'orange',
      targetPln: null,
      kind: 0,
      contributions: [
        { amountPln: 2000, accountId: accountAId },
        { amountPln: 2000, accountId: accountBId },
      ],
      classAllocation: [
        { assetClass: 1, percent: 50 },
        { assetClass: 9, percent: 50 },
      ],
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('removes a contribution row', async () => {
    await setup(
      {
        mode: 'edit',
        group: contributedGroup,
        groups: [blueGroup, contributedGroup],
        accounts: groupAccounts,
      },
      () => jsonResponse(contributedGroup),
    );
    await TestBed.inject(LanguageService).setLanguage('pl');
    await settle();

    expect(contributionRows()).toHaveLength(2);
    expect(selectTriggerState(fixture, 'accountId', 0).triggerText).toContain('Konto A');
    expect(selectTriggerState(fixture, 'accountId', 1).triggerText).toBe('Bez konta');

    contributionRows()[0]
      .querySelector<HTMLButtonElement>('[data-testid="remove-contribution-row"]')!
      .click();
    await settle();

    expect(contributionRows()).toHaveLength(1);
    expect(selectTriggerState(fixture, 'accountId', 0).triggerText).toBe('Bez konta');

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('PUT');
    expect((await request.json()).contributions).toEqual([{ amountPln: 500, accountId: null }]);
  });

  it('validates contribution rows', async () => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts }, () =>
      jsonResponse(
        {
          title: 'Validation',
          status: 400,
          detail: 'At most 20 contributions are allowed.',
          errorCode: 'Validation.TooManyContributions',
        },
        400,
      ),
    );
    await addContributionRow('0', 'Konto A');

    await submitNamed();
    await settle();
    expect(rowError(0)).not.toBe('');
    expect(writes()).toEqual([]);

    setAmount(0, '10.555');
    await submitNamed();
    await settle();
    expect(rowError(0)).not.toBe('');
    expect(writes()).toEqual([]);

    setAmount(0, '2000');
    await settle();
    expect(rowError(0)).toBe('');

    await submitNamed();
    await settle();
    expect(writes()).toHaveLength(1);
    expect(errorText('contributions-error')).not.toBe('');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('keeps the archived account of an existing row', async () => {
    await setup({
      mode: 'edit',
      group: archivedContributionGroup,
      groups: [archivedContributionGroup],
      accounts: groupAccounts,
    });
    await settle();

    expect(contributionRows()).toHaveLength(1);
    expect(selectTriggerState(fixture, 'accountId', 0).triggerText).toContain('Stare konto');

    const labels = await selectOptionLabels(fixture, 'accountId', 0);
    expect(labels.some((label) => label.includes('Stare konto'))).toBe(true);
    expect(labels.some((label) => label.includes('Zamknięte konto'))).toBe(false);
  });

  it('closes with false on cancel', async () => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts });

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  function saveButton(): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      'button[type=submit]',
    )!;
  }

  function hintText(): string {
    return (
      (fixture.nativeElement as HTMLElement).querySelector('mat-hint')?.textContent?.trim() ?? ''
    );
  }

  it('palette caption and taken colours', async () => {
    await setup({ mode: 'create', groups: [blueGroup, greenGroup], accounts: groupAccounts });
    await TestBed.inject(LanguageService).setLanguage('pl');
    component['form'].controls.color.setValue('orange');
    await settle();

    expect(errorText('palette-caption')).toBe(
      `Wybrany: ${pl.groups.colors.orange} · przekreślone kolory są już używane przez inną grupę`,
    );
    expect(isDisabled(swatch('blue'))).toBe(true);
    expect(swatch('blue').querySelector('mat-icon')).not.toBeNull();
    expect(swatch('orange').querySelector('mat-icon')).toBeNull();
    const tooltip = fixture.debugElement
      .query(By.css('[data-color="blue"]'))
      .injector.get(MatTooltip, null);
    expect(tooltip?.message).toBe('Kolor jest już używany przez inną grupę');
  });

  it('cushion requires a target', async () => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts });
    await TestBed.inject(LanguageService).setLanguage('pl');
    component['form'].controls.name.setValue('Poduszka');
    component['form'].controls.color.setValue('orange');
    component['form'].controls.kind.setValue(1);
    await settle();

    expect(saveButton().disabled).toBe(true);
    expect(hintText()).toBe('Opcjonalny, wymagany dla poduszki');

    component['form'].controls.kind.setValue(0);
    await settle();

    expect(saveButton().disabled).toBe(false);
  });

  it('opens with the allocation fieldset', async () => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts });
    const root = fixture.nativeElement as HTMLElement;
    const fieldset = root.querySelector<HTMLFieldSetElement>('[data-testid="allocation-editor"]');
    expect(fieldset?.tagName).toBe('FIELDSET');
    expect(fieldset!.querySelector('legend')).toBeTruthy();

    setAllocation([[1, 80]]);
    await settle();
    expect(root.querySelector('[data-testid="allocation-live-bar"]')).not.toBeNull();
  });

  it('allocation sum and duplicates', async () => {
    await setup({ mode: 'create', groups: [], accounts: groupAccounts });
    await TestBed.inject(LanguageService).setLanguage('pl');
    component['form'].controls.name.setValue('Emerytura');
    component['form'].controls.color.setValue('orange');
    await settle();

    setAllocation([[1, 80]]);
    await settle();
    expect(errorText('allocation-sum')).toContain('Suma: 80% z 100%');
    expect(errorText('allocation-sum')).toContain('brakuje 20 pp');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid="allocation-live-bar"]'),
    ).not.toBeNull();
    expect(saveButton().disabled).toBe(true);

    setAllocation([
      [1, 70],
      [9, 50],
    ]);
    await settle();
    expect(errorText('allocation-sum')).toContain('za dużo o 20 pp');
    expect(saveButton().disabled).toBe(true);

    setAllocation([
      [1, 50],
      [1, 50],
    ]);
    await settle();
    const alert = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="allocation-error"]',
    );
    expect(alert?.getAttribute('role')).toBe('alert');
    expect(errorText('allocation-error')).toBe('Każda klasa może wystąpić tylko raz.');
    expect(saveButton().disabled).toBe(true);

    setAllocation([
      [1, 50],
      [9, 50],
    ]);
    await settle();
    expect(errorText('allocation-sum')).toContain('Suma: 100% z 100%');
    expect(saveButton().disabled).toBe(false);

    setAllocation([]);
    await settle();
    expect(saveButton().disabled).toBe(false);
  });
});
