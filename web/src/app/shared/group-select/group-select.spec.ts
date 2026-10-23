import { OverlayContainer } from '@angular/cdk/overlay';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { jsonResponse } from '../../features/assets/asset-form/testing/asset-form-fixtures';
import {
  assetGroups,
  blueGroup,
  greenGroup,
  isGroupListRequest,
} from '../../features/groups/testing/group-fixtures';
import { selectOptionLabels } from '../../features/deposits/testing/deposit-fixtures';
import { provideI18nTesting } from '../../core/i18n/testing';
import { restoreEnglish } from '../../../testing/i18n';
import { groupColor } from '../colors';
import { GroupSelect } from './group-select';

@Component({
  imports: [ReactiveFormsModule, GroupSelect],
  template: `<form [formGroup]="form"><app-group-select formControlName="groupId" /></form>`,
})
class GroupSelectHost {
  readonly form = new FormGroup({ groupId: new FormControl<string | null>(null) });
}

function overlayOptions(): HTMLElement[] {
  return Array.from(
    TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelectorAll<HTMLElement>('mat-option'),
  );
}

function hasSwatch(option: HTMLElement, css: string): boolean {
  const probe = document.createElement('span');
  probe.style.backgroundColor = css;
  return Array.from(option.querySelectorAll<HTMLElement>('*')).some(
    (element) => element.style.backgroundColor === probe.style.backgroundColor,
  );
}

describe('GroupSelect', () => {
  let fixture: ComponentFixture<GroupSelectHost>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(groups = assetGroups): Promise<void> {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input) =>
        isGroupListRequest(input) ? jsonResponse(groups) : jsonResponse({}, 404),
      );

    await TestBed.configureTestingModule({
      imports: [GroupSelectHost],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(GroupSelectHost);
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('renders groups', async () => {
    await setup();

    const labels = await selectOptionLabels(fixture, 'groupId');
    const options = overlayOptions();

    expect(labels).toEqual(['No group', blueGroup.name, greenGroup.name]);
    expect(hasSwatch(options[1], groupColor(blueGroup.color))).toBe(true);
    expect(hasSwatch(options[2], groupColor(greenGroup.color))).toBe(true);
  });

  it('hidden without groups', async () => {
    await setup([]);

    expect((fixture.nativeElement as HTMLElement).querySelector('mat-select')).toBeNull();
  });
});
