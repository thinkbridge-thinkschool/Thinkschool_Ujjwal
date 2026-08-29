import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuotesStore } from './quotes-store';
import { Quote } from '../models/quote.model';

const QUOTES_URL = 'http://localhost:5296/api/quotes';

describe('QuotesStore', () => {
  let store: QuotesStore;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(QuotesStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts idle, with an empty cache', () => {
    expect(store.status()).toBe('idle');
    expect(store.quotes()).toEqual([]);
  });

  // The concurrency requirement: two load() calls in flight at once, and
  // the SECOND one's response arrives FIRST (a real, not hypothetical,
  // out-of-order network scenario). The later-issued call must still be
  // the one whose result survives - the first call's late-arriving
  // response must not clobber it.
  it('a stale response from an earlier load() cannot overwrite a later load()', () => {
    const firstCallQuotes: Quote[] = [{ id: 1, author: 'Old Author', text: 'Stale.', createdByUserId: null, createdBy: null }];
    const secondCallQuotes: Quote[] = [{ id: 2, author: 'New Author', text: 'Fresh.', createdByUserId: null, createdBy: null }];

    store.load(); // request A issued
    store.load(); // request B issued before A resolved

    const reqs = httpMock.match((r) => r.url === QUOTES_URL);
    expect(reqs.length).toBe(2);
    const [reqA, reqB] = reqs;

    // B (the later call) resolves first - out of order on the wire.
    reqB.flush(secondCallQuotes);
    expect(store.quotes()).toEqual(secondCallQuotes);
    expect(store.status()).toBe('loaded');

    // A (the earlier call) resolves after B - must be discarded, not applied.
    reqA.flush(firstCallQuotes);
    expect(store.quotes()).toEqual(secondCallQuotes);
    expect(store.status()).toBe('loaded');
  });

  it('an earlier load()\'s error cannot clobber a later load()\'s success', () => {
    const secondCallQuotes: Quote[] = [{ id: 5, author: 'Winner', text: 'Still here.', createdByUserId: null, createdBy: null }];

    store.load(); // request A
    store.load(); // request B

    const [reqA, reqB] = httpMock.match((r) => r.url === QUOTES_URL);

    reqB.flush(secondCallQuotes);
    expect(store.status()).toBe('loaded');

    reqA.flush({ error: 'stale failure' }, { status: 500, statusText: 'Server Error' });
    expect(store.status()).toBe('loaded');
    expect(store.quotes()).toEqual(secondCallQuotes);
    expect(store.error()).toBeNull();
  });

  it('create() replaces the array reference and appends the created quote', () => {
    const initial: Quote[] = [{ id: 1, author: 'A', text: 'One.', createdByUserId: null, createdBy: null }];
    store.load();
    httpMock.expectOne((r) => r.url === QUOTES_URL && r.method === 'GET').flush(initial);

    const before = store.quotes();
    store.create({ author: 'B', text: 'Two.' }).subscribe();
    httpMock
      .expectOne((r) => r.url === QUOTES_URL && r.method === 'POST')
      .flush({ id: 2, author: 'B', text: 'Two.', createdByUserId: '9', createdBy: null });

    expect(store.quotes()).not.toBe(before); // new reference, not a mutation
    expect(store.quotes().map((q) => q.id)).toEqual([1, 2]);
  });

  it('remove() replaces the array reference and drops the deleted quote', () => {
    const initial: Quote[] = [
      { id: 1, author: 'A', text: 'One.', createdByUserId: null, createdBy: null },
      { id: 2, author: 'B', text: 'Two.', createdByUserId: null, createdBy: null },
    ];
    store.load();
    httpMock.expectOne((r) => r.url === QUOTES_URL && r.method === 'GET').flush(initial);

    const before = store.quotes();
    store.remove(1).subscribe();
    httpMock.expectOne(`${QUOTES_URL}/1`).flush(null, { status: 204, statusText: 'No Content' });

    expect(store.quotes()).not.toBe(before);
    expect(store.quotes().map((q) => q.id)).toEqual([2]);
  });

  it('loadOne() serves from cache with no HTTP call when the quote is already loaded', () => {
    const initial: Quote[] = [{ id: 7, author: 'Cached', text: 'Already here.', createdByUserId: null, createdBy: null }];
    store.load();
    httpMock.expectOne((r) => r.url === QUOTES_URL && r.method === 'GET').flush(initial);

    let result: Quote | undefined;
    store.loadOne(7).subscribe((q) => (result = q));

    httpMock.verify(); // throws if any unexpected request was made
    expect(result).toEqual(initial[0]);
  });

  it('loadOne() falls back to GET /api/quotes/{id} and merges the result into the cache (the deep-link case)', () => {
    // No load() call at all - simulates navigating straight to /quotes/3
    // with the list never fetched.
    let result: Quote | undefined;
    store.loadOne(3).subscribe((q) => (result = q));

    const req = httpMock.expectOne(`${QUOTES_URL}/3`);
    const fetched: Quote = { id: 3, author: 'Deep Link', text: 'Found directly.', createdByUserId: null, createdBy: null };
    req.flush(fetched);

    expect(result).toEqual(fetched);
    expect(store.getById(3)).toEqual(fetched);
  });

  it('setFilter() drives filteredQuotes by author or text, case-insensitively', () => {
    store.load();
    httpMock.expectOne((r) => r.url === QUOTES_URL && r.method === 'GET').flush([
      { id: 1, author: 'Ada Lovelace', text: 'On computation.', createdByUserId: null, createdBy: null },
      { id: 2, author: 'Grace Hopper', text: 'On debugging.', createdByUserId: null, createdBy: null },
    ]);

    store.setFilter('hopper');
    expect(store.filteredQuotes().map((q) => q.id)).toEqual([2]);

    store.setFilter('COMPUTATION');
    expect(store.filteredQuotes().map((q) => q.id)).toEqual([1]);
  });
});
