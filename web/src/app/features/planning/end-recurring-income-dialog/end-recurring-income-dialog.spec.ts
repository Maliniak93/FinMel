import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { EndRecurringIncomeDialog } from './end-recurring-income-dialog';

describe('EndRecurringIncomeDialog', () => {
  let fixture: ComponentFixture<EndRecurringIncomeDialog>;
  let component: EndRecurringIncomeDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => new Response(null, { status: 204 }));

    await TestBed.configureTestingModule({
      imports: [EndRecurringIncomeDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: { id: 'income-1', name: 'Wypłata', startDate: '2026-01-01' },
        },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EndRecurringIncomeDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('ends the cycle in the chosen month', async () => {
    await setup();

    const formField = element().querySelector('mat-form-field')!;
    expect(formField.classList).toContain('mat-mdc-form-field-label-always-float');

    component['form'].controls.endDate.setValue(new Date(2026, 11, 31));
    await component['onSubmit']();

    const [request] = fetchSpy.mock.calls.map((call: unknown[]) => call[0] as Request);
    expect(request.method).toBe('POST');
    expect(request.url).toContain('/api/portfolio/recurring-incomes/income-1/end');
    expect(await request.json()).toEqual({ endDate: '2026-12-31' });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('shows the end-month error under the field', async () => {
    await setup();

    component['form'].controls.endDate.setValue(null);
    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
    const formField = element().querySelector('mat-form-field')!;
    expect(formField.classList).toContain('mat-mdc-form-field-label-always-float');
    expect(formField.querySelector('mat-error')).not.toBeNull();
  });
});
