import type { Type } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { FormArray, FormGroup, type AbstractControl } from '@angular/forms';
import type { MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { provideNativeDateAdapter } from '@angular/material/core';
import { By } from '@angular/platform-browser';

import type { InstrumentDetailsResponse, InstrumentSearchResult } from '../../../../api/marketdata';
import type { AssetClass, AssetResponse } from '../../../../api/portfolio';
import { InstrumentPicker } from '../blocks/instrument-picker/instrument-picker';
import { provideI18nTesting } from '../../../../core/i18n/testing';

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

export function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
}

export function findControl(root: AbstractControl, name: string): AbstractControl {
  const found = searchControl(root, name);
  if (!found) {
    throw new Error(`No control named '${name}' in the form.`);
  }
  return found;
}

export function hasControl(root: AbstractControl, name: string): boolean {
  return searchControl(root, name) !== null;
}

function searchControl(root: AbstractControl, name: string): AbstractControl | null {
  const queue: AbstractControl[] = [root];
  while (queue.length > 0) {
    const current = queue.shift()!;
    if (current instanceof FormGroup) {
      for (const [childName, child] of Object.entries(current.controls)) {
        if (childName === name) {
          return child;
        }
        queue.push(child);
      }
    } else if (current instanceof FormArray) {
      queue.push(...current.controls);
    }
  }
  return null;
}

export async function toggleFirstTransaction(fixture: ComponentFixture<unknown>): Promise<void> {
  const element = fixture.nativeElement as HTMLElement;
  const checkbox = element.querySelector<HTMLInputElement>('mat-checkbox input[type="checkbox"]');
  if (!checkbox) {
    throw new Error('No "Add first transaction" checkbox rendered.');
  }
  checkbox.click();
  fixture.detectChanges();
  await fixture.whenStable();
}

export async function mountAssetForm<T>(
  component: Type<T>,
  assetClass: AssetClass,
  asset?: AssetResponse,
): Promise<ComponentFixture<T>> {
  await TestBed.configureTestingModule({
    imports: [component],
    providers: [provideNativeDateAdapter(), provideI18nTesting()],
  }).compileComponents();

  const fixture = TestBed.createComponent(component);
  fixture.componentRef.setInput('assetClass', assetClass);
  if (asset) {
    fixture.componentRef.setInput('asset', asset);
  }
  await fixture.whenStable();
  return fixture;
}

export async function pickInstrument(
  fixture: ComponentFixture<unknown>,
  option: InstrumentSearchResult | InstrumentDetailsResponse,
): Promise<void> {
  const picker = fixture.debugElement.query(By.directive(InstrumentPicker));
  if (!picker) {
    throw new Error('No instrument picker rendered.');
  }
  (picker.componentInstance as InstrumentPicker)['onInstrumentOptionSelected']({
    option: { value: option },
  } as MatAutocompleteSelectedEvent);
  fixture.detectChanges();
  await fixture.whenStable();
}

export async function showValidationErrors(
  fixture: ComponentFixture<unknown>,
  form: AbstractControl,
): Promise<void> {
  form.markAllAsTouched();
  fixture.detectChanges();
  await fixture.whenStable();
}

export function renderedText(fixture: ComponentFixture<unknown>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

export const portfolioId = '22222222-2222-2222-2222-222222222222';
export const instrumentId = '33333333-3333-3333-3333-333333333333';

export const cashAsset: AssetResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  portfolioId,
  assetClass: 0,
  valuationMode: 2,
  name: 'Checking account',
  currency: 'PLN',
  quantity: 0,
  transactionCount: 0,
  isArchived: false,
};

export const realEstateAsset: AssetResponse = {
  id: '55555555-5555-5555-5555-555555555555',
  portfolioId,
  assetClass: 7,
  valuationMode: 1,
  name: 'Apartment',
  currency: 'PLN',
  quantity: 0,
  manualValue: 650000,
  manualValueDate: '2020-06-15',
  transactionCount: 0,
  isArchived: false,
};

export const cryptoAsset: AssetResponse = {
  id: '44444444-4444-4444-4444-444444444444',
  portfolioId,
  assetClass: 5,
  valuationMode: 0,
  name: 'Bitcoin',
  currency: 'USD',
  quantity: 0.5,
  instrumentId,
  transactionCount: 0,
  isArchived: false,
};

export const cryptoInstrumentDetails: InstrumentDetailsResponse = {
  id: instrumentId,
  ticker: 'bitcoin',
  name: 'Bitcoin',
  assetClass: 5,
  quoteCurrency: 'USD',
  exchange: null,
  source: 2,
  verificationStatus: 0,
  lastPrice: 65000,
  lastPriceDate: '2026-08-04',
};

export const etfSearchResult: InstrumentSearchResult = {
  id: instrumentId,
  ticker: 'VWCE.DE',
  name: 'Vanguard FTSE All-World',
  assetClass: 3,
  quoteCurrency: 'EUR',
  verificationStatus: 0,
  lastPrice: 120,
  lastPriceDate: '2026-08-04',
  exchange: 'Xetra',
};

export const etfCandidate: InstrumentSearchResult = {
  id: null,
  ticker: 'VWCE.DE',
  name: 'Vanguard FTSE All-World UCITS ETF',
  assetClass: 3,
  quoteCurrency: 'EUR',
  verificationStatus: null,
  lastPrice: null,
  lastPriceDate: null,
  exchange: 'Xetra',
};
