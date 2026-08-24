import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { QuoteService } from '../../services/quote';
import { Quote } from '../../models/quote.model';

@Component({
  selector: 'app-quotes',
  imports: [],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class QuotesComponent implements OnInit {
  private readonly quoteService = inject(QuoteService);

  protected readonly loading = signal(true);
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly filter = signal('');

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

  ngOnInit(): void {
    this.quoteService.getQuotes().subscribe({
      next: (quotes) => {
        this.quotes.set(quotes);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  onFilterInput(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }
}
