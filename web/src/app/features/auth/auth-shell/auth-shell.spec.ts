import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { textOf } from '../../../../testing/i18n';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { AuthShell } from './auth-shell';

@Component({
  imports: [AuthShell],
  template: `
    <app-auth-shell title="Log in">
      <p>form content</p>
    </app-auth-shell>
  `,
})
class AuthShellHost {}

describe('AuthShell', () => {
  let fixture: ComponentFixture<AuthShellHost>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AuthShellHost],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(AuthShellHost);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('language switch top right', () => {
    const element = fixture.nativeElement as HTMLElement;
    const language = element.querySelector('app-language-switch')!;
    const title = element.querySelector('h1')!;

    expect(textOf(language.querySelector('button'))).toBe('EN');
    expect(language.querySelector('mat-icon')).toBeNull();
    expect(language.compareDocumentPosition(title) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
  });

  it('sets the login title to 26 px, weight 600, left-aligned', () => {
    const scss = readFileSync(
      resolve(process.cwd(), 'src/app/features/auth/auth-shell/auth-shell.scss'),
      'utf8',
    );

    expect(scss).toMatch(/\.auth-shell__title\s*\{[^}]*font-size:\s*var\(--sk-text-2xl\)/);
    expect(scss).toMatch(/\.auth-shell__title\s*\{[^}]*font-weight:\s*600/);
    expect(scss).toMatch(/\.auth-shell__title\s*\{[^}]*text-align:\s*left/);
  });

  it('places the language switch 24 px from the top and 32 px from the right', () => {
    const scss = readFileSync(
      resolve(process.cwd(), 'src/app/features/auth/auth-shell/auth-shell.scss'),
      'utf8',
    );

    expect(scss).toMatch(/\.auth-shell__language\s*\{[^}]*top:\s*24px/);
    expect(scss).toMatch(/\.auth-shell__language\s*\{[^}]*right:\s*32px/);
  });
});
