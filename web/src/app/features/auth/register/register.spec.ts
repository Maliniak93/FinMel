import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService } from '../../../core/auth/auth';
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
      providers: [provideRouter([]), { provide: AuthService, useValue: authService }],
    }).compileComponents();

    fixture = TestBed.createComponent(Register);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    await fixture.whenStable();
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
      problem: { errorCode: 'Validation.Register', detail: 'Passwords must be at least 6 characters.' },
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
});
