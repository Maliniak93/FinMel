import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PageHeader } from './page-header';

@Component({
  imports: [PageHeader],
  template: `<app-page-header title="Settings"
    ><nav breadcrumb>Back</nav>
    <button actions type="button">Go</button>
    <p subtitle>As of today</p></app-page-header
  >`,
})
class Host {}

@Component({
  imports: [PageHeader],
  template: `<app-page-header title="Bare" />`,
})
class BareHost {}

function scss(): string {
  return readFileSync(
    resolve(process.cwd(), 'src/app/shared/page-header/page-header.scss'),
    'utf8',
  );
}

describe('PageHeader', () => {
  it('renders title, breadcrumb above, actions beside and subtitle under', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;

    const heading = element.querySelector('app-page-header h1') as HTMLElement;
    expect(heading.textContent?.trim()).toBe('Settings');
    const action = element.querySelector('app-page-header button') as HTMLElement;
    expect(action.textContent?.trim()).toBe('Go');
    const breadcrumb = element.querySelector('app-page-header nav') as HTMLElement;
    const subtitle = element.querySelector('app-page-header p') as HTMLElement;

    expect(breadcrumb.textContent?.trim()).toBe('Back');
    expect(subtitle.textContent?.trim()).toBe('As of today');
    expect(heading.parentElement).toBe(action.parentElement);
    expect(breadcrumb.parentElement).not.toBe(heading.parentElement);
    expect(subtitle.parentElement).not.toBe(heading.parentElement);
    const order = (a: Node, b: Node) =>
      a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING;
    expect(order(breadcrumb, heading)).toBeTruthy();
    expect(order(heading, subtitle)).toBeTruthy();
  });

  it('renders nothing for omitted slots', async () => {
    const fixture = TestBed.createComponent(BareHost);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('app-page-header h1')?.textContent?.trim()).toBe('Bare');
    expect(element.querySelector('app-page-header button')).toBeNull();
    expect(element.querySelector('app-page-header nav')).toBeNull();
    expect(element.querySelector('app-page-header p')).toBeNull();
  });

  it('wraps and leaves the heading font to the global h1', () => {
    const css = scss();

    expect(css).toMatch(/flex-wrap:\s*wrap/);
    expect(css).not.toMatch(/--mat-sys-headline|font-size|font-weight|\bfont:/);
  });
});
