import { TRANSLATIONS, restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import type { TargetAllocationResponse } from '../../../api/reporting';
import {
  configuredAllocation,
  notConfiguredAllocation,
} from '../../allocation/testing/target-allocation-fixtures';
import { jsonResponse, renderCard, requestUrl } from '../testing/dashboard-fixtures';
import { AllocationCard } from './allocation-card';

const unvalued = configuredAllocation.groups.map((group) => ({
  ...group,
  valuePln: 0,
  currentPercent: null,
  deviationPp: null,
  targetValuePln: null,
  deviationPln: null,
  isInBand: null,
}));

const allInBand: TargetAllocationResponse = {
  ...configuredAllocation,
  groups: configuredAllocation.groups.map((group) => ({ ...group, isInBand: true })),
};

const noValuation: TargetAllocationResponse = {
  ...configuredAllocation,
  asOf: null,
  basePln: 0,
  groups: unvalued,
};

const emptyBaseAndStale: TargetAllocationResponse = {
  ...configuredAllocation,
  isStale: true,
  basePln: 0,
  groups: unvalued,
};

describe('AllocationCard', () => {
  afterEach(async () => {
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  it('asks for a target when none is configured and loads the target-allocation endpoint', async () => {
    const { fixture, fetchSpy } = await renderCard(AllocationCard, {
      allocation: jsonResponse(notConfiguredAllocation),
    });
    await switchLanguage(fixture, 'pl');
    const element = fixture.nativeElement as HTMLElement;

    expect(textOf(element)).toContain('Ustaw alokację docelową');
    expect(
      fetchSpy.mock.calls
        .map(([input]) => requestUrl(input))
        .some((url) => url.includes('/api/reporting/target-allocation')),
    ).toBe(true);
  });

  it('counts the groups outside their band and links the title to the allocation page', async () => {
    const { fixture } = await renderCard(AllocationCard, {
      allocation: jsonResponse(configuredAllocation),
    });
    await switchLanguage(fixture, 'pl');
    const element = fixture.nativeElement as HTMLElement;

    expect(textOf(element)).toContain('2 z 3 grup poza pasmem');
    expect(element.querySelector('a[href="/allocation"]')).not.toBeNull();
  });

  it('says all groups are in band when none is outside', async () => {
    const { fixture } = await renderCard(AllocationCard, {
      allocation: jsonResponse(allInBand),
    });
    await switchLanguage(fixture, 'pl');

    expect(textOf(fixture.nativeElement as HTMLElement)).toContain('Wszystkie grupy w paśmie');
  });

  it('says there is no valuation when the target has never been valued', async () => {
    const { fixture } = await renderCard(AllocationCard, {
      allocation: jsonResponse(noValuation),
    });
    await switchLanguage(fixture, 'pl');

    expect(textOf(fixture.nativeElement as HTMLElement)).toContain('brak wyceny');
  });

  it('says there are no assets in the allocation and shows the stale chip when the base is empty and stale', async () => {
    const { fixture } = await renderCard(AllocationCard, {
      allocation: jsonResponse(emptyBaseAndStale),
    });
    await switchLanguage(fixture, 'pl');
    const element = fixture.nativeElement as HTMLElement;

    expect(textOf(element)).toContain('brak aktywów w alokacji');
    expect(textOf(element.querySelector('mat-chip'))).toBe(TRANSLATIONS.pl['dashboard.stale']);
  });
});
