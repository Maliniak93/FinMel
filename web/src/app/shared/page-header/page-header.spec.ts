import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

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

  it('leaves the heading size to the global h1', () => {
    const scss = readFileSync(
      resolve(process.cwd(), 'src/app/shared/page-header/page-header.scss'),
      'utf8',
    );

    expect(scss).not.toMatch(/--mat-sys-headline|font-size|\bfont:/);
  });
});
