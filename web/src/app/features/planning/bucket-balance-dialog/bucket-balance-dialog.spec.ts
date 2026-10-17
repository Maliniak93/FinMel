import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../../core/i18n/language';
import { cushionBucket, limitedBucket } from '../../../../testing/allocation-fixtures';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { BucketBalanceDialog } from './bucket-balance-dialog';

describe('BucketBalanceDialog', () => {
  let fixture: ComponentFixture<BucketBalanceDialog>;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    document.documentElement.lang = 'en';
    fetchSpy.mockRestore();
  });

  async function setup(data: { month: string; bucket: Record<string, unknown> }): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if ((input as Request).method === 'PUT') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });

    await TestBed.configureTestingModule({
      imports: [BucketBalanceDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BucketBalanceDialog);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function setInput(testId: string, value: string): void {
    const input = root().querySelector<HTMLInputElement>(`[data-testid="${testId}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('labels the amount of a cushion as the balance at the start of the month', async () => {
    await setup({ month: '2026-10', bucket: cushionBucket() });

    await TestBed.inject(LanguageService).setLanguage('pl');
    await settle();

    expect(root().textContent).toContain('saldo na początek miesiąca');
    expect(root().textContent).not.toContain('wpłacono w roku przed tym miesiącem');
  });

  it('labels the amount of an IKE counter as paid in during the year before the month', async () => {
    await setup({ month: '2026-10', bucket: limitedBucket('IKE') });

    await TestBed.inject(LanguageService).setLanguage('pl');
    await settle();

    expect(root().textContent).toContain('wpłacono w roku przed tym miesiącem');
    expect(root().textContent).not.toContain('saldo na początek miesiąca');
  });

  it('defaults the month to the viewed one and posts the amount to the chosen month, then closes with true', async () => {
    await setup({ month: '2026-10', bucket: cushionBucket() });
    const monthInput = root().querySelector<HTMLInputElement>(
      '[data-testid="bucket-balance-month"]',
    )!;
    expect(monthInput.value).toBe('2026-10');

    setInput('bucket-balance-month', '2026-12');
    setInput('bucket-balance-amount', '20000');
    (root().querySelector('[data-testid="bucket-balance-save"]') as HTMLElement).click();

    await vi.waitFor(() => expect(dialogRef.close).toHaveBeenCalledWith(true));
    const puts = fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method === 'PUT');
    expect(puts).toHaveLength(1);
    expect(puts[0].url).toContain(
      '/api/portfolio/allocation-buckets/bucket-Poduszka/balances/2026-12',
    );
    expect(JSON.parse(await puts[0].clone().text())).toEqual({ amount: 20000 });
  });
});
