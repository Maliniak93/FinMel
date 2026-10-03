import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import { restoreEnglish } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatMoney } from '../../../shared/format';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  eurCashCandidate,
  eurSavingsCandidate,
  onlySelectOptionGroups,
  onlySelectOptionLabels,
  onlySelectTriggerText,
  pickOnlySelectOption,
  plnCashCandidate,
  plnSavingsCandidate,
  requestMethod,
  requestedCandidateClasses,
  savingsPortfolioId,
  transferCandidateRequests,
  transferCandidatesFor,
  type TransferCandidateFixture,
} from '../testing/deposit-fixtures';
import { savingsAccountResponse } from '../testing/savings-account-fixtures';
import { SavingsAccountFormDialog } from '../savings-account-form-dialog/savings-account-form-dialog';
import { PayoutDestinationField } from './payout-destination-field';

@Component({
  imports: [ReactiveFormsModule, PayoutDestinationField],
  template: `
    <app-payout-destination-field
      [formControl]="control"
      [currency]="currency"
      [portfolioId]="portfolioId"
      [offerKeep]="offerKeep"
    />
  `,
})
class HostComponent {
  control = new FormControl<string | null>(null);
  currency = 'PLN';
  portfolioId = savingsPortfolioId;
  offerKeep = false;
}

describe('PayoutDestinationField', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let savingsCandidates: TransferCandidateFixture[];

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(offerKeep = false): Promise<void> {
    savingsCandidates = [plnSavingsCandidate];
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (
        requestMethod(input) === 'GET' &&
        requestUrl(input).includes('/api/portfolio/transfer-candidates')
      ) {
        return jsonResponse(
          transferCandidatesFor(
            new URL(requestUrl(input)),
            { PLN: [plnCashCandidate], EUR: [eurCashCandidate] },
            { PLN: savingsCandidates, EUR: [eurSavingsCandidate] },
          ),
        );
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });
    dialog = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideI18nTesting(), { provide: MatDialog, useValue: dialog }],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    host.offerKeep = offerKeep;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function optionText(candidate: TransferCandidateFixture, currency = 'PLN'): string {
    return `${candidate.name} — ${candidate.portfolioName} (${formatMoney(candidate.balance, currency)})`;
  }

  it('lists the PLN Cash and PLN savings candidates in two groups, then "New savings account…"', async () => {
    await setup();

    const requested = requestedCandidateClasses(fetchSpy);
    expect(requested).toContain('Cash');
    expect(requested).toContain('Savings');
    for (const url of transferCandidateRequests(fetchSpy)) {
      expect(url.searchParams.get('currency')).toBe('PLN');
    }

    const groups = await onlySelectOptionGroups(fixture);
    expect(groups.map((group) => group.label)).toEqual(['Cash', 'Savings accounts']);
    expect(groups[0].options).toEqual([optionText(plnCashCandidate)]);
    expect(groups[1].options).toEqual([optionText(plnSavingsCandidate)]);

    const labels = await onlySelectOptionLabels(fixture);
    expect(labels.some((label) => label.includes(eurCashCandidate.name))).toBe(false);
    expect(labels.some((label) => label.includes(eurSavingsCandidate.name))).toBe(false);
    expect(labels.at(-1)).toMatch(/^New savings account/);
  });

  it('does not offer "Keep in the deposit" when it is not enabled', async () => {
    await setup(false);

    const labels = await onlySelectOptionLabels(fixture);

    expect(labels.some((label) => /keep in the deposit/i.test(label))).toBe(false);
  });

  it('offers "Keep in the deposit" first when it is enabled', async () => {
    await setup(true);

    const labels = await onlySelectOptionLabels(fixture);

    expect(labels[0]).toMatch(/keep in the deposit/i);
    expect(labels.at(-1)).toMatch(/^New savings account/);
    expect(onlySelectTriggerText(fixture)).toContain('Keep in the deposit');
    expect(host.control.value ?? null).toBeNull();
  });

  it('picking a candidate sets the control to its asset id', async () => {
    await setup();

    await pickOnlySelectOption(fixture, plnSavingsCandidate.name);

    expect(host.control.value).toBe(plnSavingsCandidate.assetId);
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('picking "New savings account…" opens the account dialog for the deposit currency and portfolio', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(undefined) });

    await pickOnlySelectOption(fixture, 'New savings account');

    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(dialog.open).toHaveBeenCalledWith(
      SavingsAccountFormDialog,
      expect.objectContaining({
        data: expect.objectContaining({ currency: 'PLN', portfolioId: savingsPortfolioId }),
      }),
    );
  });

  it('a created account reloads the candidates and is selected', async () => {
    await setup();
    const created = savingsAccountResponse({
      assetId: 'abababab-abab-abab-abab-abababababab',
      name: 'Fresh account',
    });
    dialog.open.mockImplementation(() => {
      savingsCandidates = [
        ...savingsCandidates,
        {
          assetId: created.assetId,
          name: created.name,
          portfolioId: created.portfolioId,
          portfolioName: created.portfolioName,
          balance: 0,
        },
      ];
      return { afterClosed: () => of(created) };
    });
    const savingsRequestsBefore = requestedCandidateClasses(fetchSpy).filter(
      (assetClass) => assetClass === 'Savings',
    ).length;

    await pickOnlySelectOption(fixture, 'New savings account');
    await vi.waitFor(() =>
      expect(
        requestedCandidateClasses(fetchSpy).filter((assetClass) => assetClass === 'Savings').length,
      ).toBeGreaterThan(savingsRequestsBefore),
    );
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.control.value).toBe(created.assetId);
    await vi.waitFor(() => expect(onlySelectTriggerText(fixture)).toContain('Fresh account'));
    const groups = await onlySelectOptionGroups(fixture);
    expect(groups[1].options.some((label) => label.includes('Fresh account'))).toBe(true);
  });

  it.each([
    ['a previously picked candidate', plnCashCandidate.assetId],
    ['nothing', null],
  ])('cancelling the account dialog restores %s', async (_case, previous) => {
    await setup(true);
    host.control.setValue(previous);
    fixture.detectChanges();
    await fixture.whenStable();
    dialog.open.mockReturnValue({ afterClosed: () => of(undefined) });

    await pickOnlySelectOption(fixture, 'New savings account');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(host.control.value ?? null).toBe(previous);
  });

  it('a dialog closed with false counts as a cancel', async () => {
    await setup();
    host.control.setValue(plnCashCandidate.assetId);
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await pickOnlySelectOption(fixture, 'New savings account');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.control.value).toBe(plnCashCandidate.assetId);
  });
});
