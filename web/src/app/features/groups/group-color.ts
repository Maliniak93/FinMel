// Palette order mirrors Skarbiec.Contracts.AssetGroupColors.Keys.
export const GROUP_COLORS: readonly { key: string; css: string }[] = [
  { key: 'blue', css: '#4C6EF5' },
  { key: 'green', css: '#12B886' },
  { key: 'orange', css: '#F76707' },
  { key: 'purple', css: '#7048E8' },
  { key: 'red', css: '#FA5252' },
  { key: 'teal', css: '#0CA678' },
  { key: 'pink', css: '#E64980' },
  { key: 'amber', css: '#FAB005' },
  { key: 'indigo', css: '#3B5BDB' },
  { key: 'lime', css: '#82C91E' },
  { key: 'cyan', css: '#15AABF' },
  { key: 'brown', css: '#8D6E63' },
];

export const UNGROUPED_COLOR = '#868E96';

export function groupColor(key: string | null | undefined): string {
  return GROUP_COLORS.find((color) => color.key === key)?.css ?? UNGROUPED_COLOR;
}
