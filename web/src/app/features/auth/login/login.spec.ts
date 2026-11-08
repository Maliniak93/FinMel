import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import {
  elementWithText,
  isTranslationIn,
  looksLikeTranslationKey,
  messageOf,
  pickLanguageFromMenu,
  textOf,
} from '../../../../testing/i18n';
import { AuthService } from '../../../core/auth/auth';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../../core/i18n/language';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { Login } from './login';

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let component: Login;
  let authService: { login: ReturnType<typeof vi.fn> };
  let router: Router;

  beforeEach(async () => {
    authService = { login: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        provideI18nTesting(),
        { provide: AuthService, useValue: authService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Login);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    await fixture.whenStable();
  });

  afterEach(async () => {
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    document.documentElement.lang = 'en';
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders the centred layout', () => {
    const element = fixture.nativeElement as HTMLElement;
    expect(elementWithText(element, 'Skarbiec')).toBeDefined();
    expect(textOf(element.querySelector('h1'))).toBe('Log in');
    expect(element.querySelector('mat-label')).toBeNull();

    const email = element.querySelector<HTMLInputElement>('input[type="email"]')!;
    const password = element.querySelector<HTMLInputElement>('input[type="password"]')!;
    expect(Array.from(email.labels ?? [], textOf)).toEqual(['Email']);
    expect(Array.from(password.labels ?? [], textOf)).toEqual(['Password']);
    expect(
      email.labels![0].compareDocumentPosition(email) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0);

    const submit = element.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(textOf(submit)).toBe('Log in');
    expect(password.compareDocumentPosition(submit) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);

    const registerLink = element.querySelector('a[href="/register"]')!;
    expect(textOf(registerLink)).toBe('Register');
    expect(
      submit.compareDocumentPosition(registerLink) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0);
  });

  it('does not submit an invalid form', async () => {
    await component['onSubmit']();
    expect(authService.login).not.toHaveBeenCalled();
  });

  it('logs in with the form values and navigates to the dashboard on success', async () => {
    authService.login.mockResolvedValue({ success: true });
    component['form'].setValue({ email: 'a@b.com', password: 'secret' });

    await component['onSubmit']();

    expect(authService.login).toHaveBeenCalledWith({ email: 'a@b.com', password: 'secret' });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('shows one error line with an icon when sign-in fails', async () => {
    authService.login.mockResolvedValue({
      success: false,
      problem: { detail: 'Invalid email or password.' },
    });
    component['form'].setValue({ email: 'a@b.com', password: 'wrong' });

    await component['onSubmit']();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const alert = element.querySelector('[role="alert"]')!;
    const submit = element.querySelector('button[type="submit"]')!;
    expect(messageOf(alert)).toBe('Invalid email or password.');
    expect(alert.querySelector('mat-icon')).not.toBeNull();
    expect(element.querySelectorAll('[role="alert"]')).toHaveLength(1);
    expect(alert.compareDocumentPosition(submit) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('styles the error line with --sk-negative and a 14 px icon', () => {
    const scss = readFileSync(
      resolve(process.cwd(), 'src/app/features/auth/_auth-page.scss'),
      'utf8',
    );

    expect(scss).toMatch(/\.auth-page__error\s*\{[^}]*color:\s*var\(--sk-negative\)/);
    expect(scss).toMatch(/\.auth-page__error\s*\{[^}]*font-size:\s*var\(--sk-text-sm\)/);
    expect(scss).toMatch(/\.auth-page__error mat-icon\s*\{[^}]*font-size:\s*var\(--sk-text-base\)/);
  });

  it('colours the field labels with on-surface-variant', () => {
    const scss = readFileSync(
      resolve(process.cwd(), 'src/app/features/auth/_auth-page.scss'),
      'utf8',
    );

    expect(scss).toMatch(
      /\.auth-page__label\s*\{[^}]*color:\s*var\(--mat-sys-on-surface-variant\)/,
    );
  });

  it('disables the button while submitting', async () => {
    let finish!: (result: unknown) => void;
    authService.login.mockReturnValue(new Promise((resolve) => (finish = resolve)));
    component['form'].setValue({ email: 'a@b.com', password: 'secret' });

    const submitting = component['onSubmit']();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const submit = element.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(submit.disabled).toBe(true);
    expect(submit.getAttribute('aria-busy')).toBe('true');
    expect(element.querySelector('[role="progressbar"]')).toBeNull();

    finish({ success: true });
    await submitting;
  });

  it('switches to Polish from the login page', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const title = () => textOf(element.querySelector('h1'));
    const labels = () => Array.from(element.querySelectorAll('label'), textOf);
    const submit = () => textOf(element.querySelector('button[type="submit"]'));
    const registerLink = () => textOf(element.querySelector('a[href="/register"]'));
    const languageCode = () => textOf(element.querySelector('app-language-switch button'));

    expect(title()).toBe('Log in');
    expect(labels()).toEqual(['Email', 'Password']);
    expect(submit()).toBe('Log in');
    expect(registerLink()).toBe('Register');
    expect(languageCode()).toBe('EN');

    await pickLanguageFromMenu(fixture, 'Polski');

    for (const [english, polish] of [
      ['Log in', title()],
      ['Password', labels()[1]],
      ['Log in', submit()],
      ['Register', registerLink()],
    ]) {
      expect(polish).not.toBe(english);
      expect(looksLikeTranslationKey(polish)).toBe(false);
      expect(isTranslationIn('pl', polish), `"${polish}" is not a pl.json value`).toBe(true);
    }
    expect(isTranslationIn('pl', labels()[0])).toBe(true);
    expect(languageCode()).toBe('PL');
    expect(document.documentElement.lang).toBe('pl');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('pl');
  });

  it('shows its client-side validation messages in Polish', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');
    await component['onSubmit']();
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    const errors = Array.from(element.querySelectorAll('mat-error'));
    expect(errors).toHaveLength(2);
    for (const error of errors) {
      const message = messageOf(error);
      expect(['Email is required.', 'Password is required.']).not.toContain(message);
      expect(isTranslationIn('pl', message), `"${message}" is not a pl.json value`).toBe(true);
      expect(error.querySelector('mat-icon')).not.toBeNull();
    }

    const email = element.querySelector('input[type="email"]')!;
    const password = element.querySelector('input[type="password"]')!;
    expect(email.compareDocumentPosition(errors[0]) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
    expect(errors[0].compareDocumentPosition(password) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(
      0,
    );
    expect(password.compareDocumentPosition(errors[1]) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(
      0,
    );
  });
});
