import { AbstractControl, ValidationErrors } from '@angular/forms';

// Angular's built-in Validators.required does not trim: a string of only
// spaces has length > 0, so it passes. The real server check is
// string.IsNullOrWhiteSpace(request.Author/Text) (EndpointExtensions.cs),
// which rejects whitespace-only input. Without this, the client would
// accept something the server 400s on.
export function notBlank(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  const isBlank = value == null || (typeof value === 'string' && value.trim().length === 0);
  return isBlank ? { required: true } : null;
}
