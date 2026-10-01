using System.ComponentModel.DataAnnotations;

namespace RestaurantSeating.Api.Models.Requests;

public sealed class ArrivalRequest
{
    [Range(1, 6, ErrorMessage = "Size must be 1–6.")]
    public int Size { get; init; }
}

public sealed class AddTableRequest
{
    [Range(2, 6, ErrorMessage = "Capacity must be 2–6.")]
    public int Capacity { get; init; }
}
