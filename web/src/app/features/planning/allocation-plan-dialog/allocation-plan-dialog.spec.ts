import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormArray, FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import {
  allocation,
  cushionBucket,
  firstPlanBuckets,
  limitedBucket,
} from '../../../../testing/allocation-fixtures';
import {
  findControl,
  jsonResponse,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { AllocationPlanDialog } from './allocation-plan-dialog';

describe('AllocationPlanDialog', () => {
  let fixture: ComponentFixture<AllocationPlanDialog>;
  let component: AllocationPlanDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(
    buckets: Record<string, unknown>[] = firstPlanBuckets(),
    accounts: Record<string, unknown>[] = [],
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if ((input as Request).method === 'PUT') {
        return new Response(null, { status: 204 });
      }
      if (requestUrl(input).includes('/accounts')) {
        return jsonResponse(accounts);
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });

    await TestBed.configureTestingModule({
      imports: [AllocationPlanDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            month: '2026-10',
            allocation: allocation({ effectiveFrom: '2026-10', buckets }),
          },
        },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AllocationPlanDialog);
    component = fixture.componentInstance;
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function form(): FormGroup {
    return component['form'] as FormGroup;
  }

  function rowGroups(): FormGroup[] {
    return (findControl(form(), 'rows') as FormArray).controls as FormGroup[];
  }

  function rowElements(): HTMLElement[] {
    return Array.from(root().querySelectorAll<HTMLElement>('[data-testid="plan-row"]'));
  }

  function screenNames(): string[] {
    return rowElements().map(
      (row) => row.querySelector<HTMLInputElement>('[data-testid="row-name"]')!.value,
    );
  }

  function rowOf(name: string): HTMLElement {
    return rowElements().find(
      (row) => row.querySelector<HTMLInputElement>('[data-testid="row-name"]')!.value === name,
    )!;
  }

  async function clickIn(name: string, testId: string): Promise<void> {
    const target = rowOf(name).querySelector<HTMLElement>(`[data-testid="${testId}"]`)!;
    const clickable = target.matches('input,button')
      ? target
      : (target.querySelector<HTMLElement>('input,button') ?? target);
    clickable.click();
    await settle();
  }

  function flagOf(name: string): boolean {
    const group = rowGroups().find((candidate) => candidate.getRawValue().name === name)!;
    return group.getRawValue().isRemainder as boolean;
  }

  async function setShare(name: string, share: number): Promise<void> {
    const group = rowGroups().find((candidate) => candidate.getRawValue().name === name)!;
    findControl(group, 'share').setValue(share);
    await settle();
  }

  function saveButton(): HTMLButtonElement {
    const target = root().querySelector<HTMLElement>('[data-testid="save"]')!;
    return (
      target.matches('button') ? target : target.querySelector('button')
    ) as HTMLButtonElement;
  }

  it('moves a row up and down only inside its group', async () => {
    await setup();

    expect(screenNames()).toEqual(['Wakacje', 'Akcje', 'Lokata', 'ETF']);

    await clickIn('Akcje', 'move-up');
    await clickIn('Wakacje', 'move-down');
    await clickIn('Wakacje', 'move-up');
    await clickIn('ETF', 'move-down');

    expect(screenNames()).toEqual(['Wakacje', 'Akcje', 'Lokata', 'ETF']);

    await clickIn('Akcje', 'move-down');

    expect(screenNames()).toEqual(['Wakacje', 'Lokata', 'Akcje', 'ETF']);

    await clickIn('Akcje', 'move-up');

    expect(screenNames()).toEqual(['Wakacje', 'Akcje', 'Lokata', 'ETF']);
  });

  it('adds and removes rows', async () => {
    await setup();

    root().querySelector<HTMLElement>('[data-testid="add-row"]')!.click();
    await settle();

    expect(rowElements()).toHaveLength(5);

    await clickIn('Lokata', 'remove-row');

    expect(screenNames()).not.toContain('Lokata');
    expect(rowElements()).toHaveLength(4);
  });

  it('clears the previous remainder when another row is picked', async () => {
    await setup();

    expect(flagOf('ETF')).toBe(true);

    await clickIn('Akcje', 'row-remainder');

    expect(flagOf('Akcje')).toBe(true);
    expect(flagOf('ETF')).toBe(false);
    expect(rowGroups().filter((group) => group.getRawValue().isRemainder)).toHaveLength(1);
  });

  it('disables save while the percent sum is over 100 or no remainder is chosen in a non-empty plan', async () => {
    await setup();

    expect(saveButton().disabled).toBe(false);

    await setShare('Akcje', 60);

    expect(saveButton().disabled).toBe(true);

    await setShare('Akcje', 50);

    expect(saveButton().disabled).toBe(false);

    await clickIn('ETF', 'row-remainder');

    expect(flagOf('ETF')).toBe(false);
    expect(saveButton().disabled).toBe(true);

    for (const name of ['Wakacje', 'Akcje', 'Lokata', 'ETF']) {
      await clickIn(name, 'remove-row');
    }

    expect(rowElements()).toHaveLength(0);
    expect(saveButton().disabled).toBe(false);
  });

  it('keeps an existing cushion first and unmovable, without a share or a remainder choice, and offers no second one', async () => {
    await setup([cushionBucket(), ...firstPlanBuckets()]);

    expect(screenNames()).toEqual(['Poduszka', 'Wakacje', 'Akcje', 'Lokata', 'ETF']);
    const cushionRow = rowOf('Poduszka');
    const button = (testId: string): HTMLButtonElement =>
      cushionRow.querySelector<HTMLElement>(`[data-testid="${testId}"]`) as HTMLButtonElement;
    expect(button('move-up').disabled).toBe(true);
    expect(button('move-down').disabled).toBe(true);
    expect(cushionRow.querySelector('[data-testid="row-remainder"]')).toBeNull();
    expect(cushionRow.querySelector('[data-testid="row-share"]')).toBeNull();
    expect(cushionRow.querySelector('[data-testid="row-target-months"]')).not.toBeNull();
    expect(cushionRow.querySelector('[data-testid="row-starting-balance"]')).toBeNull();
    expect(root().querySelector('[data-testid="add-cushion"]')).toBeNull();

    await clickIn('Wakacje', 'move-up');

    expect(screenNames()).toEqual(['Poduszka', 'Wakacje', 'Akcje', 'Lokata', 'ETF']);
  });

  it('adds a new cushion first and puts its target months and starting balance without a share', async () => {
    await setup();

    root().querySelector<HTMLElement>('[data-testid="add-cushion"]')!.click();
    await settle();

    expect(screenNames()).toEqual(['', 'Wakacje', 'Akcje', 'Lokata', 'ETF']);
    expect(root().querySelector('[data-testid="add-cushion"]')).toBeNull();
    expect(rowElements()[0].querySelector('[data-testid="row-starting-balance"]')).not.toBeNull();
    const cushion = rowGroups()[0];
    findControl(cushion, 'name').setValue('Poduszka');
    findControl(cushion, 'targetMonths').setValue(6);
    findControl(cushion, 'startingBalance').setValue(20000);
    await settle();

    saveButton().click();

    await vi.waitFor(() => {
      const puts = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'PUT');
      expect(puts).toHaveLength(1);
    });
    const request = fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .find((candidate: Request) => candidate.method === 'PUT')!;
    const body = JSON.parse(await request.clone().text()) as {
      buckets: Record<string, unknown>[];
    };
    expect(body.buckets[0]).toMatchObject({
      name: 'Poduszka',
      kind: 2,
      targetMonths: 6,
      startingBalance: 20000,
      isRemainder: false,
    });
    expect(body.buckets[0]['share'] ?? null).toBeNull();
    expect(body.buckets[0]['shareType'] ?? null).toBeNull();
  });

  it('puts the plan to the chosen month with the rows in on-screen order', async () => {
    await setup();
    findControl(form(), 'effectiveFrom').setValue('2027-01');
    await clickIn('Lokata', 'move-up');
    expect(screenNames()).toEqual(['Wakacje', 'Lokata', 'Akcje', 'ETF']);

    saveButton().click();

    await vi.waitFor(() => {
      const puts = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'PUT');
      expect(puts).toHaveLength(1);
      expect(puts[0].url).toContain('/api/portfolio/allocation-plan/versions/2027-01');
    });
    const request = fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .find((candidate: Request) => candidate.method === 'PUT')!;
    const body = JSON.parse(await request.clone().text()) as {
      buckets: Record<string, unknown>[];
    };
    expect(body.buckets.map((bucket) => bucket['name'])).toEqual([
      'Wakacje',
      'Lokata',
      'Akcje',
      'ETF',
    ]);
    expect(body.buckets[0]).toMatchObject({
      bucketId: 'bucket-Wakacje',
      kind: 1,
      shareType: 0,
      share: 500,
      isRemainder: false,
    });
    expect(body.buckets[3]).toMatchObject({ bucketId: 'bucket-ETF', isRemainder: true });
    await vi.waitFor(() => expect(dialogRef.close).toHaveBeenCalledWith(true));
  });

  it('offers only the non-archived accounts as the reserve account and posts the chosen one', async () => {
    await setup(firstPlanBuckets(), [
      { id: 'acc-savings', name: 'Konto oszczędnościowe', wrapper: 0, isArchived: false },
      { id: 'acc-old', name: 'Stare konto', wrapper: 0, isArchived: true },
    ]);
    root()
      .querySelector<HTMLElement>('[data-testid="reserve-account"] .mat-mdc-select-trigger')!
      .click();
    await settle();

    const options = Array.from(document.querySelectorAll<HTMLElement>('mat-option'));
    const names = options.map((option) => option.textContent!.trim());
    expect(names).toContain('Konto oszczędnościowe');
    expect(names).not.toContain('Stare konto');
    options.find((option) => option.textContent!.includes('Konto oszczędnościowe'))!.click();
    await settle();

    saveButton().click();

    await vi.waitFor(() => {
      const puts = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'PUT');
      expect(puts).toHaveLength(1);
    });
    const request = fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .find((candidate: Request) => candidate.method === 'PUT')!;
    const body = JSON.parse(await request.clone().text()) as { reserveAccountId: string | null };
    expect(body.reserveAccountId).toBe('acc-savings');
    await vi.waitFor(() => expect(dialogRef.close).toHaveBeenCalledWith(true));
  });

  describe('limited buckets', () => {
    const accounts = [
      { id: 'acc-ike', name: 'IKE Dominik', wrapper: 1, isArchived: false },
      { id: 'acc-ikze', name: 'IKZE Dominik', wrapper: 2, isArchived: false },
      { id: 'acc-xtb', name: 'XTB', wrapper: 0, isArchived: false },
    ];

    async function addLimitedRow(kind: number): Promise<FormGroup> {
      root().querySelector<HTMLElement>('[data-testid="add-row"]')!.click();
      await settle();
      const group = rowGroups()[4];
      findControl(group, 'kind').setValue(kind);
      component['onKindChanged'](group as never);
      await settle();
      return group;
    }

    function accountNames(group: FormGroup): string[] {
      return (component['accountOptions'](group as never) as { name: string }[]).map(
        (account) => account.name,
      );
    }

    it('offers only the accounts with the matching wrapper for IKE, IKZE and IKZE JDG', async () => {
      await setup(firstPlanBuckets(), accounts);

      const group = await addLimitedRow(3);
      expect(accountNames(group)).toEqual(['IKE Dominik']);

      findControl(group, 'kind').setValue(4);
      expect(accountNames(group)).toEqual(['IKZE Dominik']);

      findControl(group, 'kind').setValue(5);
      expect(accountNames(group)).toEqual(['IKZE Dominik']);

      findControl(group, 'kind').setValue(0);
      expect(accountNames(group)).toEqual(['IKE Dominik', 'IKZE Dominik', 'XTB']);
    });

    it('drops a chosen account that does not fit the new kind', async () => {
      await setup(firstPlanBuckets(), accounts);
      root().querySelector<HTMLElement>('[data-testid="add-row"]')!.click();
      await settle();
      const group = rowGroups()[4];
      findControl(group, 'accountId').setValue('acc-xtb');

      findControl(group, 'kind').setValue(3);
      component['onKindChanged'](group as never);

      expect(findControl(group, 'accountId').value).toBeNull();
    });

    it('asks for the contributed value on a new limited row only and never offers it as the remainder', async () => {
      await setup([limitedBucket('IKE', { bucketId: 'bucket-IKE' }), ...firstPlanBuckets()]);

      expect(rowOf('IKE').querySelector('[data-testid="row-contributed"]')).toBeNull();
      expect(rowOf('IKE').querySelector('[data-testid="row-remainder"]')).toBeNull();
      expect(rowOf('Wakacje').querySelector('[data-testid="row-contributed"]')).toBeNull();

      root().querySelector<HTMLElement>('[data-testid="add-row"]')!.click();
      await settle();
      expect(rowElements()[5].querySelector('[data-testid="row-contributed"]')).toBeNull();
      expect(rowElements()[5].querySelector('[data-testid="row-remainder"]')).not.toBeNull();

      const group = rowGroups()[5];
      findControl(group, 'isRemainder').setValue(true);
      findControl(group, 'kind').setValue(3);
      component['onKindChanged'](group as never);
      await settle();

      expect(findControl(group, 'isRemainder').value).toBe(false);
      expect(rowElements()[5].querySelector('[data-testid="row-contributed"]')).not.toBeNull();
      expect(rowElements()[5].querySelector('[data-testid="row-remainder"]')).toBeNull();
      expect(rowElements()[5].textContent).toContain('2026');
    });

    it('puts the contributed value of a new limited row as startingBalance', async () => {
      await setup(firstPlanBuckets(), accounts);
      const group = await addLimitedRow(3);
      findControl(group, 'name').setValue('IKE');
      findControl(group, 'share').setValue(10);
      findControl(group, 'accountId').setValue('acc-ike');
      findControl(group, 'startingBalance').setValue(26000);
      await setShare('Akcje', 40);

      saveButton().click();

      await vi.waitFor(() => {
        const puts = fetchSpy.mock.calls
          .map((call: unknown[]) => call[0] as Request)
          .filter((request: Request) => request.method === 'PUT');
        expect(puts).toHaveLength(1);
      });
      const request = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .find((candidate: Request) => candidate.method === 'PUT')!;
      const body = JSON.parse(await request.clone().text()) as {
        buckets: Record<string, unknown>[];
      };
      expect(body.buckets[4]).toMatchObject({
        name: 'IKE',
        kind: 3,
        accountId: 'acc-ike',
        startingBalance: 26000,
        isRemainder: false,
      });
      expect(body.buckets[0]['startingBalance'] ?? null).toBeNull();
    });
  });
});
