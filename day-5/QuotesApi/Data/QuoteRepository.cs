using Microsoft.EntityFrameworkCore;
using QuotesApi.Diagnostics;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuoteRepository : IQuoteRepository
{
    private readonly QuotesDbContext _context;
    private readonly ILogger<QuoteRepository> _logger;
    private readonly QuoteQueryCounter _queryCounter;

    // queryCounter is optional (not just for DI convenience): a sibling
    // test project outside this task's scope
    // (day-5/Quotes.Tests.Integration/QuoteRepositoryTests.cs)
    // constructs this type directly with the pre-Day-21 2-argument
    // constructor. Defaulting to a throwaway instance keeps that call
    // compiling unchanged - real DI usage always supplies the actual
    // singleton via InfrastructureExtensions.cs, so production counting
    // is unaffected.
    public QuoteRepository(QuotesDbContext context, ILogger<QuoteRepository> logger, QuoteQueryCounter? queryCounter = null)
    {
        _context = context;
        _logger = logger;
        _queryCounter = queryCounter ?? new QuoteQueryCounter();
    }

    public async Task<List<Quote>> GetPagedAsync(int page, int size, CancellationToken ct)
    {
        _queryCounter.Increment();
        _logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);
        return await _context.Quotes
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
    }

    public async Task<Quote?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task<Quote> AddAsync(Quote quote, CancellationToken ct)
    {
        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Created quote {Id}", quote.Id);
        return quote;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var quote = await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null) return false;

        _context.Quotes.Remove(quote);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted quote {Id}", id);
        return true;
    }
}