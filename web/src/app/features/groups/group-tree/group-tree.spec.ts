import {
  blueGroupId,
  greenGroupId,
  skarbonki,
  treeAccount,
  treeAccountId,
  treeAccountWithThreeChildren,
  treeAsset,
  treeChild,
} from '../testing/group-fixtures';
import { flattenVisible, page, rowState } from './group-tree';

const noFilter = { text: '', assetClass: null };
const names = (rows: { name: string }[]): string[] => rows.map((row) => row.name);

describe('GroupTree', () => {
  it('pages expanded rows', () => {
    const items = [treeAccountWithThreeChildren(), ...skarbonki(20)];

    const collapsed = flattenVisible(items, new Set<string>(), noFilter);
    expect(collapsed).toHaveLength(21);
    expect(page(collapsed, 0, 20)).toHaveLength(20);
    expect(page(collapsed, 1, 20)).toHaveLength(1);

    const expanded = flattenVisible(items, new Set([treeAccountId]), noFilter);
    expect(expanded).toHaveLength(24);
    expect(names(expanded).slice(0, 4)).toEqual(['IKE Obligacje', 'OTS0126', 'ROR0126', 'COI0126']);
    expect(page(expanded, 0, 20)).toHaveLength(20);
    expect(page(expanded, 1, 20)).toHaveLength(4);
  });

  it('text filter auto-expands', () => {
    const items = [
      treeAccountWithThreeChildren(),
      treeAccount('acc-2', 'IKE eMakler', [treeChild('c-9', 'VWCE.DE')]),
      ...skarbonki(2),
    ];

    const rows = flattenVisible(items, new Set<string>(), { text: 'ror0126', assetClass: null });

    expect(names(rows)).toEqual(['IKE Obligacje', 'ROR0126']);
  });

  it('text filter shows a matching flat item', () => {
    const items = [treeAccountWithThreeChildren(), ...skarbonki(12)];

    const rows = flattenVisible(items, new Set<string>(), {
      text: 'skarbonka 12',
      assetClass: null,
    });

    expect(names(rows)).toEqual(['Skarbonka 12']);
  });

  it('class filter', () => {
    const items = [
      treeAccount('acc-1', 'Broker', [
        treeChild('c-1', 'VWCE.DE', { assetClass: 3 }),
        treeChild('c-2', 'CDR.WA', { assetClass: 2 }),
        treeChild('c-3', 'PKO.WA', { assetClass: 2 }),
      ]),
      treeAccountWithThreeChildren(),
      treeAsset('a-1', 'Gold jar', { assetClass: 0 }),
      treeAsset('a-2', 'Direct stock', { assetClass: 2 }),
    ];

    const rows = flattenVisible(items, new Set<string>(), { text: '', assetClass: 2 });

    expect(names(rows)).toEqual(['Broker', 'CDR.WA', 'PKO.WA', 'Direct stock']);
  });

  it('row state', () => {
    const inBlue = treeAccountWithThreeChildren(blueGroupId, blueGroupId);
    const inGreenChild = treeAccount('acc-2', 'Other', [
      treeChild('c-1', 'In green', { groupId: greenGroupId }),
      treeChild('c-2', 'Free'),
    ]);
    const greenAccount = treeAccount('acc-3', 'Green account', [], { groupId: greenGroupId });
    const rows = flattenVisible(
      [inBlue, inGreenChild, greenAccount],
      new Set([treeAccountId, 'acc-2']),
      noFilter,
    );
    const stateOf = (name: string) =>
      rowState(
        rows.find((row) => row.name === name)!,
        blueGroupId,
      );

    expect(stateOf('IKE Obligacje')).toEqual({ checked: true, greyed: false, chip: null });
    expect(stateOf('OTS0126')).toEqual({ checked: true, greyed: false, chip: null });
    expect(stateOf('In green')).toEqual({ checked: false, greyed: true, chip: greenGroupId });
    expect(stateOf('Free')).toEqual({ checked: false, greyed: false, chip: null });
    expect(stateOf('Green account')).toEqual({ checked: false, greyed: true, chip: greenGroupId });
  });
});
