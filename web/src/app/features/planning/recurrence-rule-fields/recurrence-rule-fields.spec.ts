import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { provideI18nTesting } from '../../../core/i18n/testing';
import { RecurrenceRuleFields } from './recurrence-rule-fields';

@Component({
  imports: [ReactiveFormsModule, RecurrenceRuleFields],
  template: `
    <form [formGroup]="form">
      <app-recurrence-rule-fields [dayKind]="dayKind()" />
    </form>
  `,
})
class Host {
  readonly dayKind = signal(0);
  readonly form = new FormGroup({
    intervalMonths: new FormControl(1),
    dayKind: new FormControl(0),
    dayOfMonth: new FormControl(5),
    shift: new FormControl(0),
  });
}

describe('RecurrenceRuleFields', () => {
  let fixture: ComponentFixture<Host>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  function field(name: string): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector(`[formControlName="${name}"]`);
  }

  it('hides the day field for the last business day', async () => {
    expect(field('dayOfMonth')).not.toBeNull();
    expect(field('shift')).not.toBeNull();
    for (const name of ['intervalMonths', 'dayKind', 'dayOfMonth', 'shift']) {
      expect(field(name)!.closest('mat-form-field')!.classList, name).toContain(
        'mat-mdc-form-field-label-always-float',
      );
    }

    fixture.componentInstance.dayKind.set(1);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(field('intervalMonths')).not.toBeNull();
    expect(field('dayKind')).not.toBeNull();
    expect(field('dayOfMonth')).toBeNull();
    expect(field('shift')).toBeNull();
  });
});
