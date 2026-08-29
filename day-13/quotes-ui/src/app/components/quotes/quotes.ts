import { Component, OnInit, effect, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesStore } from '../../store/quotes-store';

@Component({
  selector: 'app-quotes',
  imports: [RouterLink],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class QuotesComponent implements OnInit {
  protected readonly store = inject(QuotesStore);

  // Reacts to filteredQuotes (itself derived from quotes + filter), so the
  // tab title stays in sync without any manual wiring in onFilterInput.
  private readonly titleEffect = effect(() => {
    const count = this.store.filteredQuotes().length;
    document.title = count > 0 ? `Quotes (${count})` : 'Quotes';
  });

  ngOnInit(): void {
    // Only load on the FIRST visit - the store already has the data after
    // that (including anything create()/remove() added or removed since),
    // so revisiting this route must not silently refetch and throw away
    // the point of caching it.
    if (this.store.status() === 'idle') {
      this.store.load();
    }
  }

  onFilterInput(event: Event): void {
    this.store.setFilter((event.target as HTMLInputElement).value);
  }
}
