namespace QuotesApi.Models;

public class RefreshToken
{
    public int Id { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public int UserId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(string plainToken, int userId, DateTimeOffset expiresAt)
    {
        return new RefreshToken
        {
            TokenHash = Hash(plainToken),
            UserId = userId,
            ExpiresAt = expiresAt
        };
    }

    public static string Hash(string plainToken)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(plainToken));
        return Convert.ToBase64String(bytes);
    }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    public void Revoke(string? replacedByTokenHash = null)
    {
        RevokedAt = DateTimeOffset.UtcNow;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}