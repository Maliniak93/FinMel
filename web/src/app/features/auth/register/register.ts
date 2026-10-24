import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { AuthService } from '../../../core/auth/auth';
import { applyFieldErrors, type ApiProblemDetails } from '../../../core/auth/problem-details';
import { AuthShell } from '../auth-shell/auth-shell';

@Component({
  selector: 'app-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    TranslocoPipe,
    AuthShell,
  ],
  templateUrl: './register.html',
  styleUrl: './register.scss',
})
export class Register {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    password: ['', [Validators.required, Validators.minLength(6)]],
  });

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const result = await this.authService.register(this.form.getRawValue());

    this.submitting.set(false);

    if (!result.success) {
      this.applyServerErrors(result.problem);
      return;
    }

    await this.router.navigateByUrl('/login');
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    if (problem.errorCode === 'Conflict.DuplicateEmail') {
      this.form.controls.email.setErrors({
        server: problem.detail ?? translate('auth.register.duplicateEmail'),
      });
      return;
    }

    if (problem.errorCode === 'Validation.Register') {
      this.form.controls.password.setErrors({
        server: problem.detail ?? translate('auth.register.weakPassword'),
      });
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
