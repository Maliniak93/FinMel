import { Component, forwardRef, inject, input, signal } from '@angular/core';
import { NG_VALUE_ACCESSOR, type ControlValueAccessor } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';

import { groupColor } from '../../features/groups/group-color';
import { GroupList } from './group-list';

const NO_GROUP = '';

@Component({
  selector: 'app-group-select',
  imports: [MatFormFieldModule, MatSelectModule, TranslocoPipe],
  templateUrl: './group-select.html',
  styleUrl: './group-select.scss',
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => GroupSelect), multi: true },
  ],
})
export class GroupSelect implements ControlValueAccessor {
  protected readonly groupList = inject(GroupList);

  readonly labelKey = input('groups.field.group');

  protected readonly noGroup = NO_GROUP;
  protected readonly groupColor = groupColor;
  protected readonly selected = signal(NO_GROUP);
  protected readonly disabled = signal(false);

  private onChange: (value: string | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: string | null): void {
    this.selected.set(value ?? NO_GROUP);
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected choose(value: string): void {
    this.selected.set(value);
    this.onChange(value === NO_GROUP ? null : value);
  }

  protected touch(opened: boolean): void {
    if (!opened) {
      this.onTouched();
    }
  }
}
