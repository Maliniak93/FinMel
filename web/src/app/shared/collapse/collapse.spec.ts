import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { Collapse } from './collapse';

@Component({
  imports: [Collapse],
  template: `<app-collapse [open]="open()"><button type="button">inside</button></app-collapse>`,
})
class Host {
  readonly open = signal(false);
}

describe('Collapse', () => {
  it('closed content is inert and out of the tab order', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const button = element.querySelector('button')!;

    expect(button).not.toBeNull();
    expect(button.closest('[inert]')).not.toBeNull();

    fixture.componentInstance.open.set(true);
    await fixture.whenStable();
    expect(button.closest('[inert]')).toBeNull();

    fixture.componentInstance.open.set(false);
    await fixture.whenStable();
    expect(button.closest('[inert]')).not.toBeNull();
  });
});
