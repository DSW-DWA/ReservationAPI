namespace RestaurantSeating.Api.Exceptions;

public sealed class GroupNotFoundException(long id) : Exception($"Group {id} not found.");
