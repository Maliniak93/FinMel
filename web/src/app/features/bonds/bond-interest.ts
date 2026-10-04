import { FormArray, FormControl, Validators } from '@angular/forms';
import { translate } from '@jsverse/transloco';

import { getApiMarketdataBondSeriesByCode } from '../../api/marketdata';
import {
  getApiPortfolioTransferCandidates,
  type BondPeriodResponse,
  type BondPeriodState,
  type BondResponse,
  type SettleBondInterestRequest,
  type TransferCandidateResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ASSET_CLASS } from '../assets/asset-class';
import { bondTypeInfo } from './bond-types';

// Mirrors BondPeriodState in declaration order: the enum travels as an int.
export const BOND_PERIOD_STATE = { Upcoming: 0, Due: 1, Settled: 2 } as const satisfies Record<
  string,
  BondPeriodState
>;

const BOND_CURRENCY = 'PLN';

export type SeriesRates = ReadonlyMap<number, number>;

export interface BondSettleSection {
  readonly bond: BondResponse;
  readonly periods: readonly BondPeriodResponse[];
  readonly coupon: boolean;
  readonly rates: FormArray<FormControl<number | null>>;
  readonly destination: FormControl<string | null>;
}

export function isDue(period: BondPeriodResponse): boolean {
  return Number(period.state) === BOND_PERIOD_STATE.Due;
}

export function isCouponBond(bond: BondResponse): boolean {
  return bondTypeInfo(bond.type)?.payout === 'coupon';
}

// A fixed-rate bond and every bond's first period take the rate from the terms, never from the request.
export function usesTermsRate(bond: BondResponse, periodIndex: number | string): boolean {
  return bondTypeInfo(bond.type)?.followingRate === 'fixed' || Number(periodIndex) === 1;
}

export function canSettle(bond: BondResponse): boolean {
  return Number(bond.duePeriodCount) > 0 && !bond.isArchived && !bond.portfolioIsArchived;
}

// A series missing from the catalog only loses the prefill: the user types the rates in.
export async function loadSeriesRates(
  seriesCode: string,
  signal: AbortSignal,
): Promise<SeriesRates | null> {
  const result = await getApiMarketdataBondSeriesByCode({ path: { code: seriesCode }, signal });
  if (result.error || !result.data) {
    return null;
  }
  return new Map(
    result.data.periodRates.map((rate) => [Number(rate.periodIndex), Number(rate.ratePercent)]),
  );
}

export async function loadCashCandidates(
  signal: AbortSignal,
): Promise<TransferCandidateResponse[]> {
  const result = await getApiPortfolioTransferCandidates({
    query: { currency: BOND_CURRENCY, assetClass: ASSET_CLASS.Cash },
    signal,
  });
  if (result.error) {
    throw new Error(
      readProblemDetails(result.error).detail ?? translate('bonds.settle.cashLoadFailed'),
    );
  }
  return result.data ?? [];
}

export function buildSettleSection(
  bond: BondResponse,
  seriesRates: SeriesRates | null,
  candidates: readonly TransferCandidateResponse[],
): BondSettleSection {
  const periods = bond.periods.filter(isDue);
  const coupon = isCouponBond(bond);
  const rates = new FormArray(
    periods.map((period) =>
      usesTermsRate(bond, period.index)
        ? new FormControl<number | null>({
            value: Number(bond.firstPeriodRatePercent),
            disabled: true,
          })
        : new FormControl<number | null>(seriesRates?.get(Number(period.index)) ?? null, [
            Validators.required,
            Validators.min(0),
            Validators.max(100),
          ]),
    ),
  );
  const fundingStillCandidate = candidates.some(
    (candidate) => candidate.assetId === bond.fundingAssetId,
  );
  const destination = new FormControl<string | null>(
    coupon && fundingStillCandidate ? (bond.fundingAssetId ?? null) : null,
    coupon ? [Validators.required] : [],
  );

  return { bond, periods, coupon, rates, destination };
}

// A FormArray whose controls are all disabled reports neither valid nor invalid, only disabled.
export function isSectionValid(section: BondSettleSection): boolean {
  return (section.rates.valid || section.rates.disabled) && section.destination.valid;
}

export function settleRequest(section: BondSettleSection): SettleBondInterestRequest {
  const values = section.rates.getRawValue();
  const periods = section.periods.map((period, i) =>
    usesTermsRate(section.bond, period.index)
      ? { periodIndex: Number(period.index) }
      : { periodIndex: Number(period.index), ratePercent: Number(values[i]) },
  );
  return section.coupon ? { periods, destinationAssetId: section.destination.value } : { periods };
}
