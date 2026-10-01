namespace RestaurantSeating.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            BadHttpRequestException badRequest =>
                (badRequest.StatusCode, "Invalid request", "Invalid body or parameters."),
            GroupNotFoundException => (404, "Group not found", exception.Message),
            StateConflictException => (409, "Invalid state transition", exception.Message),
            DbUpdateConcurrencyException => (409, "Concurrency conflict", "Concurrent group update."),
            SqlException { Number: 51006 or 1205 or 1222 or -2 } =>
                (503, "Database is busy", "Database timeout or concurrency conflict."),
            DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 or -2 } } =>
                (503, "Database is busy", "Database timeout or concurrency conflict."),
            _ => (500, "Internal server error", "Unexpected error.")
        };

        if (status == 503)
        {
            var sql = (exception as SqlException) ?? (exception.InnerException as SqlException);
            logger.LogWarning("db.busy code={SqlCode} path={Path}", sql?.Number, context.Request.Path);
            context.Response.Headers.RetryAfter = "1";
        }
        else if (status == 500)
            logger.LogError(exception, "request.failed path={Path}", context.Request.Path);

        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new()
            {
                Status = status, Title = title, Detail = detail, Instance = context.Request.Path
            }
        });
        return true;
    }
}
