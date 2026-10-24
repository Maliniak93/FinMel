import { ComponentFixture, TestBed } from '@angular/core/testing';

import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import type { ContributionResponse } from '../../../api/portfolio';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { ContributionBar } from './contribution-bar';

describe('ContributionBar', () => {
  let fixture: ComponentFixture<ContributionBar>;

  afterEach(async () => {
    await restoreEnglish();
  });

  async function render(contribution: ContributionResponse): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ContributionBar],
      providers: [provideI18nTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(ContributionBar);
    fixture.componentRef.setInput('contribution', contribution);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function bar(): HTMLElement {
    return root().querySelector<HTMLElement>('[data-testid="contribution-bar"]')!;
  }

  function fill(): HTMLElement {
    return root().querySelector<HTMLElement>('[data-testid="contribution-fill"]')!;
  }

  function text(): string {
    return (root().textContent ?? '').replace(/\s+/g, ' ');
  }

  const limit = 10000;

  it('thresholds', async () => {
    await render({ year: 2026, contributedPln: 7100, limitPln: limit });
    expect(bar().dataset['state']).toBe('calm');
    expect(fill().style.width).toBe('71%');
    expect(text()).toContain('left');
    TestBed.resetTestingModule();

    await render({ year: 2026, contributedPln: 9000, limitPln: limit });
    expect(bar().dataset['state']).toBe('warning');
    expect(fill().style.width).toBe('90%');
    TestBed.resetTestingModule();

    await render({ year: 2026, contributedPln: 10000, limitPln: limit });
    expect(bar().dataset['state']).toBe('warning');
    expect(fill().style.width).toBe('100%');
    TestBed.resetTestingModule();

    await render({ year: 2026, contributedPln: 10800, limitPln: limit });
    expect(bar().dataset['state']).toBe('error');
    expect(fill().style.width).toBe('100%');
    expect(root().querySelector('[data-testid="contribution-marker"]')).not.toBeNull();
    expect(text()).toContain('limit exceeded');

    const progressbar = root().querySelector('[role="progressbar"]')!;
    expect(progressbar.getAttribute('aria-label')).toBe('Contribution limit');

    await switchLanguage(fixture, 'pl');
    expect(root().querySelector('[role="progressbar"]')!.getAttribute('aria-label')).toBe(
      'Limit wpłat',
    );
    expect(text()).toContain('limit przekroczony');
  });

  it('no limit', async () => {
    await render({ year: 2026, contributedPln: 12000, limitPln: null });

    expect(root().querySelector('[role="progressbar"]')).toBeNull();
    expect(text()).toContain('Paid');
    expect(text()).toContain('in 2026');
    expect(text()).not.toContain('left');

    await switchLanguage(fixture, 'pl');
    expect(text()).toContain('Wpłacono');
    expect(text()).toContain('w 2026');
  });
});
