import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { Skeleton } from './skeleton';

@Component({ imports: [Skeleton], template: `<app-skeleton [rows]="3" />` })
class Host {}

describe('Skeleton', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('appears after 300 ms', async () => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(Host);
    const element = fixture.nativeElement as HTMLElement;
    const rows = () => element.querySelectorAll('.skeleton__row').length;
    fixture.detectChanges();
    expect(rows()).toBe(0);

    await vi.advanceTimersByTimeAsync(299);
    fixture.detectChanges();
    expect(rows()).toBe(0);

    await vi.advanceTimersByTimeAsync(2);
    fixture.detectChanges();
    expect(rows()).toBe(3);
    expect(element.querySelector('app-skeleton')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('animates only without reduced motion', () => {
    const css = readFileSync(
      resolve(process.cwd(), 'src/app/shared/skeleton/skeleton.scss'),
      'utf8',
    );
    const media = css.indexOf('@media (prefers-reduced-motion: no-preference)');
    expect(media).toBeGreaterThanOrEqual(0);
    expect(css.indexOf('animation')).toBeGreaterThan(media);
    expect(css.slice(0, media)).not.toMatch(/animation\s*:/);
  });
});
