using RestaurantSeating.Api.Models.Entities;

namespace RestaurantSeating.Api.Domain;

public static class Seating
{
    public static IReadOnlyList<GuestGroup> ProcessQueue(
        IReadOnlyList<RestaurantTable> tables, IReadOnlyList<GuestGroup> groups, DateTimeOffset now)
    {
        var occupied = tables.ToDictionary(t => t.Id, _ => 0);
        foreach (var group in groups.Where(g => g.Status == GroupStatus.Seated))
            occupied[group.TableId!.Value] += group.Size;

        var newlySeated = new List<GuestGroup>();
        foreach (var group in groups.Where(g => g.Status == GroupStatus.Waiting).OrderBy(g => g.Id))
        {
            var table = tables
                .Where(t => t.Capacity - occupied[t.Id] >= group.Size)
                .OrderBy(t => occupied[t.Id] == 0 ? 0 : 1)
                .ThenBy(t => t.Capacity - occupied[t.Id])
                .ThenBy(t => t.Id)
                .FirstOrDefault();

            if (table is null)
                continue;

            group.Seat(table.Id, now);
            occupied[table.Id] += group.Size;
            newlySeated.Add(group);
        }

        return newlySeated;
    }
}
