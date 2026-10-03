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
import { Register } from './register';

describe('Register', () => {
  let fixture: ComponentFixture<Register>;
  let component: Register;
  let authService: { register: ReturnType<typeof vi.fn> };
  let router: Router;

  beforeEach(async () => {
    authService = { register: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Register],
      providers: [
        provideRouter([]),
        provideI18nTesting(),
        { provide: AuthService, useValue: authService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Register);
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
    expect(authService.register).not.toHaveBeenCalled();
  });

  it('registers with the form values and navigates to /login on success', async () => {
    authService.register.mockResolvedValue({ success: true });
    component['form'].setValue({
      email: 'a@b.com',
      displayName: 'Ada',
      password: 'secretpw',
    });

    await component['onSubmit']();

    expect(authService.register).toHaveBeenCalledWith({
      email: 'a@b.com',
      displayName: 'Ada',
      password: 'secretpw',
    });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('maps a duplicate-email conflict onto the email field', async () => {
    authService.register.mockResolvedValue({
      success: false,
      problem: { errorCode: 'Conflict.DuplicateEmail', detail: 'Email already registered.' },
    });
    component['form'].setValue({ email: 'a@b.com', displayName: 'Ada', password: 'secretpw' });

    await component['onSubmit']();

    expect(component['form'].controls.email.getError('server')).toBe('Email already registered.');
    expect(component['formError']()).toBeNull();
  });

  it('maps a weak-password validation failure onto the password field', async () => {
    authService.register.mockResolvedValue({
      success: false,
      problem: {
        errorCode: 'Validation.Register',
        detail: 'Passwords must be at least 6 characters.',
      },
    });
    component['form'].setValue({ email: 'a@b.com', displayName: 'Ada', password: 'secretpw' });

    await component['onSubmit']();

    expect(component['form'].controls.password.getError('server')).toBe(
      'Passwords must be at least 6 characters.',
    );
  });

  it('maps a built-in validation errors dictionary onto matching controls', async () => {
    authService.register.mockResolvedValue({
      success: false,
      problem: { errors: { Email: ['The Email field is not a valid e-mail address.'] } },
    });
    component['form'].setValue({ email: 'a@b.com', displayName: 'Ada', password: 'secretpw' });

    await component['onSubmit']();

    expect(component['form'].controls.email.getError('server')).toBe(
      'The Email field is not a valid e-mail address.',
    );
  });

  it('falls back to a form-level banner for unrecognized errors', async () => {
    authService.register.mockResolvedValue({
      success: false,
      problem: { detail: 'Something went wrong. Please try again.' },
    });
    component['form'].setValue({ email: 'a@b.com', displayName: 'Ada', password: 'secretpw' });

    await component['onSubmit']();

    expect(component['formError']()).toBe('Something went wrong. Please try again.');
  });

  it('shows validation messages in Polish', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const errors = () => Array.from(element.querySelectorAll('mat-error'), textOf);
    const english = [
      'Display name is required.',
      'Display name is too long.',
      'Email is required.',
      'Enter a valid email address.',
      'Password is required.',
      'Password must be at least 6 characters.',
    ];
    const expectPolish = (messages: string[], count: number) => {
      expect(messages).toHaveLength(count);
      for (const message of messages) {
        expect(english).not.toContain(message);
        expect(message).not.toBe('');
        expect(looksLikeTranslationKey(message)).toBe(false);
      }
    };

    await TestBed.inject(LanguageService).setLanguage('pl');

    await component['onSubmit']();
    await fixture.whenStable();
    expectPolish(errors(), 3);
    for (const message of errors()) {
      expect(isTranslationIn('pl', message), `"${message}" is not a pl.json value`).toBe(true);
    }

    component['form'].setValue({
      email: 'not-an-email',
      displayName: 'x'.repeat(201),
      password: 'abc',
    });
    component['form'].markAllAsTouched();
    await fixture.whenStable();
    expectPolish(errors(), 3);
  });

  it('offers the language menu on the register page', async () => {
    await pickLanguageFromMenu(fixture, 'Polski');

    const title = textOf((fixture.nativeElement as HTMLElement).querySelector('mat-card-title'));
    expect(title).not.toBe('Register');
    expect(isTranslationIn('pl', title)).toBe(true);
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('pl');
  });
});
