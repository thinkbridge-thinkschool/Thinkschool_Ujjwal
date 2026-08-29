using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace QuotesApi.Authorization;

// Lets "can-*-quotes" policies do a plain RequireClaim("scope", ...) check
// without caring whether the request came in via the Internal or Entra scheme.
public class ScopeClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == "scope"))
            return Task.FromResult(principal);

        var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated);
        if (identity is null)
            return Task.FromResult(principal);

        var scp = principal.FindFirst("scp")?.Value;
        if (!string.IsNullOrWhiteSpace(scp))
        {
            foreach (var scope in scp.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                identity.AddClaim(new Claim("scope", scope));
        }

        foreach (var role in identity.FindAll("roles").ToList())
            identity.AddClaim(new Claim("scope", role.Value));

        return Task.FromResult(principal);
    }
}
