using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authorization;
using QuotesApi.Configuration;

namespace QuotesApi.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration config)
    {
        // Runs before builder.Build(), so IOptions<T> isn't resolvable yet - these
        // closures need concrete values now. This only guards against the whole
        // section being absent; ValidateOnStart below still catches an individual
        // missing/invalid property (e.g. Key present but empty) at host startup.
        var jwtOptions = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException($"{JwtOptions.SectionName} configuration section is missing.");
        var entraOptions = config.GetSection(EntraOptions.SectionName).Get<EntraOptions>()
            ?? throw new InvalidOperationException($"{EntraOptions.SectionName} configuration section is missing.");

        services.AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EntraOptions>()
            .Bind(config.GetSection(EntraOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // v2 issuer once the manifest has requestedAccessTokenVersion = 2.
        var entraV2Issuer = $"https://login.microsoftonline.com/{entraOptions.TenantId}/v2.0";
        // v1 issuer, which is what the az CLI returns if it is still on version 1.
        var entraV1Issuer = $"https://sts.windows.net/{entraOptions.TenantId}/";

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "PolicyScheme";
            options.DefaultChallengeScheme = "PolicyScheme";
        })
        .AddJwtBearer("Internal", options =>
        {
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "email",
                AuthenticationType = "Internal"
            };
        })
        .AddJwtBearer("Entra", options =>
        {
            // Authority discovery pulls the signing keys from the tenant's JWKS
            // endpoint, so no Entra key material lives in configuration.
            options.Authority = entraV2Issuer;
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = [entraV2Issuer, entraV1Issuer],
                ValidateAudience = true,
                // Bare GUID for v2 tokens, App ID URI for v1 tokens.
                ValidAudiences = [entraOptions.Audience, $"api://{entraOptions.Audience}"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "preferred_username",
                RoleClaimType = "roles",
                AuthenticationType = "Entra"
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = HandleAuthenticationFailedAsync
            };
        })
        .AddPolicyScheme("PolicyScheme", "PolicyScheme", options =>
        {
            options.ForwardDefaultSelector = context =>
                SelectScheme(context.Request.Headers.Authorization.ToString());
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("can-read-quotes", policy => policy.RequireClaim("scope", "quotes.read"));
            options.AddPolicy("can-edit-quotes", policy => policy.RequireClaim("scope", "quotes.write"));
            options.AddPolicy("can-delete-quotes", policy => policy.RequireClaim("scope", "quotes.delete"));
        });

        services.AddTransient<IClaimsTransformation, ScopeClaimsTransformation>();
        services.AddSingleton<IAuthorizationHandler, MustOwnQuoteHandler>();
        services.AddSingleton<IAuthorizationHandler, MustOwnCollectionHandler>();

        return services;
    }

    // Decides which JWT bearer scheme should validate the request: pure string/claim
    // logic pulled out of the PolicyScheme's ForwardDefaultSelector so it's testable
    // without booting a host. Anything that isn't a readable, Entra-issued token falls
    // through to Internal, which rejects it with a proper 401.
    public static string SelectScheme(string? authorizationHeader)
    {
        var header = authorizationHeader ?? string.Empty;

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return "Internal";

        var token = header["Bearer ".Length..].Trim();
        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(token))
            return "Internal";

        var issuer = handler.ReadJwtToken(token).Issuer;

        // Match both the v2 and v1 Entra issuers. Anything else falls through
        // to Internal, which rejects it with a proper 401.
        return issuer.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase)
            || issuer.StartsWith("https://sts.windows.net/", StringComparison.OrdinalIgnoreCase)
            ? "Entra"
            : "Internal";
    }

    public static Task HandleAuthenticationFailedAsync(AuthenticationFailedContext ctx)
    {
        var env = ctx.HttpContext.RequestServices
            .GetRequiredService<IHostEnvironment>();

        // Exception type only, never the token itself.
        if (env.IsDevelopment())
            ctx.Response.Headers["x-auth-error"] = ctx.Exception.GetType().Name;

        return Task.CompletedTask;
    }
}
