import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { SectionError } from './section-error';

@Component({
  imports: [SectionError],
  template: `
    <app-section-error id="custom" message="Custom failure" (retry)="retries = retries + 1" />
    <app-section-error id="default" />
  `,
})
class Host {
  retries = 0;
}

describe('SectionError', () => {
  async function render() {
    await TestBed.configureTestingModule({ providers: [provideI18nTesting()] }).compileComponents();
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return fixture;
  }

  it('alerts with a retry', async () => {
    const fixture = await render();
    const custom = (fixture.nativeElement as HTMLElement).querySelector('#custom') as HTMLElement;
    const alert = custom.matches('[role="alert"]') || custom.querySelector('[role="alert"]');
    expect(alert).toBeTruthy();
    expect(custom.textContent).toContain('Custom failure');

    const button = custom.querySelector('button') as HTMLButtonElement;
    expect(button.textContent?.trim()).toBe('Retry');
    button.click();
    expect(fixture.componentInstance.retries).toBe(1);
  });

  it('uses the default message', async () => {
    const fixture = await render();
    const fallback = (fixture.nativeElement as HTMLElement).querySelector(
      '#default',
    ) as HTMLElement;
    expect(fallback.textContent).toContain("Couldn't load data.");
  });
});
