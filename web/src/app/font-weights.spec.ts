import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

function scssFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      return scssFiles(path);
    }
    return entry.name.endsWith('.scss') ? [path] : [];
  });
}

describe('font weights', () => {
  const files = scssFiles(resolve(process.cwd(), 'src'));

  it('no stylesheet declares font-weight 500 or medium', () => {
    for (const file of files) {
      expect(readFileSync(file, 'utf8'), file).not.toMatch(/font-weight:\s*(500|medium)\b/);
    }
  });

  it('--sk-font-display is referenced nowhere', () => {
    for (const file of files) {
      expect(readFileSync(file, 'utf8'), file).not.toContain('--sk-font-display');
    }
  });
});
