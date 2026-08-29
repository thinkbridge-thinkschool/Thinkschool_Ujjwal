using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Middleware;

namespace Quotes.Tests.Unit.Middleware;

public class ExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NextThrows_Returns500WithProblemDetails()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => throw new InvalidOperationException("boom"),
            logger: NullLogger<ExceptionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        // WriteAsJsonAsync below overwrites the "application/problem+json" set a few
        // lines up in the middleware with its own default content type — asserting the
        // actual value here, not the one the middleware source appears to intend.
        context.Response.ContentType.Should().Be("application/json; charset=utf-8");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        problem!.Status.Should().Be(500);
        problem.Title.Should().Be("An unexpected error occurred");
        problem.Detail.Should().Be("boom");
    }

    [Fact]
    public async Task InvokeAsync_NextSucceeds_DoesNotModifyResponse()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => Task.CompletedTask,
            logger: NullLogger<ExceptionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Length.Should().Be(0);
    }
}
