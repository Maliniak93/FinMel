import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import { restoreEnglish } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatMoney } from '../../../shared/format';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  eurCashCandidate,
  eurBankCandidate,
  onlySelectOptionGroups,
  onlySelectOptionLabels,
  onlySelectTriggerText,
  pickOnlySelectOption,
  plnCashCandidate,
  plnBankCandidate,
  requestMethod,
  requestedCandidateClasses,
  transferCandidateRequests,
  transferCandidatesFor,
  type TransferCandidateFixture,
} from '../testing/deposit-fixtures';
import { PayoutDestinationField } from './payout-destination-field';

@Component({
  imports: [ReactiveFormsModule, PayoutDestinationField],
  template: `
    <app-payout-destination-field
      [formControl]="control"
      [currency]="currency"
      [offerKeep]="offerKeep"
    />
  `,
})
class HostComponent {
  control = new FormControl<string | null>(null);
  currency = 'PLN';
  offerKeep = false;
}

describe('PayoutDestinationField', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(offerKeep = false): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (
        requestMethod(input) === 'GET' &&
        requestUrl(input).includes('/api/portfolio/transfer-candidates')
      ) {
        return jsonResponse(
          transferCandidatesFor(
            new URL(requestUrl(input)),
            { PLN: [plnCashCandidate], EUR: [eurCashCandidate] },
            { PLN: [plnBankCandidate], EUR: [eurBankCandidate] },
          ),
        );
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });

    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideI18nTesting()],
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
    return `${candidate.name} (${formatMoney(candidate.balance, currency)})`;
  }

  it('lists the PLN Cash and PLN bank account candidates in two groups', async () => {
    await setup();

    const requested = requestedCandidateClasses(fetchSpy);
    expect(requested).toContain('Cash');
    expect(requested).toContain('BankAccount');
    for (const url of transferCandidateRequests(fetchSpy)) {
      expect(url.searchParams.get('currency')).toBe('PLN');
    }

    const groups = await onlySelectOptionGroups(fixture);
    expect(groups.map((group) => group.label)).toEqual(['Cash', 'Bank accounts']);
    expect(groups[0].options).toEqual([optionText(plnCashCandidate)]);
    expect(groups[1].options).toEqual([optionText(plnBankCandidate)]);

    const labels = await onlySelectOptionLabels(fixture);
    expect(labels.some((label) => label.includes(eurCashCandidate.name))).toBe(false);
    expect(labels.some((label) => label.includes(eurBankCandidate.name))).toBe(false);
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
    expect(labels.some((label) => /new savings account/i.test(label))).toBe(false);
    expect(onlySelectTriggerText(fixture)).toContain('Keep in the deposit');
    expect(host.control.value ?? null).toBeNull();
  });

  it('picking a candidate sets the control to its asset id', async () => {
    await setup();

    await pickOnlySelectOption(fixture, plnBankCandidate.name);

    expect(host.control.value).toBe(plnBankCandidate.assetId);
  });
});
