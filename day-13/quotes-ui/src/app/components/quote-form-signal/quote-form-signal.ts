import { Component, inject, signal } from '@angular/core';
import { FormField, form, maxLength, required, requiredError, submit, validate } from '@angular/forms/signals';
import { Router } from '@angular/router';
import { QuotesStore } from '../../store/quotes-store';
import { Quote } from '../../models/quote.model';
import { AppHttpError } from '../../http/app-http-error';

@Component({
  selector: 'app-quote-form-signal',
  imports: [FormField],
  templateUrl: './quote-form-signal.html',
  styleUrl: './quote-form-signal.css',
})
export class QuoteFormSignalComponent {
  private readonly store = inject(QuotesStore);
  private readonly router = inject(Router);

  protected readonly model = signal({ author: '', text: '' });

  // required()'s own isEmpty() check is `value === ''` (confirmed by reading
  // fesm2022/signals.mjs directly) - a whitespace-only string still passes,
  // same gap Angular's Validators.required has in the reactive-forms version
  // of this form. Kept required() for the native `required` attribute it
  // wires up automatically, but added an explicit whitespace check on top -
  // the built-in validator alone would not match the server's
  // string.IsNullOrWhiteSpace check. The whitespace check only fires for
  // non-empty-but-blank input (`v !== '' && ...`) so it never doubles up
  // with required()'s own error on a plain empty string - found the
  // doubled "Author is required. Author is required." in a real browser
  // run before adding that guard.
  protected readonly quoteForm = form(this.model, (path) => {
    required(path.author, { message: 'Author is required.' });
    validate(path.author, ({ value }) => {
      const v = value();
      return v !== '' && v.trim().length === 0
        ? requiredError({ message: 'Author is required.' })
        : undefined;
    });
    maxLength(path.author, 200, { message: 'Author must be at most 200 characters.' });

    required(path.text, { message: 'Text is required.' });
    validate(path.text, ({ value }) => {
      const v = value();
      return v !== '' && v.trim().length === 0
        ? requiredError({ message: 'Text is required.' })
        : undefined;
    });
    maxLength(path.text, 2000, { message: 'Text must be at most 2000 characters.' });
  });

  protected readonly formError = signal<string | null>(null);

  showFieldError(name: 'author' | 'text'): boolean {
    const field = this.quoteForm[name]();
    return field.invalid() && field.touched();
  }

  fieldErrorMessage(name: 'author' | 'text'): string | null {
    if (!this.showFieldError(name)) return null;
    return this.quoteForm[name]()
      .errors()
      .map((e) => e.message)
      .filter((m): m is string => !!m)
      .join(' ');
  }

  onSubmit(): void {
    this.formError.set(null);

    submit(this.quoteForm, {
      action: async (field) => {
        try {
          await new Promise<Quote>((resolve, reject) => {
            this.store.create(field().value()).subscribe({ next: resolve, error: reject });
          });
          this.model.set({ author: '', text: '' });
          this.router.navigate(['/quotes']);
          return undefined;
        } catch (err) {
          return this.mapServerError(err as AppHttpError);
        }
      },
    });
  }

  private mapServerError(err: AppHttpError) {
    const fieldErrors = err.fieldErrors;
    if (fieldErrors && Object.keys(fieldErrors).length > 0) {
      const mapped = Object.entries(fieldErrors)
        .filter(([field]) => field === 'author' || field === 'text')
        .map(([field, messages]) => ({
          fieldTree: this.quoteForm[field as 'author' | 'text'],
          kind: 'server',
          message: messages.join(' '),
        }));
      if (mapped.length > 0) {
        return mapped;
      }
    }

    this.formError.set(err.friendlyMessage);
    return undefined;
  }
}
