import {
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { QuotesStore } from '../../store/quotes-store';
import { notBlank } from './not-blank.validator';
import { AppHttpError } from '../../http/app-http-error';

type SubmitState = 'idle' | 'submitting' | 'success' | 'error';

@Component({
  selector: 'app-quote-form',
  imports: [ReactiveFormsModule],
  templateUrl: './quote-form.html',
  styleUrl: './quote-form.css',
})
export class QuoteFormComponent {
  private readonly store = inject(QuotesStore);
  private readonly router = inject(Router);

  protected readonly state = signal<SubmitState>('idle');
  protected readonly formError = signal<string | null>(null);

  private readonly authorInput = viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput = viewChild<ElementRef<HTMLTextAreaElement>>('textInput');

  // maxLength mirrors CreateQuoteRequest's [MaxLength] annotations. Those
  // annotations are NOT actually enforced by the server right now (no
  // validation middleware reads them - verified live, a 250-char author
  // was accepted with 201) but they're the declared contract, so the client
  // is intentionally stricter than what the server currently checks.
  protected readonly form = new FormGroup({
    author: new FormControl('', {
      nonNullable: true,
      validators: [notBlank, Validators.maxLength(200)],
    }),
    text: new FormControl('', {
      nonNullable: true,
      validators: [notBlank, Validators.maxLength(2000)],
    }),
  });

  fieldErrorMessage(name: 'author' | 'text'): string | null {
    const control = this.form.controls[name];
    if (!control.invalid || !control.touched) {
      return null;
    }
    const label = name === 'author' ? 'Author' : 'Text';
    if (control.hasError('required')) {
      return `${label} is required.`;
    }
    if (control.hasError('maxlength')) {
      const limit = control.getError('maxlength').requiredLength;
      return `${label} must be at most ${limit} characters.`;
    }
    if (control.hasError('server')) {
      return control.getError('server') as string;
    }
    return null;
  }

  showFieldError(name: 'author' | 'text'): boolean {
    return this.fieldErrorMessage(name) !== null;
  }

  onSubmit(): void {
    if (this.state() === 'submitting') {
      return;
    }

    this.form.markAllAsTouched();

    if (this.form.invalid) {
      if (this.form.controls.author.invalid) {
        this.authorInput()?.nativeElement.focus();
      } else if (this.form.controls.text.invalid) {
        this.textInput()?.nativeElement.focus();
      }
      return;
    }

    this.state.set('submitting');
    this.formError.set(null);

    this.store.create(this.form.getRawValue()).subscribe({
      next: () => {
        this.state.set('success');
        this.form.reset({ author: '', text: '' });
        // Same reasoning as the Day 15 fix: navigating immediately would
        // unmount this component in the same tick the success state is
        // set, before "Quote added." ever paints a frame.
        setTimeout(() => this.router.navigate(['/quotes']), 900);
      },
      error: (err: AppHttpError) => {
        this.state.set('error');
        this.applyServerError(err);
      },
    });
  }

  private applyServerError(err: AppHttpError): void {
    const fieldErrors = err.fieldErrors;
    if (fieldErrors && Object.keys(fieldErrors).length > 0) {
      let mapped = false;
      for (const [field, messages] of Object.entries(fieldErrors)) {
        const control = this.form.controls[field as 'author' | 'text'];
        if (control) {
          control.setErrors({ ...control.errors, server: messages.join(' ') });
          control.markAsTouched();
          mapped = true;
        }
      }
      if (mapped) {
        return;
      }
    }

    this.formError.set(err.friendlyMessage);
  }
}
