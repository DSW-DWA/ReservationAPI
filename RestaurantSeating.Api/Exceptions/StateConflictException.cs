namespace RestaurantSeating.Api.Exceptions;

public sealed class StateConflictException(string message) : Exception(message);
