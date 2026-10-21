import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import {
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

  async function setup(
    bond: BondFixture = settledTosBond,
    preview: typeof tosRedemptionPreview = tosRedemptionPreview,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (requestMethod(input) === 'GET') {
        if (url.endsWith('/redemption-preview')) {
          return jsonResponse(preview);
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

  it('shows the redemption preview and the account cash line instead of a cash select', async () => {
    await setup();

    await vi.waitFor(() => {
      fixture.detectChanges();
      const text = renderedText(fixture);
      expect(text).toContain(formatDate(tosRedemptionPreview.date));
      expect(text).toContain(formatMoney(tosRedemptionPreview.capitalisedInterest, 'PLN'));
      expect(text).toContain(formatMoney(tosRedemptionPreview.tax, 'PLN'));
      expect(text).toContain(formatMoney(tosRedemptionPreview.proceeds, 'PLN'));
      expect(text).toContain(`To the cash of ${settledTosBond.accountName}`);
    });
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-select')).toBeNull();
  });

  it('shows the redeemed bond count with the thousands separator', async () => {
    await setup(settledTosBond, { ...tosRedemptionPreview, bondCount: 1234 });

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(renderedText(fixture)).toContain('1,234');
    });
  });

  it('redeems with no destination and closes with true', async () => {
    await setup();
    await vi.waitFor(() => expect(component['previewResource'].hasValue()).toBe(true));

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.url).toContain(`/api/portfolio/bonds/${settledTosBond.assetId}/redemption`);
    expect(await request.json()).toEqual({});
    expect(dialogRef.close).toHaveBeenCalledWith(true);
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
