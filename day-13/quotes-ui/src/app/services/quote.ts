import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Quote } from '../models/quote.model';

const QUOTES_URL = 'http://localhost:5296/api/quotes';

@Injectable({
  providedIn: 'root',
})
export class QuoteService {
  private readonly http = inject(HttpClient);

  getQuotes(): Observable<Quote[]> {
    // The API defaults to page=1&size=10; request the max page size so the
    // component sees the full list rather than a silently-truncated one.
    const params = new HttpParams().set('page', 1).set('size', 100);
    return this.http.get<Quote[]>(QUOTES_URL, { params });
  }

  getQuoteById(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${QUOTES_URL}/${id}`);
  }
}
