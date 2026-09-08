import { AbstractControl, ValidationErrors } from '@angular/forms';

// Rejects a bare email address, or a value with no real word in it at all
// (all digits, all punctuation/symbols) - "!!!", "12345", and
// "a@b.com" all pass Validators.required and notBlank but aren't quote
// or author content. Mirrors IsQuoteContent in EndpointExtensions.cs so
// nothing accepted here gets rejected by the server, or vice versa.
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const WORD_PATTERN = /[A-Za-z]{2,}/;

export function quoteContent(control: AbstractControl): ValidationErrors | null {
  const value = typeof control.value === 'string' ? control.value.trim() : '';
  if (value.length === 0) {
    // Blank input is notBlank's error to report, not this one's.
    return null;
  }

  const isEmail = EMAIL_PATTERN.test(value);
  const hasWord = WORD_PATTERN.test(value);

  return !isEmail && hasWord ? null : { notQuoteContent: true };
}
