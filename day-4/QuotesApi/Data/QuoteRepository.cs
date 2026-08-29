using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuoteRepository : IQuoteRepository
{
    private readonly QuotesDbContext _context;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(QuotesDbContext context, ILogger<QuoteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<Quote>> GetPagedAsync(int page, int size, CancellationToken ct)
    {
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