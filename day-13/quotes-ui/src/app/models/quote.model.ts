export interface Quote {
  id: number;
  author: string;
  text: string;
  createdByUserId: string | null;
}

// Matches QuotesApi.Models.CreateQuoteRequest exactly: id and
// createdByUserId are server-owned and must never be sent by the client.
export interface CreateQuoteRequest {
  author: string;
  text: string;
}

// The shape of ASP.NET's Results.ValidationProblem(errors) response.
export interface ValidationProblemBody {
  errors?: Record<string, string[]>;
}
