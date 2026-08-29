namespace QuotesApi.Models;

public class User
{
    public int Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;

    private User() { }

    public static User Create(string email, string plainTextPassword)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(plainTextPassword);
        return new User
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash
        };
    }

    public bool VerifyPassword(string plainTextPassword)
    {
        return BCrypt.Net.BCrypt.Verify(plainTextPassword, PasswordHash);
    }
}