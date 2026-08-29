using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

public class MustOwnQuoteRequirement : IAuthorizationRequirement
{
}

public class MustOwnQuoteHandler : AuthorizationHandler<MustOwnQuoteRequirement, Quote>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MustOwnQuoteRequirement requirement,
        Quote resource)
    {
        var callerId = context.User.FindFirst("oid")?.Value ?? context.User.FindFirst("sub")?.Value;

        if (callerId is not null && callerId == resource.CreatedByUserId)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
