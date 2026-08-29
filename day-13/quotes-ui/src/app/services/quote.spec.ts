import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuoteService } from './quote';
import { Quote } from '../models/quote.model';

// Characterization test: pins the REAL Week-1 QuotesApi contract as
// QuoteService actually consumes it today, before any interceptor exists.
// Source of truth, read directly from day-5/QuotesApi/Extensions/EndpointExtensions.cs:
//   GET  /api/quotes?page=N&size=N  -> 200, Quote[] { id, author, text, createdByUserId }
//   POST /api/quotes (invalid body) -> 400, HttpValidationProblemDetails
//     { type, title, status, errors: Record<string, string[]> }
// If this test ever breaks, the frontend's assumption about the contract
// has drifted from the real backend - that's the point of it.
describe('QuoteService - characterizes the real Week-1 API contract', () => {
  let service: QuoteService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QuoteService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GET /api/quotes?page=1&size=100 returns Quote[] shaped {id, author, text, createdByUserId}', () => {
    const mockResponse: Quote[] = [
      {
        id: 6,
        author: 'Maya Angelou',
        text: "I've learned that people will forget what you said...",
        createdByUserId: '1', createdBy: null,
      },
      { id: 2, author: 'Steve Jobs', text: 'The only way to do great work is to love what you do.', createdByUserId: null, createdBy: null },
    ];

    let actual: Quote[] | undefined;
    service.getQuotes().subscribe((quotes) => (actual = quotes));

    const req = httpMock.expectOne(
      (r) => r.url === 'http://localhost:5296/api/quotes' && r.params.get('page') === '1' && r.params.get('size') === '100',
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockResponse);

    expect(actual).toEqual(mockResponse);
    // Pins the field shape explicitly, not just object equality with the
    // fixture - createdByUserId is nullable, id is numeric, per the real
    // Quote model (QuotesApi.Models.Quote: int Id, string Author, string
    // Text, string? CreatedByUserId).
    for (const quote of actual ?? []) {
      expect(typeof quote.id).toBe('number');
      expect(typeof quote.author).toBe('string');
      expect(typeof quote.text).toBe('string');
      expect(quote.createdByUserId === null || typeof quote.createdByUserId === 'string').toBe(true);
    }
  });

  it('GET /api/quotes/{id} hits the real per-id route and returns a single Quote', () => {
    const mockQuote: Quote = { id: 25, author: 'Mark Twain', text: '...', createdByUserId: '1', createdBy: null };

    let actual: Quote | undefined;
    service.getQuoteById(25).subscribe((quote) => (actual = quote));

    const req = httpMock.expectOne('http://localhost:5296/api/quotes/25');
    expect(req.request.method).toBe('GET');
    req.flush(mockQuote);

    expect(actual).toEqual(mockQuote);
  });

  it('POST /api/quotes never sends server-owned fields (id, createdByUserId)', () => {
    service.createQuote({ author: 'Ada Lovelace', text: 'Real quote text.' }).subscribe();

    const req = httpMock.expectOne('http://localhost:5296/api/quotes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ author: 'Ada Lovelace', text: 'Real quote text.' });
    expect(req.request.body.id).toBeUndefined();
    expect(req.request.body.createdByUserId).toBeUndefined();

    req.flush({ id: 40, author: 'Ada Lovelace', text: 'Real quote text.', createdByUserId: '4', createdBy: null });
  });

  it('a 400 from POST /api/quotes surfaces the real ValidationProblemDetails shape untouched', () => {
    // Captured verbatim from a live POST with an empty author, earlier in
    // this project (Day 14 phase 0 contract extraction).
    const problemDetails = {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { author: ['Author is required.'] },
    };

    let caught: HttpErrorResponse | undefined;
    service.createQuote({ author: '', text: 'Some text' }).subscribe({
      error: (err: HttpErrorResponse) => (caught = err),
    });

    const req = httpMock.expectOne('http://localhost:5296/api/quotes');
    req.flush(problemDetails, { status: 400, statusText: 'Bad Request' });

    expect(caught?.status).toBe(400);
    expect(caught?.error).toEqual(problemDetails);
    expect(caught?.error.errors.author).toEqual(['Author is required.']);
  });

  it('DELETE /api/quotes/{id} hits the real route with no body', () => {
    service.deleteQuote(38).subscribe();

    const req = httpMock.expectOne('http://localhost:5296/api/quotes/38');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
