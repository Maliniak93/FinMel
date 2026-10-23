import { TestBed } from '@angular/core/testing';

import { stubViewport, type ViewportStub } from '../../../testing/match-media';
import { ShellLayout } from './shell-layout';

describe('ShellLayout', () => {
  let viewport: ViewportStub;

  afterEach(() => {
    viewport.restore();
  });

  function layoutAt(width: number): ShellLayout {
    viewport = stubViewport(width);

    return TestBed.inject(ShellLayout);
  }

  async function resizeTo(width: number): Promise<void> {
    viewport.setWidth(width);
    await new Promise((resolve) => setTimeout(resolve, 10));
  }

  it('picks the layout from the width at the breakpoints', () => {
    expect(layoutAt(1200).layout()).toBe('full');
    viewport.restore();
    TestBed.resetTestingModule();
    expect(layoutAt(1199).layout()).toBe('rail');
    viewport.restore();
    TestBed.resetTestingModule();
    expect(layoutAt(720).layout()).toBe('rail');
    viewport.restore();
    TestBed.resetTestingModule();
    expect(layoutAt(719).layout()).toBe('bottom');
  });

  it('follows the breakpoints when the width changes without a reload', async () => {
    const shellLayout = layoutAt(1400);
    expect(shellLayout.layout()).toBe('full');

    await resizeTo(1000);
    expect(shellLayout.layout()).toBe('rail');

    await resizeTo(500);
    expect(shellLayout.layout()).toBe('bottom');

    await resizeTo(1300);
    expect(shellLayout.layout()).toBe('full');
  });
});
