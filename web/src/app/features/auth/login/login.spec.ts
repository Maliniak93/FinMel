import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService } from '../../../core/auth/auth';
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
      providers: [provideRouter([]), { provide: AuthService, useValue: authService }],
    }).compileComponents();

    fixture = TestBed.createComponent(Login);
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
});
