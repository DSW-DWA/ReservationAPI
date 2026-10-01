using RestaurantSeating.Api.Domain;
using RestaurantSeating.Api.Exceptions;
using RestaurantSeating.Api.Models.Entities;

namespace RestaurantSeating.UnitTests;

public sealed class SeatingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static GuestGroup Group(long id, int size) => new(id, size, Now);

    [Fact]
    public void Groups_can_share_a_table_without_exceeding_capacity()
    {
        var first = Group(1, 2);
        first.Seat(1, Now);
        var second = Group(2, 3);
        var third = Group(3, 2);
        Seating.ProcessQueue([new(1, 6)], [first, second, third], Now);
        Assert.Equal(1, second.TableId);
        Assert.Equal(GroupStatus.Waiting, third.Status);
    }

    [Fact]
    public void Empty_table_has_priority_even_when_shared_table_is_a_tighter_fit()
    {
        var first = Group(1, 2);
        first.Seat(1, Now);
        var second = Group(2, 2);
        Seating.ProcessQueue([new(1, 4), new(2, 6)], [first, second], Now);
        Assert.Equal(2, second.TableId);
    }

    [Fact]
    public void Smallest_suitable_empty_table_is_chosen_then_lowest_id()
    {
        var group = Group(1, 2);
        Seating.ProcessQueue([new(1, 6), new(3, 2), new(2, 2)], [group], Now);
        Assert.Equal(2, group.TableId);
    }

    [Fact]
    public void Shared_table_with_least_free_space_is_chosen_then_lowest_id()
    {
        var first = Group(1, 1);
        first.Seat(1, Now);
        var second = Group(2, 2);
        second.Seat(2, Now);
        var third = Group(3, 2);
        third.Seat(3, Now);
        var arrival = Group(4, 2);
        Seating.ProcessQueue([new(3, 4), new(1, 4), new(2, 4)], [first, second, third, arrival], Now);
        Assert.Equal(2, arrival.TableId);
    }

    [Fact]
    public void Group_is_not_split_across_tables()
    {
        var group = Group(1, 3);
        Seating.ProcessQueue([new(1, 2), new(2, 2)], [group], Now);
        Assert.Equal(GroupStatus.Waiting, group.Status);
        Assert.Null(group.TableId);
    }

    [Fact]
    public void Queue_skips_non_fitting_groups_and_uses_id_even_with_identical_timestamps()
    {
        var large = Group(1, 6);
        var small = Group(2, 2);
        var later = Group(3, 2);
        Seating.ProcessQueue([new(1, 2)], [later, small, large], Now);
        Assert.Equal(GroupStatus.Waiting, large.Status);
        Assert.Equal(GroupStatus.Seated, small.Status);
        Assert.Equal(GroupStatus.Waiting, later.Status);
    }

    [Fact]
    public void Seated_group_is_never_moved_to_a_newly_empty_table()
    {
        var group = Group(1, 1);
        group.Seat(2, Now);
        Assert.Empty(Seating.ProcessQueue([new(1, 2), new(2, 6)], [group], Now.AddMinutes(1)));
        Assert.Equal(2, group.TableId);
        Assert.Throws<StateConflictException>(() => group.Seat(1, Now));
    }

}
