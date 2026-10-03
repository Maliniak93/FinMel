import type { FormGroup } from '@angular/forms';
import { translate } from '@jsverse/transloco';

export interface ApiProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errorCode?: string;
  errors?: Record<string, string[]>;
  traceId?: string;
}

export function readProblemDetails(error: unknown): ApiProblemDetails {
  if (error && typeof error === 'object') {
    return error as ApiProblemDetails;
  }
  return { detail: typeof error === 'string' ? error : translate('errors.generic') };
}

// `errors` keys are dictionary keys, which skip the camelCase policy, so they match case-insensitively.
export function applyFieldErrors(form: FormGroup, problem: ApiProblemDetails): boolean {
  if (!problem.errors) {
    return false;
  }

  let matched = false;
  for (const [field, messages] of Object.entries(problem.errors)) {
    const controlName = Object.keys(form.controls).find(
      (name) => name.toLowerCase() === field.toLowerCase(),
    );
    if (controlName) {
      form.get(controlName)?.setErrors({ server: messages.join(' ') });
      matched = true;
    }
  }
  return matched;
}
