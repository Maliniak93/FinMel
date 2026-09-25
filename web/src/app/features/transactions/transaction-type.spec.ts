import { ASSET_CLASS } from '../assets/asset-class';
import { allowedTransactionTypes, TRANSACTION_TYPES } from './transaction-type';

// cash-transaction-types AC-6: the UI mirror of the backend's AssetTransactionTypes rule — a
// cash-like class (Cash, Deposit) takes only Deposit/Withdraw, every other class all six types.
describe('transaction-type', () => {
  it('allowedTransactionTypes limits Cash and Deposit to Deposit/Withdraw', () => {
    const cashLike: number[] = [ASSET_CLASS.Cash, ASSET_CLASS.Deposit];

    for (const assetClass of Object.values(ASSET_CLASS)) {
      const allowed = allowedTransactionTypes(assetClass);

      if (cashLike.includes(assetClass)) {
        expect(allowed).toEqual([
          { value: 2, label: 'Deposit' },
          { value: 3, label: 'Withdraw' },
        ]);
      } else {
        expect(allowed).toEqual(TRANSACTION_TYPES);
      }
    }
  });
});
