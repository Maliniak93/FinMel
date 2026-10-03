import { ASSET_CLASS } from '../assets/asset-class';
import { allowedTransactionTypes, TRANSACTION_TYPES } from './transaction-type';

describe('transaction-type', () => {
  it('allowedTransactionTypes limits Cash, Deposit, Savings and Bond to Deposit/Withdraw', () => {
    const cashLike: number[] = [
      ASSET_CLASS.Cash,
      ASSET_CLASS.Deposit,
      ASSET_CLASS.Savings,
      ASSET_CLASS.Bond,
    ];

    for (const assetClass of Object.values(ASSET_CLASS)) {
      const allowed = allowedTransactionTypes(assetClass);

      if (cashLike.includes(assetClass)) {
        expect(allowed).toEqual([TRANSACTION_TYPES[2], TRANSACTION_TYPES[3]]);
        expect(allowed.map((t) => t.value)).toEqual([2, 3]);
      } else {
        expect(allowed).toEqual(TRANSACTION_TYPES);
      }
    }
  });
});
