import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

describe('Shell styles', () => {
  const shell = readFileSync(resolve(process.cwd(), 'src/app/layout/shell/shell.scss'), 'utf8');
  const globals = readFileSync(resolve(process.cwd(), 'src/styles.scss'), 'utf8');

  it('draws the sidenav right border and the active link radius and margin', () => {
    expect(`${globals}\n${shell}`).toMatch(
      /border-right:\s*1px solid var\(--mat-sys-outline-variant\)/,
    );
    expect(shell).toMatch(/border-radius:\s*0 var\(--sk-radius-md\) var\(--sk-radius-md\) 0/);
    expect(shell).toMatch(/margin-right:\s*var\(--sk-space-3\)/);
  });

  it('sets the sidebar brand, links and icons to the prototype sizes', () => {
    expect(shell).toMatch(/\.shell__brand\s*\{[^}]*font-weight:\s*600/);
    expect(shell).toMatch(/\.shell__nav-link\s*\{[^}]*font-size:\s*15px/);
    expect(shell).toMatch(/font-size:\s*18px/);
  });

  it('sizes the sidebar language button to 13 px, .06em and 36 px', () => {
    expect(shell).toMatch(
      /\.shell__nav-footer\s*\{[\s\S]*?app-language-switch button[^{]*\{[^}]*height:\s*36px[^}]*font-size:\s*13px[^}]*letter-spacing:\s*0\.06em/,
    );
  });

  it('sets the bottom nav to 68 px with 12 px labels, 20 px icons and a 16 px badge', () => {
    expect(shell).toMatch(/(^|[\s{;])height:\s*68px/m);
    expect(shell).toMatch(
      /\.shell__bottom-nav[\s\S]*?\.shell__nav-label\s*\{[^}]*font-size:\s*12px/,
    );
    expect(shell).toMatch(/font-size:\s*20px/);
    expect(shell).toMatch(/(^|[\s{;])height:\s*16px/m);
  });
});
