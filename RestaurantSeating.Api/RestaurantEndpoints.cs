namespace RestaurantSeating.Api;

public static class RestaurantEndpoints
{
    public static void MapRestaurantEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("Restaurant")
            .WithMetadata(
                new ProducesResponseTypeMetadata(500, typeof(ProblemDetails), ["application/problem+json"]),
                new ProducesResponseTypeMetadata(503, typeof(ProblemDetails), ["application/problem+json"]));

        api.MapGet("/restaurant", (RestaurantService restaurant, CancellationToken cancellationToken, long? historyBeforeId = null) =>
            restaurant.GetStateAsync(cancellationToken, historyBeforeId))
            .WithDescription("All active groups and up to 100 closed groups by descending ID. Use nextHistoryBeforeId for the next history page.");

        api.MapPost("/tables", AddTableAsync).WithParameterValidation();
        api.MapPost("/groups", ArriveAsync).WithParameterValidation();

        api.MapPost("/groups/{id:long}/leave", (long id, RestaurantService restaurant, CancellationToken cancellationToken) =>
            restaurant.LeaveAsync(id, cancellationToken))
            .ProducesProblem(404).ProducesProblem(409);

        api.MapPost("/groups/{id:long}/complete", (long id, RestaurantService restaurant, CancellationToken cancellationToken) =>
            restaurant.CompleteAsync(id, cancellationToken))
            .ProducesProblem(404).ProducesProblem(409);
    }

    private static async Task<Created<TableResponse>> AddTableAsync(
        AddTableRequest request, RestaurantService restaurant, CancellationToken cancellationToken)
    {
        var table = await restaurant.AddTableAsync(request.Capacity, cancellationToken);
        return TypedResults.Created((string?)null, table);
    }

    private static async Task<Created<GroupResponse>> ArriveAsync(
        ArrivalRequest request, RestaurantService restaurant, CancellationToken cancellationToken)
    {
        var group = await restaurant.ArriveAsync(request.Size, cancellationToken);
        return TypedResults.Created((string?)null, group);
    }
}
