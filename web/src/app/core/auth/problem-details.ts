import type { FormGroup } from '@angular/forms';

// ASP.NET Core's ProblemDetails shape (RFC 7807) plus the extensions ServiceDefaults/ADR-017
// always add: `errorCode` (handler Result failures) and, for the built-in Minimal API validation
// (AddValidation() + DataAnnotations), an `errors` dictionary keyed by request property name.
export interface ApiProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errorCode?: string;
  errors?: Record<string, string[]>;
  traceId?: string;
}

// The generated client (throwOnError: false) resolves failed calls to `{ error }` where `error`
// is the parsed JSON body when the response was JSON, otherwise raw response text.
export function readProblemDetails(error: unknown): ApiProblemDetails {
  if (error && typeof error === 'object') {
    return error as ApiProblemDetails;
  }
  return { detail: typeof error === 'string' ? error : 'Something went wrong. Please try again.' };
}

// Field names in `errors` come from the C# request record's property names (PascalCase) and
// aren't run through the JSON camelCase naming policy (that only applies to object properties,
// not dictionary keys) — match case-insensitively against the form's actual control names.
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
