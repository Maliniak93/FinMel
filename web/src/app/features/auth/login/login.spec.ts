import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import {
  isTranslationIn,
  looksLikeTranslationKey,
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

  it('shows a form-level error for invalid credentials, without navigating', async () => {
    authService.login.mockResolvedValue({
      success: false,
      problem: { detail: 'Invalid email or password.' },
    });
    component['form'].setValue({ email: 'a@b.com', password: 'wrong' });

    await component['onSubmit']();

    expect(component['formError']()).toBe('Invalid email or password.');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('switches to Polish from the login page', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const title = () => textOf(element.querySelector('mat-card-title'));
    const labels = () => Array.from(element.querySelectorAll('mat-label'), textOf);
    const submit = () => textOf(element.querySelector('button[type="submit"]'));
    const registerLink = () => textOf(element.querySelector('a[href="/register"]'));

    expect(title()).toBe('Log in');
    expect(labels()).toEqual(['Email', 'Password']);
    expect(submit()).toBe('Log in');
    expect(registerLink()).toBe('Register');

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
    expect(document.documentElement.lang).toBe('pl');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('pl');
  });

  it('shows its client-side validation messages in Polish', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');
    await component['onSubmit']();
    await fixture.whenStable();

    const errors = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('mat-error'),
      textOf,
    );
    expect(errors).toHaveLength(2);
    for (const error of errors) {
      expect(['Email is required.', 'Password is required.']).not.toContain(error);
      expect(isTranslationIn('pl', error), `"${error}" is not a pl.json value`).toBe(true);
    }
  });
});
