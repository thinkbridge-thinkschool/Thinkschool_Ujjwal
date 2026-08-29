import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, of, tap } from 'rxjs';
import { QuoteService } from '../services/quote';
import { CreateQuoteRequest, Quote } from '../models/quote.model';
import { AppHttpError } from '../http/app-http-error';

type LoadStatus = 'idle' | 'loading' | 'loaded' | 'error';

@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly quoteService = inject(QuoteService);

  private readonly _quotes = signal<Quote[]>([]);
  private readonly _status = signal<LoadStatus>('idle');
  private readonly _error = signal<AppHttpError | null>(null);
  private readonly _filter = signal('');

  readonly quotes = this._quotes.asReadonly();
  readonly status = this._status.asReadonly();
  readonly error = this._error.asReadonly();
  readonly filter = this._filter.asReadonly();

  readonly filteredQuotes = computed(() => {
    const term = this._filter().trim().toLowerCase();
    const all = this._quotes();
    if (!term) {
      return all;
    }
    return all.filter(
      (quote) =>
        quote.author.toLowerCase().includes(term) || quote.text.toLowerCase().includes(term),
    );
  });

  // Bumped on every load() call, captured as `token` in the closure below.
  // A response only gets applied if it's still the most recently issued
  // load - this is the same discipline QuoteDetailComponent already used
  // for its own stale-response guard, just generalized: whichever load()
  // was called LAST wins, even if an earlier call's response arrives after
  // it (out-of-order network replies are real, not hypothetical).
  private loadToken = 0;

  load(): void {
    const token = ++this.loadToken;
    this._status.set('loading');
    this._error.set(null);

    this.quoteService.getQuotes().subscribe({
      next: (quotes) => {
        if (token !== this.loadToken) return;
        this._quotes.set(quotes);
        this._status.set('loaded');
      },
      error: (err: AppHttpError) => {
        if (token !== this.loadToken) return;
        this._error.set(err);
        this._status.set('error');
      },
    });
  }

  // Returns the Observable rather than swallowing it, so QuoteFormComponent
  // and QuoteFormSignalComponent keep driving their own submitting/error UI
  // exactly as before (field-level server errors, spinners, focus
  // management) - the store's only job is the cache side effect on
  // success. Replaces the array with a new reference; under zoneless a
  // push() would mutate in place and never trigger a re-render.
  create(request: CreateQuoteRequest): Observable<Quote> {
    return this.quoteService.createQuote(request).pipe(
      tap((quote) => {
        this._quotes.set([...this._quotes(), quote]);
      }),
    );
  }

  // Same reasoning as create(): thin pass-through, cache updated as a side
  // effect, caller keeps its own UI state. Filters rather than splices for
  // the same zoneless-reactivity reason.
  remove(id: number): Observable<void> {
    return this.quoteService.deleteQuote(id).pipe(
      tap(() => {
        this._quotes.set(this._quotes().filter((q) => q.id !== id));
      }),
    );
  }

  setFilter(value: string): void {
    this._filter.set(value);
  }

  // Synchronous cache lookup - a plain signal read, not its own signal, so
  // callers that read it inside an effect or computed still track _quotes
  // correctly through the method call.
  getById(id: number): Quote | undefined {
    return this._quotes().find((q) => q.id === id);
  }

  // For the deep-link case (QuoteDetailComponent reached directly, list
  // never loaded): serves from cache when present - no network call at
  // all - and only falls back to GET /api/quotes/{id}, the cheap
  // single-item endpoint, rather than fetching the whole list just to find
  // one row. A successful fetch is merged into the cache so a later visit
  // to /quotes won't have to re-fetch it either.
  loadOne(id: number): Observable<Quote> {
    const cached = this.getById(id);
    if (cached) {
      return of(cached);
    }

    return this.quoteService.getQuoteById(id).pipe(
      tap((quote) => {
        if (!this.getById(quote.id)) {
          this._quotes.set([...this._quotes(), quote]);
        }
      }),
    );
  }
}
