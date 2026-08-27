import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../services/auth';
import { AppHttpError } from '../../http/app-http-error';

type Mode = 'sign-in' | 'create-account';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

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
        // Send the user back to whatever route the auth guard bounced them
        // from (preserved as returnUrl), not a hardcoded default - only
        // fall back to /quotes when they arrived at /login directly.
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/quotes';
        this.router.navigateByUrl(returnUrl);
      },
      error: (err: AppHttpError) => {
        this.submitting.set(false);
        this.error.set(this.describeError(err));
      },
    });
  }

  // The interceptor's generic 401 message ("You need to sign in to do
  // that.") assumes an authorization failure on a protected resource - on
  // /api/auth/login a 401 always means bad credentials instead, and the
  // backend sends no body to derive that from (Results.Unauthorized() is
  // empty), so this is the one place that has to override the generic
  // friendlyMessage rather than just display it.
  private describeError(err: AppHttpError): string {
    if (err.status === 401) return 'Invalid email or password.';
    return err.friendlyMessage;
  }
}
