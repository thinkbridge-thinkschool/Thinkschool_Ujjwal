using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            // day-27: this used to put ex.Message directly in the response
            // body, in every environment including production - a real
            // information-disclosure bug (exception messages can carry
            // file paths, connection details, internal type/method names).
            // The full exception is still logged (line above), so nothing
            // is lost for debugging; only what an anonymous caller can see
            // over HTTP is restricted.
            var problem = new ProblemDetails
            {
                Status = 500,
                Title = "An unexpected error occurred",
                Detail = _env.IsDevelopment() ? ex.Message : "An unexpected error occurred. Please try again later."
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}