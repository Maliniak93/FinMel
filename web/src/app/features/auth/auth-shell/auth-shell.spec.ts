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
});
