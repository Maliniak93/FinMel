import { BreakpointObserver } from '@angular/cdk/layout';
import { computed, inject, Injectable } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';

export type ShellLayoutName = 'full' | 'rail' | 'bottom';

const FULL_QUERY = '(min-width: 1200px)';
const RAIL_QUERY = '(min-width: 720px)';

@Injectable({ providedIn: 'root' })
export class ShellLayout {
  private readonly state = toSignal(inject(BreakpointObserver).observe([FULL_QUERY, RAIL_QUERY]), {
    requireSync: true,
  });

  readonly layout = computed<ShellLayoutName>(() => {
    const { breakpoints } = this.state();
    if (breakpoints[FULL_QUERY]) {
      return 'full';
    }

    return breakpoints[RAIL_QUERY] ? 'rail' : 'bottom';
  });
}
