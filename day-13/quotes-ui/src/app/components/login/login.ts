import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { AuthService } from '../../services/auth';

type Mode = 'sign-in' | 'create-account';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);

  protected readonly mode = signal<Mode>('sign-in');
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  toggleMode(): void {
    this.mode.set(this.mode() === 'sign-in' ? 'create-account' : 'sign-in');
    this.error.set(null);
  }

  onSubmit(): void {
    if (this.submitting()) return;

    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.submitting.set(true);
    this.error.set(null);

    const credentials = this.form.getRawValue();
    const request$ =
      this.mode() === 'sign-in' ? this.auth.login(credentials) : this.auth.register(credentials);

    request$.subscribe({
      next: () => {
        this.submitting.set(false);
        this.form.reset({ email: '', password: '' });
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.error.set(this.describeError(err));
      },
    });
  }

  private describeError(err: HttpErrorResponse): string {
    const serverMessage = (err.error as { error?: string } | null)?.error;
    if (serverMessage) return serverMessage;
    if (err.status === 401) return 'Invalid email or password.';
    if (err.status === 0) return 'Could not reach the API. Is it running?';
    return 'Something went wrong. Please try again.';
  }
}
