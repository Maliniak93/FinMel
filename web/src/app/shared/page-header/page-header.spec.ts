import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PageHeader } from './page-header';

@Component({
  imports: [PageHeader],
  template: `<app-page-header title="Settings"
    ><button actions type="button">Go</button></app-page-header
  >`,
})
class Host {}

describe('PageHeader', () => {
  it('renders title and actions', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;

    const heading = element.querySelector('app-page-header h1');
    expect(heading?.textContent?.trim()).toBe('Settings');
    const action = element.querySelector('app-page-header button');
    expect(action?.textContent?.trim()).toBe('Go');
  });
});
