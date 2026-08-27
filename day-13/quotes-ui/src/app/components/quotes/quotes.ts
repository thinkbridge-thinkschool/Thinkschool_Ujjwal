import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuoteService } from '../../services/quote';
import { Quote } from '../../models/quote.model';

type LoadState = 'loading' | 'loaded' | 'error';

@Component({
  selector: 'app-quotes',
  imports: [RouterLink],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class QuotesComponent implements OnInit {
  private readonly quoteService = inject(QuoteService);

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly filter = signal('');

  // Derived from the two signals above: recomputes whenever quotes are
  // (re)loaded or the filter text changes.
  protected readonly filteredQuotes = computed(() => {
    const term = this.filter().trim().toLowerCase();
    const all = this.quotes();
    if (!term) {
      return all;
    }
    return all.filter(
      (quote) =>
        quote.author.toLowerCase().includes(term) || quote.text.toLowerCase().includes(term),
    );
  });

  // Reacts to filteredQuotes (itself derived from quotes + filter), so the
  // tab title stays in sync without any manual wiring in onFilterInput.
  private readonly titleEffect = effect(() => {
    const count = this.filteredQuotes().length;
    document.title = count > 0 ? `Quotes (${count})` : 'Quotes';
  });

  ngOnInit(): void {
    this.quoteService.getQuotes().subscribe({
      next: (quotes) => {
        this.quotes.set(quotes);
        this.loadState.set('loaded');
      },
      error: () => {
        this.loadState.set('error');
      },
    });
  }

  onFilterInput(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }
}
