using RestaurantSeating.Api.Exceptions;

namespace RestaurantSeating.Api.Models.Entities;

public enum GroupStatus { Waiting, Seated, Left, Completed }

public sealed record RestaurantTable(int Id, int Capacity);

public sealed class GuestGroup(long id, int size, DateTimeOffset arrivedAt)
{
    public long Id { get; private set; } = id;
    public int Size { get; private set; } = size is >= 1 and <= 6
        ? size : throw new ArgumentOutOfRangeException(nameof(size), "Size must be 1–6.");
    public DateTimeOffset ArrivedAt { get; private set; } = arrivedAt;
    public GroupStatus Status { get; private set; } = GroupStatus.Waiting;
    public int? TableId { get; private set; }
    public DateTimeOffset? SeatedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public void Seat(int tableId, DateTimeOffset now)
    {
        RequireStatus(GroupStatus.Waiting);
        TableId = tableId;
        SeatedAt = now;
        Status = GroupStatus.Seated;
    }

    public void Leave(DateTimeOffset now)
    {
        RequireStatus(GroupStatus.Waiting);
        Status = GroupStatus.Left;
        EndedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        RequireStatus(GroupStatus.Seated);
        Status = GroupStatus.Completed;
        EndedAt = now;
    }

    private void RequireStatus(GroupStatus expected)
    {
        if (Status != expected)
            throw new StateConflictException($"Group {Id}: expected {expected}, actual {Status}.");
    }
}
