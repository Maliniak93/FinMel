import { TRANSLATIONS } from '../../../testing/i18n';
import { ASSET_CLASS, ASSET_CLASSES, assetClassLabel } from '../../features/assets/asset-class';
import {
  DEPOSIT_CAPITALIZATIONS,
  DEPOSIT_STATUS,
  DEPOSIT_TERM_UNITS,
  depositStatusLabel,
} from '../../features/deposits/deposit-terms';
import { RUN_KINDS, syncRunStatusLabel } from '../../features/settings/settings';
import {
  TRANSACTION_TYPES,
  quantityFieldLabel,
  transactionTypeLabel,
} from '../../features/transactions/transaction-type';
import { TRANSFER_DIRECTION, transferLabel } from '../../features/transactions/transfer-direction';
import { SUPPORTED_CURRENCIES } from '../../shared/currencies';

// i18n foundation (#131) AC-9: every enum label map returns a translation key (templates translate
// it through the `transloco` pipe), and every key it can return has a non-empty label in both
// en.json and pl.json. Asserted against the real files, so a missing key fails here instead of
// printing a raw key on screen.

function expectTranslatedKey(source: string, key: string): void {
  for (const lang of ['en', 'pl'] as const) {
    const value = TRANSLATIONS[lang][key];
    expect(typeof value, `${source}: "${key}" has no string label in ${lang}.json`).toBe('string');
    expect((value as string).trim(), `${source}: "${key}" is empty in ${lang}.json`).not.toBe('');
  }
}

describe('enum label maps', () => {
  it('every enum value has a translated label in en and pl', () => {
    const keys: [string, string][] = [];

    // Asset class — the picker list, the lookup and its unknown-value fallback.
    for (const assetClass of ASSET_CLASSES) {
      keys.push(['ASSET_CLASSES', assetClass.label]);
    }
    for (const value of Object.values(ASSET_CLASS)) {
      keys.push(['assetClassLabel', assetClassLabel(value)]);
    }
    keys.push(['assetClassLabel(unknown)', assetClassLabel(99)]);

    // Transaction type and the quantity field label that depends on it.
    for (const type of TRANSACTION_TYPES) {
      keys.push(['TRANSACTION_TYPES', type.label]);
      keys.push(['transactionTypeLabel', transactionTypeLabel(type.value)]);
      keys.push(['quantityFieldLabel', quantityFieldLabel(type.value)]);
    }
    keys.push(['transactionTypeLabel(unknown)', transactionTypeLabel(99)]);

    // Deposit term unit, capitalization and status.
    for (const unit of DEPOSIT_TERM_UNITS) {
      keys.push(['DEPOSIT_TERM_UNITS', unit.label]);
    }
    for (const capitalization of DEPOSIT_CAPITALIZATIONS) {
      keys.push(['DEPOSIT_CAPITALIZATIONS', capitalization.label]);
    }
    for (const status of Object.values(DEPOSIT_STATUS)) {
      keys.push(['depositStatusLabel', depositStatusLabel(status)]);
    }

    // Sync run status (C# SyncRunStatus: Running, Completed, Partial, Failed) and run kind.
    for (const status of [0, 1, 2, 3, 99, null, undefined]) {
      keys.push(['syncRunStatusLabel', syncRunStatusLabel(status)]);
    }
    for (const kind of RUN_KINDS) {
      keys.push(['RUN_KINDS', kind.label]);
    }

    // Currency picker labels.
    for (const currency of SUPPORTED_CURRENCIES) {
      keys.push(['SUPPORTED_CURRENCIES', currency.label]);
    }

    for (const [source, key] of keys) {
      expectTranslatedKey(source, key);
    }

    // The maps hand out keys, not English text: no two different enum values share one label.
    const assetClassKeys = ASSET_CLASSES.map((c) => c.label);
    expect(new Set(assetClassKeys).size).toBe(assetClassKeys.length);
    const typeKeys = TRANSACTION_TYPES.map((t) => t.label);
    expect(new Set(typeKeys).size).toBe(typeKeys.length);
  });

  it('a transfer leg label is a translated key carrying the counterpart as parameters', () => {
    const base = {
      counterpartAssetId: '11111111-1111-1111-1111-111111111111',
      counterpartAssetName: 'Term deposit',
      counterpartPortfolioId: '22222222-2222-2222-2222-222222222222',
      counterpartPortfolioName: 'Savings',
    };

    const out = transferLabel({ ...base, direction: TRANSFER_DIRECTION.Out });
    const into = transferLabel({ ...base, direction: TRANSFER_DIRECTION.In });

    expectTranslatedKey('transferLabel(Out)', out.key);
    expectTranslatedKey('transferLabel(In)', into.key);
    expect(out.key).not.toBe(into.key);
    for (const label of [out, into]) {
      expect(Object.values(label.params)).toEqual(
        expect.arrayContaining(['Term deposit', 'Savings']),
      );
    }
  });
});
