import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { QuoteService } from '../../services/quote';
import { AuthService } from '../../services/auth';
import { Quote } from '../../models/quote.model';

type DetailState = 'idle' | 'loading' | 'loaded' | 'error';

@Component({
  selector: 'app-quote-detail',
  imports: [],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetailComponent {
  private readonly quoteService = inject(QuoteService);
  private readonly auth = inject(AuthService);

  readonly quoteId = input<number | null>(null);
  readonly quoteDeleted = output<number>();

  protected readonly state = signal<DetailState>('idle');
  protected readonly quote = signal<Quote | null>(null);

  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<string | null>(null);

  // DELETE /api/quotes/{id} requires ownership server-side (matched against
  // the JWT's sub claim), so only show the control when it would actually
  // succeed - not as a security boundary, just to avoid an inevitable 403.
  protected readonly canDelete = computed(() => {
    const q = this.quote();
    const userId = this.auth.currentUserId();
    return q !== null && userId !== null && q.createdByUserId === userId;
  });

  // Re-fetches whenever quoteId changes. Selecting quotes fast enough that
  // two requests are in flight at once is a real race: whichever response
  // arrives last would otherwise win, even if it's for a quote the user has
  // already navigated away from. The `this.quoteId() === id` check discards
  // any response that resolves after a newer selection has already landed.
  private readonly fetchEffect = effect(() => {
    const id = this.quoteId();
    this.deleteError.set(null);

    if (id === null) {
      this.state.set('idle');
      this.quote.set(null);
      return;
    }

    this.state.set('loading');
    this.quoteService.getQuoteById(id).subscribe({
      next: (quote) => {
        if (this.quoteId() === id) {
          this.quote.set(quote);
          this.state.set('loaded');
        }
      },
      error: () => {
        if (this.quoteId() === id) {
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

    this.quoteService.deleteQuote(q.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.quoteDeleted.emit(q.id);
      },
      error: (err: HttpErrorResponse) => {
        this.deleting.set(false);
        this.deleteError.set(
          err.status === 403
            ? 'You can only delete quotes you created.'
            : 'Could not delete this quote. Please try again.',
        );
      },
    });
  }
}
