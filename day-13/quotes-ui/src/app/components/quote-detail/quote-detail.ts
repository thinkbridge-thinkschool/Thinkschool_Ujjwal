import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { QuotesStore } from '../../store/quotes-store';
import { AuthService } from '../../services/auth';
import { AppHttpError } from '../../http/app-http-error';

type DetailState = 'loading' | 'loaded' | 'error' | 'not-found';

@Component({
  selector: 'app-quote-detail',
  imports: [RouterLink],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetailComponent {
  private readonly store = inject(QuotesStore);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // Bound directly from the :id route param via withComponentInputBinding
  // - route params are always strings, so the numeric conversion (and its
  // NaN guard) happens here, not at the router boundary.
  readonly id = input<string>();

  protected readonly state = signal<DetailState>('loading');

  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<string | null>(null);

  // Derived from the store's cache rather than held as its own copy - if
  // the cached quote is ever updated elsewhere, this reflects it
  // automatically. Only the fetch's own lifecycle (loading/error/not-found)
  // stays local, since that's specific to this screen's request, not
  // shared data.
  protected readonly quote = computed(() => {
    const raw = this.id();
    const numericId = Number(raw);
    if (!Number.isFinite(numericId)) {
      return null;
    }
    return this.store.getById(numericId) ?? null;
  });

  protected readonly canDelete = computed(() => {
    const q = this.quote();
    const userId = this.auth.currentUserId();
    return q !== null && userId !== null && q.createdByUserId === userId;
  });

  // Number('abc') is NaN, and Number(undefined) is also NaN, so both the
  // "route param is missing" and "route param isn't a number" cases land
  // here without ever reaching the HTTP call - no request to
  // /api/quotes/NaN. A numeric id that the backend doesn't have is a
  // separate, already-handled case: the 'error' state below.
  private readonly fetchEffect = effect(() => {
    const raw = this.id();
    this.deleteError.set(null);

    const numericId = Number(raw);
    if (!Number.isFinite(numericId)) {
      this.state.set('not-found');
      return;
    }

    // Already cached (arrived here from the list, or a prior visit) - no
    // network call at all.
    if (this.store.getById(numericId)) {
      this.state.set('loaded');
      return;
    }

    this.state.set('loading');
    this.store.loadOne(numericId).subscribe({
      next: () => {
        // Discard a response that resolves after the route has already
        // moved on to a different id - the same stale-response guard as
        // before routing and the store existed, just keyed off the route
        // param instead of a parent input.
        if (this.id() === raw) {
          this.state.set('loaded');
        }
      },
      error: () => {
        if (this.id() === raw) {
          this.state.set('error');
        }
      },
    });
  });

  onDelete(): void {
    const q = this.quote();
    if (!q || this.deleting()) return;

    if (!confirm('Delete this quote? This cannot be undone.')) {
      return;
    }

    this.deleting.set(true);
    this.deleteError.set(null);

    this.store.remove(q.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.router.navigate(['/quotes']);
      },
      error: (err: AppHttpError) => {
        this.deleting.set(false);
        this.deleteError.set(
          err.status === 403 ? 'You can only delete quotes you created.' : err.friendlyMessage,
        );
      },
    });
  }
}
