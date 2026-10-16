import { Component, inject, input } from '@angular/core';
import { ControlContainer, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';

import { DAY_KIND, SHIFT_OPTIONS } from '../recurrence';

@Component({
  selector: 'app-recurrence-rule-fields',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  viewProviders: [
    {
      provide: ControlContainer,
      useFactory: () => inject(ControlContainer, { skipSelf: true }),
    },
  ],
  templateUrl: './recurrence-rule-fields.html',
  styleUrl: './recurrence-rule-fields.scss',
})
export class RecurrenceRuleFields {
  readonly dayKind = input.required<number>();

  protected readonly dayKinds = DAY_KIND;
  protected readonly shiftOptions = SHIFT_OPTIONS;
}
