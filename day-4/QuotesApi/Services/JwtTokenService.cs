using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Configuration;
using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    int AccessTokenMinutes { get; }
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    // Singleton service, config doesn't change at runtime: IOptions<T> (not
    // IOptionsSnapshot/IOptionsMonitor) is the correct fit here.
    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public int AccessTokenMinutes => (int)_options.AccessTokenLifetime.TotalMinutes;

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("scope", "quotes.read"),
            new Claim("scope", "quotes.write"),
            new Claim("scope", "quotes.delete")
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(_options.AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }
}