import { ASSET_CLASS, ASSET_CLASSES } from './asset-class';
import {
  canAddCustomInstrument,
  defaultValuationMode,
  VALUATION_MODE,
} from './asset-valuation-mode';

// Pins the frontend mirror of Skarbiec.Contracts.AssetValuationModes.Default (M1.4) one class at a
// time, by name — the tripwire that forces asset-valuation-mode.ts to be updated if the backend
// mapping ever changes (see the comment on defaultValuationMode for why this can't be a compile-time
// link).
describe('defaultValuationMode', () => {
  it('defaults Cash and Deposit to currency-valued', () => {
    expect(defaultValuationMode(ASSET_CLASS.Cash)).toBe(VALUATION_MODE.CurrencyValued);
    expect(defaultValuationMode(ASSET_CLASS.Deposit)).toBe(VALUATION_MODE.CurrencyValued);
  });

  it('defaults Stock, Etf, Bond, Crypto and PreciousMetal to market', () => {
    expect(defaultValuationMode(ASSET_CLASS.Stock)).toBe(VALUATION_MODE.Market);
    expect(defaultValuationMode(ASSET_CLASS.Etf)).toBe(VALUATION_MODE.Market);
    expect(defaultValuationMode(ASSET_CLASS.Bond)).toBe(VALUATION_MODE.Market);
    expect(defaultValuationMode(ASSET_CLASS.Crypto)).toBe(VALUATION_MODE.Market);
    expect(defaultValuationMode(ASSET_CLASS.PreciousMetal)).toBe(VALUATION_MODE.Market);
  });

  it('defaults RealEstate and Other to manual', () => {
    expect(defaultValuationMode(ASSET_CLASS.RealEstate)).toBe(VALUATION_MODE.Manual);
    expect(defaultValuationMode(ASSET_CLASS.Other)).toBe(VALUATION_MODE.Manual);
  });

  it('covers every AssetClass the UI offers, with no gaps', () => {
    for (const { value } of ASSET_CLASSES) {
      expect(() => defaultValuationMode(value)).not.toThrow();
    }
  });
});

describe('canAddCustomInstrument', () => {
  it('is true for every market-mode class except PreciousMetal', () => {
    expect(canAddCustomInstrument(ASSET_CLASS.Stock)).toBe(true);
    expect(canAddCustomInstrument(ASSET_CLASS.Etf)).toBe(true);
    expect(canAddCustomInstrument(ASSET_CLASS.Bond)).toBe(true);
    expect(canAddCustomInstrument(ASSET_CLASS.Crypto)).toBe(true);
  });

  it('is false for PreciousMetal — NBP has no arbitrary-ticker lookup (ADR-018/M1.6)', () => {
    expect(canAddCustomInstrument(ASSET_CLASS.PreciousMetal)).toBe(false);
  });

  it('is false for every non-market class', () => {
    expect(canAddCustomInstrument(ASSET_CLASS.Cash)).toBe(false);
    expect(canAddCustomInstrument(ASSET_CLASS.Deposit)).toBe(false);
    expect(canAddCustomInstrument(ASSET_CLASS.RealEstate)).toBe(false);
    expect(canAddCustomInstrument(ASSET_CLASS.Other)).toBe(false);
  });
});
