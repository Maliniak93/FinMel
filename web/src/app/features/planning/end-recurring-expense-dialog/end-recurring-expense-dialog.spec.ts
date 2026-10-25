import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { EndRecurringExpenseDialog } from './end-recurring-expense-dialog';

describe('EndRecurringExpenseDialog', () => {
  let fixture: ComponentFixture<EndRecurringExpenseDialog>;
  let component: EndRecurringExpenseDialog;
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
      imports: [EndRecurringExpenseDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { id: 'expense-1', name: 'Czynsz' } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EndRecurringExpenseDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('ends the cycle in the chosen month', async () => {
    await setup();

    const formField = (fixture.nativeElement as HTMLElement).querySelector('mat-form-field')!;
    expect(formField.classList).toContain('mat-mdc-form-field-label-always-float');

    component['form'].controls.endDate.setValue(new Date(2026, 11, 31));
    await component['onSubmit']();

    const [request] = fetchSpy.mock.calls.map((call: unknown[]) => call[0] as Request);
    expect(request.method).toBe('POST');
    expect(request.url).toContain('/api/portfolio/recurring-expenses/expense-1/end');
    expect(await request.json()).toEqual({ endDate: '2026-12-31' });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });
});
