import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';

import { MatIconRegistry } from '@angular/material/icon';
import { TestBed } from '@angular/core/testing';

import { appConfig } from './app.config';
import { AuthService } from './core/auth/auth';
import { provideI18nTesting } from './core/i18n/testing';

const APP_DIR = resolve(process.cwd(), 'src/app');
const SNAKE = /^[a-z][a-z0-9_]*$/;

function sourceFiles(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) {
      if (entry !== 'api') {
        sourceFiles(path, out);
      }
    } else if (/\.(html|ts)$/.test(entry) && !entry.endsWith('.spec.ts')) {
      out.push(path);
    }
  }
  return out;
}

function ligaturesIn(file: string, text: string): string[] {
  const names: string[] = [];
  if (file.endsWith('.html') || /<mat-icon/.test(text)) {
    for (const match of text.matchAll(/<mat-icon\b[^>]*>([\s\S]*?)<\/mat-icon>/g)) {
      const body = match[1].trim();
      if (SNAKE.test(body)) {
        names.push(body);
      }
      for (const quoted of body.matchAll(/'([a-z][a-z0-9_]*)'/g)) {
        names.push(quoted[1]);
      }
    }
  }
  if (file.endsWith('.ts')) {
    for (const match of text.matchAll(/\bicon\w*\s*[:=]\s*'([a-z][a-z0-9_]*)'/gi)) {
      names.push(match[1]);
    }
    const iconMap = /ICONS[^=]*=\s*\{([\s\S]*?)\};/g;
    for (const map of text.matchAll(iconMap)) {
      for (const value of map[1].matchAll(/:\s*'([a-z][a-z0-9_]*)'/g)) {
        names.push(value[1]);
      }
    }
  }
  return names;
}

function materialSymbolNames(): Set<string> {
  const declaration = readFileSync(
    resolve(process.cwd(), 'node_modules/material-symbols/index.d.ts'),
    'utf8',
  );
  return new Set([...declaration.matchAll(/"([a-z0-9_]+)"/g)].map((match) => match[1]));
}

describe('icons', () => {
  it('every ligature name exists', () => {
    const known = materialSymbolNames();
    const used = new Map<string, string>();
    for (const file of sourceFiles(APP_DIR)) {
      for (const name of ligaturesIn(file, readFileSync(file, 'utf8'))) {
        used.set(name, file);
      }
    }

    expect(used.size).toBeGreaterThan(20);
    const missing = [...used]
      .filter(([name]) => !known.has(name))
      .map(([name, file]) => `${name} (${file})`);
    expect(missing).toEqual([]);
  });

  it('default font set class', () => {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideI18nTesting(),
        { provide: AuthService, useValue: { refreshOnce: () => Promise.resolve() } },
      ],
    });

    const registry = TestBed.inject(MatIconRegistry);

    expect(registry.getDefaultFontSetClass()).toEqual(['material-symbols-outlined']);
  });
});
