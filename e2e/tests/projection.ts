import { expect } from '@playwright/test';

interface ProjectedOptions {
  timeout?: number;
  intervals?: number[];
}

export async function expectProjected(
  refetch: () => Promise<unknown>,
  assertion: () => Promise<unknown>,
  options: ProjectedOptions = {},
): Promise<void> {
  const { timeout = 30_000, intervals = [1_000, 2_000, 3_000, 5_000] } = options;
  await expect(async () => {
    await refetch();
    await assertion();
  }).toPass({ timeout, intervals });
}
