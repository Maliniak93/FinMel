import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { restoreEnglish } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { findControl } from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  ContributionLimitDialog,
  type ContributionLimitDialogData,
} from './contribution-limit-dialog';

const accountId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

describe('ContributionLimitDialog', () => {
  let fixture: ComponentFixture<ContributionLimitDialog>;
  let component: ContributionLimitDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    data: ContributionLimitDialogData = { accountId, year: 2026, limitPln: null },
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => new Response(null, { status: 204 }));

    await TestBed.configureTestingModule({
      imports: [ContributionLimitDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ContributionLimitDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function form(): FormGroup {
    return component['form'] as FormGroup;
  }

  async function fill(values: Record<string, unknown>): Promise<void> {
    for (const [name, value] of Object.entries(values)) {
      const control = findControl(form(), name);
      control.setValue(value);
      control.markAsDirty();
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function writes(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method !== 'GET');
  }

  it('save PUTs the amount for the chosen year and closes with true', async () => {
    await setup();
    await fill({ year: 2026, amount: 26000 });

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(`/api/portfolio/accounts/${accountId}/contribution-limits/2026`);
    expect(await request.json()).toEqual({ amount: 26000 });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('remove DELETEs the limit of the year and closes with true', async () => {
    await setup({ accountId, year: 2026, limitPln: 26000 });

    await component['onRemove']();

    const [request] = writes();
    expect(request.method).toBe('DELETE');
    expect(request.url).toContain(`/api/portfolio/accounts/${accountId}/contribution-limits/2026`);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });
});
