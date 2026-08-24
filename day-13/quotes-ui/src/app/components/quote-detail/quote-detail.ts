import { Component, effect, inject, input, signal } from '@angular/core';
import { QuoteService } from '../../services/quote';
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

  readonly quoteId = input<number | null>(null);

  protected readonly state = signal<DetailState>('idle');
  protected readonly quote = signal<Quote | null>(null);

  // Re-fetches whenever quoteId changes. Selecting quotes fast enough that
  // two requests are in flight at once is a real race: whichever response
  // arrives last would otherwise win, even if it's for a quote the user has
  // already navigated away from. The `this.quoteId() === id` check discards
  // any response that resolves after a newer selection has already landed.
  private readonly fetchEffect = effect(() => {
    const id = this.quoteId();

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
}
