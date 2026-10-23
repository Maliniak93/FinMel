import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { stubMatchMedia, type MatchMediaStub } from '../../../testing/match-media';
import { THEME_STORAGE_KEY, ThemeService } from './theme';

function inlineThemeScript(): string {
  const html = readFileSync(resolve(process.cwd(), 'src/index.html'), 'utf8');
  const match = /<script>([\s\S]*?)<\/script>/.exec(html);
  if (!match) {
    throw new Error('index.html has no inline script');
  }
  return match[1];
}

function reset(): void {
  localStorage.clear();
  document.documentElement.removeAttribute('data-theme');
  document.documentElement.style.removeProperty('color-scheme');
}

describe('index.html theme script', () => {
  let media: MatchMediaStub | undefined;

  afterEach(() => {
    media?.restore();
    reset();
  });

  it('pre-paint script resolves like the service', () => {
    const cases: { stored: string | null; dark: boolean }[] = [
      { stored: null, dark: false },
      { stored: null, dark: true },
      { stored: 'system', dark: true },
      { stored: 'light', dark: true },
      { stored: 'dark', dark: false },
      { stored: 'purple', dark: true },
    ];

    for (const { stored, dark } of cases) {
      reset();
      media?.restore();
      media = stubMatchMedia(dark);
      if (stored !== null) {
        localStorage.setItem(THEME_STORAGE_KEY, stored);
      }

      new Function(inlineThemeScript())();
      const fromScript = document.documentElement.getAttribute('data-theme');
      const scriptColorScheme = document.documentElement.style.getPropertyValue('color-scheme');

      reset();
      if (stored !== null) {
        localStorage.setItem(THEME_STORAGE_KEY, stored);
      }
      const service = new ThemeService();
      const fromService = document.documentElement.getAttribute('data-theme');

      expect(fromScript, `${stored} / dark=${dark}`).toBe(fromService);
      expect(fromScript).not.toBeNull();
      expect(scriptColorScheme).toBe('');
      expect(service.preference()).toBeDefined();
    }
  });
});
