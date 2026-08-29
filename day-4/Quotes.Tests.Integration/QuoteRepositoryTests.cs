using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;

namespace Quotes.Tests.Integration;

// Exercises the repository directly rather than through an endpoint: the delete-quote
// endpoint already 404s before ever calling DeleteAsync, so this guard is only
// reachable if something calls the repository without checking existence first.
public class QuoteRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"quotesapi-quoterepo-tests-{Guid.NewGuid():N}.db");
    private readonly QuotesDbContext _context;

    public QuoteRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _context = new QuotesDbContext(options);
        _context.Database.Migrate();
    }

    public void Dispose()
    {
        _context.Dispose();

        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task DeleteAsync_QuoteDoesNotExist_ReturnsFalse()
    {
        var repository = new QuoteRepository(_context, NullLogger<QuoteRepository>.Instance);

        var result = await repository.DeleteAsync(999999, CancellationToken.None);

        Assert.False(result);
    }
}
