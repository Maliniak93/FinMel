// Indexed by AssetClass, so a class keeps its colour whichever classes are present.
const ASSET_CLASS_COLORS: readonly string[] = [
  '#4C6EF5',
  '#22B8CF',
  '#12B886',
  '#82C91E',
  '#FAB005',
  '#FA5252',
  '#F76707',
  '#7048E8',
  '#868E96',
  '#E64980',
];

export function assetClassColor(assetClass: number): string {
  return ASSET_CLASS_COLORS[Number(assetClass) % ASSET_CLASS_COLORS.length];
}
