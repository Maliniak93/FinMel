import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { readStylesheet, rulesFor } from '../../../testing/stylesheet';

describe('Shell styles', () => {
  const shell = readFileSync(resolve(process.cwd(), 'src/app/layout/shell/shell.scss'), 'utf8');
  const globals = readFileSync(resolve(process.cwd(), 'src/styles.scss'), 'utf8');
  const dashboard = readStylesheet('src/app/features/dashboard/dashboard.scss');

  it('draws the sidenav right border and the active link radius and margin', () => {
    expect(`${globals}\n${shell}`).toMatch(
      /border-right:\s*1px solid var\(--mat-sys-outline-variant\)/,
    );
    expect(rulesFor(dashboard, /dashboard-page__hero/)).toMatch(
      /border-bottom:\s*1px solid var\(--mat-sys-outline-variant\)/,
    );
    expect(shell).toMatch(/border-radius:\s*0 var\(--sk-radius-md\) var\(--sk-radius-md\) 0/);
    expect(shell).toMatch(/margin-right:\s*var\(--sk-space-3\)/);
  });

  it('keeps the sidebar a single column of the window height that does not scroll', () => {
    expect(rulesFor(shell, /\.shell__nav-body\s*$/)).toMatch(/box-sizing:\s*border-box/);
    expect(rulesFor(shell, /\.shell__nav\s*$/)).toMatch(/overflow:\s*hidden/);
    expect(rulesFor(shell, /mat-drawer-inner-container/)).toMatch(/overflow:\s*hidden/);
  });

  it('makes the content the only vertical scroller of a long page', () => {
    const content = rulesFor(shell, /\.shell__content\s*$/);
    expect(content).toMatch(/min-height:\s*0/);
    expect(content).toMatch(/min-width:\s*0/);
    expect(content).toMatch(/overflow-y:\s*auto/);
    expect(content).toMatch(/overflow-x:\s*hidden/);
    expect(rulesFor(shell, /\.shell__content-area\s*$/)).toMatch(/overflow:\s*hidden/);
  });

  it('pins Ustawienia and the icon row while only the nav list scrolls', () => {
    expect(rulesFor(shell, /\.shell__nav-list\s*$/)).toMatch(/min-height:\s*0/);
    expect(rulesFor(shell, /\.shell__nav-list\s*$/)).toMatch(/overflow-y:\s*auto/);
    expect(rulesFor(shell, /\.shell__nav-pinned\s*$/)).toMatch(/flex:\s*0 0 auto/);
    expect(shell).toMatch(/\.shell__nav-footer\s*\{[^}]*flex:\s*0 0 auto/);
  });

  it('gives a hovered sidenav item the selected width, a weaker tint and no left accent', () => {
    const hover = rulesFor(shell, /\.shell__nav-link:hover\s*$/);
    expect(hover).toMatch(/margin-right:\s*var\(--sk-space-3\)/);
    expect(hover).toMatch(/border-radius:\s*0 var\(--sk-radius-md\) var\(--sk-radius-md\) 0/);
    expect(hover).toMatch(/color-mix\(in srgb,\s*var\(--sk-accent-tint\)\s*50%,\s*transparent\)/);
    expect(hover).not.toMatch(/border-left/);
  });

  it('keeps the selected sidenav item tinted with its accent while it is hovered', () => {
    const activeHover = rulesFor(shell, /\.shell__nav-link--active:hover\s*$/);
    expect(activeHover).toMatch(/background:\s*var\(--sk-accent-tint\)/);
    expect(activeHover).toMatch(/border-left-color:\s*var\(--sk-accent-stroke\)/);
    expect(activeHover).toMatch(/color:\s*var\(--mat-sys-primary\)/);
  });

  it('sets the sidebar brand, links and icons to the prototype sizes', () => {
    expect(shell).toMatch(/\.shell__brand\s*\{[^}]*font-weight:\s*600/);
    expect(shell).toMatch(/\.shell__nav-link\s*\{[^}]*font-size:\s*var\(--sk-text-lg\)/);
    expect(shell).toMatch(/\.shell__nav mat-icon\s*\{[^}]*font-size:\s*var\(--sk-text-lg\)/);
  });

  it('sizes the sidebar language button to 13 px, .06em and 36 px', () => {
    expect(shell).toMatch(
      /\.shell__nav-footer\s*\{[\s\S]*?app-language-switch button[^{]*\{[^}]*height:\s*36px[^}]*font-size:\s*var\(--sk-text-sm\)[^}]*letter-spacing:\s*0\.06em/,
    );
  });

  it('sets the bottom nav to 68 px with xs labels, xl icons and a 16 px badge', () => {
    expect(shell).toMatch(/(^|[\s{;])height:\s*68px/m);
    expect(shell).toMatch(
      /\.shell__bottom-nav[\s\S]*?\.shell__nav-label\s*\{[^}]*font-size:\s*var\(--sk-text-xs\)/,
    );
    expect(shell).toMatch(/font-size:\s*var\(--sk-text-xl\)/);
    expect(shell).toMatch(/(^|[\s{;])height:\s*16px/m);
  });
});
