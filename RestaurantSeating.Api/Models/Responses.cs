using RestaurantSeating.Api.Models.Entities;

namespace RestaurantSeating.Api.Models.Responses;

public sealed record GroupResponse(long Id, int Size, GroupStatus Status, int? TableId,
    DateTimeOffset ArrivedAt, DateTimeOffset? SeatedAt, DateTimeOffset? EndedAt)
{
    public static GroupResponse From(GuestGroup group) => new(group.Id, group.Size, group.Status,
        group.TableId, group.ArrivedAt, group.SeatedAt, group.EndedAt);
}

public sealed record TableResponse(int Id, int Capacity, int OccupiedSeats, int AvailableSeats);

public sealed record RestaurantResponse(IReadOnlyList<TableResponse> Tables,
    IReadOnlyList<GroupResponse> Groups, long? NextHistoryBeforeId = null);
