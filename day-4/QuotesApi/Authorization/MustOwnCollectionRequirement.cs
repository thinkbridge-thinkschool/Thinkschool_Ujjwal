using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

public class MustOwnCollectionRequirement : IAuthorizationRequirement
{
}

public class MustOwnCollectionHandler : AuthorizationHandler<MustOwnCollectionRequirement, Collection>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MustOwnCollectionRequirement requirement,
        Collection resource)
    {
        var callerId = context.User.FindFirst("oid")?.Value ?? context.User.FindFirst("sub")?.Value;

        if (callerId is not null && callerId == resource.OwnerUserId)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
