import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { WealthLayout } from './wealth-layout';

const TABS = [
  ['accounts', 'Investment accounts'],
  ['bank', 'Bank accounts'],
  ['cash', 'Cash'],
  ['deposits', 'Deposits'],
  ['bonds', 'Bonds'],
  ['securities', 'Stocks & ETFs'],
  ['metals', 'Precious metals'],
] as const;

describe('WealthLayout', () => {
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'wealth',
            component: WealthLayout,
            children: TABS.map(([path]) => ({ path, children: [] })),
          },
        ]),
        provideI18nTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  it.each(TABS)('renders every tab: /wealth/%s selects "%s"', async (path, label) => {
    await harness.navigateByUrl(`/wealth/${path}`, WealthLayout);
    harness.detectChanges();
    const element = harness.routeNativeElement as HTMLElement;

    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Wealth');
    const nav = element.querySelector('nav[mat-tab-nav-bar]')!;
    expect(nav.getAttribute('aria-label')).toBe('Asset types');

    const links = Array.from(nav.querySelectorAll<HTMLAnchorElement>('a'));
    expect(links.map((a) => a.getAttribute('href'))).toEqual(TABS.map(([p]) => `/wealth/${p}`));
    expect(links.map((a) => a.textContent?.trim())).toEqual(TABS.map(([, l]) => l));
    const selected = links.filter((a) => a.getAttribute('aria-selected') === 'true');
    expect(selected.map((a) => a.textContent?.trim())).toEqual([label]);
  });
});
