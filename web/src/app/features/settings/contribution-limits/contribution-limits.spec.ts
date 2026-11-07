import { ComponentFixture, TestBed } from '@angular/core/testing';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { ContributionLimits } from './contribution-limits';

const limitsPath = '/api/portfolio/contribution-limits';

const savedLimits = [
  { year: 2025, ikeAmount: 20000, ikzeAmount: null },
  { year: 2026, ikeAmount: 28260, ikzeAmount: 11304 },
];

describe('ContributionLimits', () => {
  let fixture: ComponentFixture<ContributionLimits>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'GET') {
        return jsonResponse(savedLimits);
      }
      return new Response(null, { status: 204 });
    });

    await TestBed.configureTestingModule({
      imports: [ContributionLimits],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(ContributionLimits);
    await fixture.whenStable();
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

  function rowYears(): number[] {
    return Array.from(
      root().querySelectorAll<HTMLElement>('[data-testid="contribution-limit-row"]'),
      (row) => Number(row.getAttribute('data-year')),
    );
  }

  function rowFor(year: number): HTMLElement {
    const row = root().querySelector<HTMLElement>(
      `[data-testid="contribution-limit-row"][data-year="${year}"]`,
    );
    if (!row) {
      throw new Error(`No row for ${year}.`);
    }
    return row;
  }

  function field(row: HTMLElement, testId: 'ike-amount' | 'ikze-amount'): HTMLInputElement {
    const input = row.querySelector<HTMLInputElement>(`[data-testid="${testId}"]`);
    if (!input) {
      throw new Error(`No ${testId} field in the row.`);
    }
    return input;
  }

  function typeInto(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function amount(input: HTMLInputElement): number {
    return Number(input.value.replace(/\s/g, '').replace(',', '.'));
  }

  function press(row: HTMLElement, testId: 'save-year' | 'remove-year' | 'add-year'): void {
    const button = row.querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`);
    if (!button) {
      throw new Error(`No ${testId} button in the row.`);
    }
    button.click();
  }

  function pressAdd(): void {
    const button = root().querySelector<HTMLButtonElement>('[data-testid="add-year"]');
    if (!button) {
      throw new Error('No add-year button.');
    }
    button.click();
  }

  function requestsWith(method: string, path: string): Request[] {
    return fetchSpy.mock.calls
      .map(([input]: [unknown]) => input as Request)
      .filter((request: Request) => request.method === method && request.url.includes(path));
  }

  it('lists years newest first', async () => {
    await setup();

    expect(rowYears()).toEqual([2026, 2025]);
    expect(amount(field(rowFor(2026), 'ike-amount'))).toBe(28260);
    expect(amount(field(rowFor(2026), 'ikze-amount'))).toBe(11304);
    expect(field(rowFor(2025), 'ikze-amount').value).toBe('');
  });

  it('rejects zero and negative amounts', async () => {
    await setup();

    for (const value of ['0', '-5']) {
      typeInto(field(rowFor(2026), 'ike-amount'), value);
      press(rowFor(2026), 'save-year');
      await settle();

      expect(rowFor(2026).querySelector('mat-error')).not.toBeNull();
    }

    expect(requestsWith('PUT', limitsPath)).toHaveLength(0);
  });

  it('prefills a new year and saves only on confirm', async () => {
    await setup();

    pressAdd();
    await settle();

    expect(amount(field(rowFor(2027), 'ike-amount'))).toBe(28260);
    expect(amount(field(rowFor(2027), 'ikze-amount'))).toBe(11304);
    expect(requestsWith('PUT', limitsPath)).toHaveLength(0);

    press(rowFor(2027), 'save-year');

    await vi.waitFor(() => expect(requestsWith('PUT', `${limitsPath}/2027`)).toHaveLength(1));
    const [put] = requestsWith('PUT', `${limitsPath}/2027`);
    expect(await put.json()).toEqual({ ikeAmount: 28260, ikzeAmount: 11304 });
  });

  it('clears a kind and removes a year', async () => {
    await setup();

    typeInto(field(rowFor(2026), 'ikze-amount'), '');
    press(rowFor(2026), 'save-year');

    await vi.waitFor(() => expect(requestsWith('PUT', `${limitsPath}/2026`)).toHaveLength(1));
    const [put] = requestsWith('PUT', `${limitsPath}/2026`);
    expect(await put.json()).toEqual({ ikeAmount: 28260, ikzeAmount: null });

    await settle();
    press(rowFor(2026), 'remove-year');

    await vi.waitFor(() => expect(requestsWith('DELETE', `${limitsPath}/2026`)).toHaveLength(1));
  });
});
