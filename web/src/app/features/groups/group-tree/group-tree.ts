import type { AssetGroupTreeChild, AssetGroupTreeItem } from '../../../api/portfolio';

export interface TreeRow {
  key: string;
  kind: 'account' | 'child' | 'asset';
  name: string;
  assetClass: number | null;
  groupId: string | null;
  isException: boolean;
  isArchived: boolean;
  accountKey: string | null;
  expandable: boolean;
  expanded: boolean;
}

export interface TreeFilter {
  text: string;
  assetClass: number | null;
}

export interface TreeRowState {
  checked: boolean;
  greyed: boolean;
  chip: string | null;
}

export function accountRow(
  item: AssetGroupTreeItem,
  expandable: boolean,
  expanded: boolean,
): TreeRow {
  return {
    key: item.accountId!,
    kind: 'account',
    name: item.name,
    assetClass: null,
    groupId: item.groupId,
    isException: false,
    isArchived: item.isArchived,
    accountKey: null,
    expandable,
    expanded,
  };
}

export function childRow(accountKey: string, child: AssetGroupTreeChild): TreeRow {
  return {
    key: child.assetId,
    kind: 'child',
    name: child.name,
    assetClass: Number(child.assetClass),
    groupId: child.groupId,
    isException: child.isException,
    isArchived: child.isArchived,
    accountKey,
    expandable: false,
    expanded: false,
  };
}

export function assetRow(item: AssetGroupTreeItem): TreeRow {
  return {
    key: item.assetId!,
    kind: 'asset',
    name: item.name,
    assetClass: item.assetClass === null ? null : Number(item.assetClass),
    groupId: item.groupId,
    isException: false,
    isArchived: item.isArchived,
    accountKey: null,
    expandable: false,
    expanded: false,
  };
}

export function flattenVisible(
  items: readonly AssetGroupTreeItem[],
  expanded: ReadonlySet<string>,
  filter: TreeFilter,
): TreeRow[] {
  const text = filter.text.trim().toLowerCase();
  const filtered = text !== '' || filter.assetClass !== null;
  const matches = (name: string): boolean => text === '' || name.toLowerCase().includes(text);
  const rows: TreeRow[] = [];

  for (const item of items) {
    if (item.kind !== 'Account') {
      if (
        matches(item.name) &&
        (filter.assetClass === null || Number(item.assetClass) === filter.assetClass)
      ) {
        rows.push(assetRow(item));
      }
      continue;
    }

    const accountKey = item.accountId!;
    if (!filtered) {
      const open = expanded.has(accountKey);
      rows.push(accountRow(item, item.children.length > 0, open));
      if (open) {
        rows.push(...item.children.map((child) => childRow(accountKey, child)));
      }
      continue;
    }

    const accountMatches = matches(item.name);
    const shown = item.children.filter(
      (child) =>
        (filter.assetClass === null || Number(child.assetClass) === filter.assetClass) &&
        (accountMatches || matches(child.name)),
    );
    if (shown.length > 0 || (filter.assetClass === null && accountMatches)) {
      rows.push(accountRow(item, false, shown.length > 0));
      rows.push(...shown.map((child) => childRow(accountKey, child)));
    }
  }

  return rows;
}

export function page<T>(rows: readonly T[], index: number, size: number): T[] {
  return rows.slice(index * size, (index + 1) * size);
}

export function rowState(row: TreeRow, groupId: string | null): TreeRowState {
  const greyed = row.groupId !== null && row.groupId !== groupId;
  return {
    checked: row.groupId === groupId,
    greyed,
    chip: greyed ? row.groupId : null,
  };
}
