import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { EmptyState } from './empty-state';

@Component({
  imports: [EmptyState],
  template: `
    <app-empty-state id="plain" icon="inbox" message="Nothing here" />
    <app-empty-state id="withAction" icon="inbox" message="Nothing here">
      <button action type="button">Add</button>
    </app-empty-state>
  `,
})
class Host {}

describe('EmptyState', () => {
  it('shows icon and message, and the action only when given', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const plain = element.querySelector('#plain') as HTMLElement;
    const withAction = element.querySelector('#withAction') as HTMLElement;

    for (const state of [plain, withAction]) {
      expect(state.querySelector('mat-icon')?.textContent?.trim()).toBe('inbox');
      expect(state.textContent).toContain('Nothing here');
    }
    expect(plain.querySelector('button')).toBeNull();
    expect(withAction.querySelector('button')?.textContent?.trim()).toBe('Add');
  });
});
