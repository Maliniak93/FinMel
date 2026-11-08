import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { readStylesheet } from '../../../testing/stylesheet';

const GROUPS_DIR = join(process.cwd(), 'src/app/features/groups');

function scssFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      return scssFiles(path);
    }
    return entry.name.endsWith('.scss') ? [path] : [];
  });
}

describe('Grupy stylesheets', () => {
  const groups = readStylesheet('src/app/features/groups/groups.scss');
  const card = readStylesheet('src/app/features/groups/group-card/group-card.scss');
  const dialog = readStylesheet('src/app/features/groups/group-form-dialog/group-form-dialog.scss');

  it('lays the cards in two columns from 720 px with the sk-space-6 gap', () => {
    expect(groups).toMatch(
      /@media\s*\((width\s*>=\s*720px|min-width:\s*720px)\)\s*\{\s*\.groups-page__list\s*\{[^}]*repeat\(2,/,
    );
    expect(groups).toMatch(/\.groups-page__list\s*\{[^}]*gap:\s*var\(--sk-space-6\)/);
  });

  it('draws the group card edge as a 3 px top border', () => {
    expect(card).not.toMatch(/group-card__bar/);
    expect(card).toMatch(/\.group-card\s*\{[^}]*border-top:\s*3px solid/);
  });

  it('rules the allocation block and pads the card from the prototype', () => {
    expect(card).toMatch(/border-top:\s*1px solid var\(--mat-sys-outline-variant\)/);
    expect(card).toMatch(/padding-top:\s*var\(--sk-space-2\)/);
    expect(card).toMatch(/padding:\s*var\(--sk-space-4\) var\(--sk-space-4\) var\(--sk-space-3\)/);
  });

  it('draws the ungrouped row dashed with md corners', () => {
    expect(groups).toMatch(
      /\.groups-page__ungrouped\s*\{[^}]*border:\s*1px dashed var\(--mat-sys-outline\)/,
    );
    expect(groups).toMatch(
      /\.groups-page__ungrouped\s*\{[^}]*border-radius:\s*var\(--sk-radius-md\)/,
    );
  });

  it('draws the dialog allocation fieldset with a top rule, a legend on the lg text size and a 10 px live bar', () => {
    expect(dialog).toMatch(
      /\.group-form-dialog__allocation\s*\{[^}]*border-top:\s*1px solid var\(--mat-sys-outline-variant\)/,
    );
    expect(dialog).toMatch(
      /\.group-form-dialog__allocation\s*\{[^}]*padding-top:\s*var\(--sk-space-3\)/,
    );
    expect(dialog).toMatch(/legend\s*\{[^}]*font-size:\s*var\(--sk-text-lg\)/);
    expect(dialog).toMatch(/\.group-form-dialog__live-bar\s*\{[^}]*height:\s*10px/);
  });

  it('uses outline tokens, spacing tokens and no colour literal', () => {
    for (const file of scssFiles(GROUPS_DIR)) {
      const css = readFileSync(file, 'utf8');
      expect(css, file).not.toMatch(/#[0-9a-fA-F]{3,6}\b|rgb\(/);
      for (const [declaration] of css.matchAll(
        /border(?:-[a-z]+)?:\s*[^;]*(solid|dashed)[^;]*;/g,
      )) {
        if (/transparent|none/.test(declaration)) {
          continue;
        }
        expect(declaration, file).toMatch(/var\(--mat-sys-outline/);
      }
    }
    expect(card).toMatch(/var\(--sk-space-/);
    expect(dialog).toMatch(/var\(--sk-space-/);
  });
});
