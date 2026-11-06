import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { restoreEnglish } from '../../../../testing/i18n';
import { readStylesheet, rulesFor } from '../../../../testing/stylesheet';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { RangeDialog, type RangeDialogData } from './range-dialog';

const data: RangeDialogData = {
  firstDate: '2025-01-10',
  lastDate: '2026-10-19',
  current: null,
};

describe('RangeDialog', () => {
  let fixture: ComponentFixture<RangeDialog>;
  let element: HTMLElement;
  let dialogRef: { close: ReturnType<typeof vi.fn> };

  afterEach(async () => {
    vi.useRealTimers();
    await restoreEnglish();
  });

  async function setup(dialogData: RangeDialogData = data): Promise<void> {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20));
    dialogRef = { close: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [RangeDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: dialogData },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RangeDialog);
    element = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function inputs(): HTMLInputElement[] {
    return [...element.querySelectorAll<HTMLInputElement>('input')];
  }

  function yearButtons(): HTMLButtonElement[] {
    return [...element.querySelectorAll<HTMLButtonElement>('.range-dialog__year')];
  }

  async function type(input: HTMLInputElement, value: string): Promise<void> {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('blur', { bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function apply(): Promise<void> {
    element.querySelector<HTMLButtonElement>('.range-dialog__apply')?.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('lists only years with data', async () => {
    await setup();

    expect(yearButtons().map((button) => button.textContent?.trim())).toEqual(['2025', '2026']);
  });

  it('year button fills the dates', async () => {
    await setup();

    yearButtons()[0].click();
    fixture.detectChanges();
    await fixture.whenStable();

    const [from, to] = inputs();
    expect(from.value).toBe('01.01.2025');
    expect(to.value).toBe('31.12.2025');

    await apply();

    expect(dialogRef.close).toHaveBeenCalledWith({ from: '2025-01-01', to: '2025-12-31' });
  });

  it('current year ends today', async () => {
    await setup();

    yearButtons()[1].click();
    fixture.detectChanges();
    await fixture.whenStable();

    const [from, to] = inputs();
    expect(from.value).toBe('01.01.2026');
    expect(to.value).toBe('20.10.2026');
  });

  it('rejects an impossible date', async () => {
    await setup();
    const [from, to] = inputs();

    await type(from, '31.02.2025');
    await type(to, '31.12.2025');
    await apply();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(element.querySelectorAll('mat-error').length).toBeGreaterThan(0);
  });

  it('rejects from after to', async () => {
    await setup();
    const [from, to] = inputs();

    await type(from, '10.06.2025');
    await type(to, '01.06.2025');
    await apply();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(element.querySelectorAll('mat-error').length).toBeGreaterThan(0);
  });

  it('rejects a range outside the history', async () => {
    await setup();
    const [from, to] = inputs();

    await type(from, '01.01.2020');
    await type(to, '31.12.2020');
    await apply();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(element.querySelectorAll('mat-error').length).toBeGreaterThan(0);
  });

  it('fits a 360px viewport', async () => {
    await setup();
    const css = readStylesheet('src/app/features/dashboard/range-dialog/range-dialog.scss');

    expect(rulesFor(css, /range-dialog__years/)).toMatch(/flex-wrap:\s*wrap/);
  });
});
