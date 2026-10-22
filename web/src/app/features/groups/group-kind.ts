// Mirrors Portfolio's AssetGroupKind: the enum travels as its int.
export const GROUP_KIND = { Regular: 0, Cushion: 1 } as const;

export const GROUP_KINDS: readonly { value: number; label: string }[] = [
  { value: GROUP_KIND.Regular, label: 'groups.kind.regular' },
  { value: GROUP_KIND.Cushion, label: 'groups.kind.cushion' },
];

export function groupKindLabel(value: number | string): string {
  return GROUP_KINDS.find((kind) => kind.value === Number(value))?.label ?? 'groups.kind.regular';
}
