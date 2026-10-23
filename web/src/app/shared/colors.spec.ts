import { ASSET_CLASS } from '../features/assets/asset-class';
import { GROUP_COLOR_KEYS, assetClassColor, groupColor } from './colors';

describe('colour dictionary', () => {
  it('maps every group key', () => {
    expect(GROUP_COLOR_KEYS).toEqual([
      'blue',
      'green',
      'orange',
      'purple',
      'red',
      'teal',
      'pink',
      'amber',
      'indigo',
      'lime',
      'cyan',
      'brown',
    ]);
    for (const key of GROUP_COLOR_KEYS) {
      expect(groupColor(key)).toBe(`var(--sk-g-${key})`);
    }
  });

  it('falls back to the neutral token', () => {
    expect(groupColor(null)).toBe('var(--sk-g-none)');
    expect(groupColor(undefined)).toBe('var(--sk-g-none)');
    expect(groupColor('chartreuse')).toBe('var(--sk-g-none)');
  });

  it('maps every asset class', () => {
    expect(assetClassColor(ASSET_CLASS.Cash)).toBe('var(--sk-c-cash)');
    expect(assetClassColor(ASSET_CLASS.Deposit)).toBe('var(--sk-c-deposit)');
    expect(assetClassColor(ASSET_CLASS.Stock)).toBe('var(--sk-c-stock)');
    expect(assetClassColor(ASSET_CLASS.Etf)).toBe('var(--sk-c-etf)');
    expect(assetClassColor(ASSET_CLASS.Bond)).toBe('var(--sk-c-bond)');
    expect(assetClassColor(ASSET_CLASS.PreciousMetal)).toBe('var(--sk-c-metal)');
    expect(assetClassColor(ASSET_CLASS.BankAccount)).toBe('var(--sk-c-bank)');
  });

  it('unknown class is neutral', () => {
    expect(assetClassColor(5)).toBe('var(--sk-g-none)');
    expect(assetClassColor(99)).toBe('var(--sk-g-none)');
  });
});
