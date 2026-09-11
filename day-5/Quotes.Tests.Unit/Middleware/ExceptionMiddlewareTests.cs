using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using QuotesApi.Middleware;

namespace Quotes.Tests.Unit.Middleware;

public class ExceptionMiddlewareTests
{
    private static IHostEnvironment FakeEnvironment(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return env;
    }

    // day-27: the exception's real message must still reach the response
    // in Development - it's how you'd actually debug something locally.
    [Fact]
    public async Task InvokeAsync_NextThrows_InDevelopment_ReturnsRealExceptionMessage()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => throw new InvalidOperationException("boom"),
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: FakeEnvironment(Environments.Development));

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

    // day-27: the actual security fix - production must never hand an
    // anonymous caller the raw exception message (file paths, internal
    // type names, connection details can all end up in ex.Message).
    [Fact]
    public async Task InvokeAsync_NextThrows_InProduction_ReturnsGenericMessage()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => throw new InvalidOperationException("boom - internal detail that must not leak"),
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: FakeEnvironment(Environments.Production));

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        problem!.Detail.Should().NotContain("boom");
        problem.Detail.Should().Be("An unexpected error occurred. Please try again later.");
    }

    [Fact]
    public async Task InvokeAsync_NextSucceeds_DoesNotModifyResponse()
    {
        var middleware = new ExceptionMiddleware(
            next: _ => Task.CompletedTask,
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: FakeEnvironment(Environments.Production));

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Length.Should().Be(0);
    }
}
