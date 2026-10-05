import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import {
  cashCandidates,
  fundingCashId,
  otherCashId,
  settledTosBond,
  tosRedemptionPreview,
  unsettledTosBond,
  type BondFixture,
} from '../../../../testing/bond-fixtures';
import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatDate, formatMoney } from '../../../shared/format';
import {
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { requestMethod, writeRequests } from '../../deposits/testing/deposit-fixtures';
import { RedeemBondDialog } from './redeem-bond-dialog';

describe('RedeemBondDialog', () => {
  let fixture: ComponentFixture<RedeemBondDialog>;
  let component: RedeemBondDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(bond: BondFixture = settledTosBond): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (requestMethod(input) === 'GET') {
        if (url.endsWith('/redemption-preview')) {
          return jsonResponse(tosRedemptionPreview);
        }
        if (url.includes('/api/portfolio/transfer-candidates')) {
          return jsonResponse(cashCandidates);
        }
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return jsonResponse(bond);
    });

    await TestBed.configureTestingModule({
      imports: [RedeemBondDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { bond } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RedeemBondDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  function previewRequests(): unknown[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => requestUrl(input).endsWith('/redemption-preview'));
  }

  it('shows the redemption preview and defaults the Cash picker to the funding account', async () => {
    await setup();

    await vi.waitFor(() => {
      fixture.detectChanges();
      const text = renderedText(fixture);
      expect(text).toContain(formatDate(tosRedemptionPreview.date));
      expect(text).toContain(formatMoney(tosRedemptionPreview.capitalisedInterest, 'PLN'));
      expect(text).toContain(formatMoney(tosRedemptionPreview.tax, 'PLN'));
      expect(text).toContain(formatMoney(tosRedemptionPreview.proceeds, 'PLN'));
      expect(text).toContain('Wallet cash');
    });
    expect(component['form'].controls.destinationAssetId.value).toBe(fundingCashId);
  });

  it('redeems to the picked Cash and closes with true', async () => {
    await setup();
    await vi.waitFor(() => expect(component['previewResource'].hasValue()).toBe(true));
    component['form'].controls.destinationAssetId.setValue(otherCashId);

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${settledTosBond.portfolioId}/bonds/${settledTosBond.assetId}/redemption`,
    );
    expect(await request.json()).toEqual({ destinationAssetId: otherCashId });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('requires a Cash account when the bond was bought with new money', async () => {
    await setup({ ...settledTosBond, fundingAssetId: null, fundingAssetName: null });
    await vi.waitFor(() => expect(component['candidatesResource'].hasValue()).toBe(true));

    await component['onSubmit']();
    fixture.detectChanges();

    expect(writeRequests(fetchSpy)).toEqual([]);
    expect(renderedText(fixture)).toContain('Pick the cash account that receives the money.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('with unsettled periods says "Najpierw rozlicz okresy", loads no preview and closes asking to settle', async () => {
    await setup(unsettledTosBond);
    await switchLanguage(fixture, 'pl');

    expect(renderedText(fixture)).toContain('Najpierw rozlicz okresy');
    expect(previewRequests()).toEqual([]);

    const settle = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="settle-first-action"]',
    );
    expect(settle?.textContent).toContain('Rozlicz okresy');
    settle!.click();

    expect(dialogRef.close).toHaveBeenCalledWith('settle');
    expect(writeRequests(fetchSpy)).toEqual([]);
  });
});
