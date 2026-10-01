using Microsoft.EntityFrameworkCore;
using RestaurantSeating.Api.Data;
using RestaurantSeating.Api.Domain;
using RestaurantSeating.Api.Exceptions;
using RestaurantSeating.Api.Models.Entities;
using RestaurantSeating.Api.Models.Responses;

namespace RestaurantSeating.Api;

public sealed class RestaurantService(RestaurantDbContext db, TimeProvider clock, ILogger<RestaurantService> logger)
{
    public async Task<TableResponse> AddTableAsync(int capacity, CancellationToken cancellationToken)
    {
        var (table, seated) = await ExecuteInTransactionAsync(write: true, async () =>
        {
            var added = new RestaurantTable(0, capacity);
            db.Tables.Add(added);
            await db.SaveChangesAsync(cancellationToken);
            var seatedGroups = await SeatWaitingGroupsAsync(clock.GetUtcNow(), cancellationToken);
            return (added, seatedGroups);
        }, cancellationToken);

        logger.LogInformation("table.add id={TableId} capacity={Capacity}", table.Id, table.Capacity);
        LogSeating(seated);
        var occupied = seated.Where(g => g.TableId == table.Id).Sum(g => g.Size);
        return new TableResponse(table.Id, table.Capacity, occupied, table.Capacity - occupied);
    }

    public async Task<GroupResponse> ArriveAsync(int size, CancellationToken cancellationToken)
    {
        var (group, seated) = await ExecuteInTransactionAsync(write: true, async () =>
        {
            var now = clock.GetUtcNow();
            var arrival = new GuestGroup(0, size, now);
            db.Groups.Add(arrival);
            await db.SaveChangesAsync(cancellationToken);
            var seatedGroups = await SeatWaitingGroupsAsync(now, cancellationToken);
            return (arrival, seatedGroups);
        }, cancellationToken);

        logger.LogInformation("group.add id={GroupId} size={Size} status={Status}", group.Id, size, group.Status);
        LogSeating(seated);
        return GroupResponse.From(group);
    }

    public async Task<RestaurantResponse> GetStateAsync(CancellationToken cancellationToken, long? historyBeforeId = null)
    {
        var (tables, groups, nextHistoryBeforeId) = await ExecuteInTransactionAsync(write: false, async () =>
        {
            var tables = await db.Tables.AsNoTracking().OrderBy(t => t.Id).ToListAsync(cancellationToken);
            var active = await ActiveGroups().AsNoTracking().ToListAsync(cancellationToken);
            var history = await db.Groups.AsNoTracking()
                .Where(g => g.Status == GroupStatus.Left || g.Status == GroupStatus.Completed)
                .Where(g => historyBeforeId == null || g.Id < historyBeforeId)
                .OrderByDescending(g => g.Id).Take(101).ToListAsync(cancellationToken);
            long? nextCursor = history.Count > 100 ? history[99].Id : null;
            var groups = active.Concat(history.Take(100)).OrderBy(g => g.Id).ToList();
            return (tables, groups, nextCursor);
        }, cancellationToken);

        var occupiedByTable = groups
            .Where(g => g.Status == GroupStatus.Seated)
            .GroupBy(g => g.TableId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(group => group.Size));

        var tableResponses = tables.Select(table =>
        {
            var occupied = occupiedByTable.GetValueOrDefault(table.Id);
            return new TableResponse(table.Id, table.Capacity, occupied, table.Capacity - occupied);
        }).ToList();
        return new RestaurantResponse(tableResponses, groups.Select(GroupResponse.From).ToList(), nextHistoryBeforeId);
    }

    public Task<GroupResponse> LeaveAsync(long id, CancellationToken cancellationToken) =>
        EndVisitAsync(id, complete: false, cancellationToken);

    public Task<GroupResponse> CompleteAsync(long id, CancellationToken cancellationToken) =>
        EndVisitAsync(id, complete: true, cancellationToken);

    private async Task<GroupResponse> EndVisitAsync(long id, bool complete, CancellationToken cancellationToken)
    {
        var (group, newlySeated) = await ExecuteInTransactionAsync(write: true, async () =>
        {
            var group = await db.Groups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken)
                ?? throw new GroupNotFoundException(id);
            var now = clock.GetUtcNow();
            if (complete)
                group.Complete(now);
            else
                group.Leave(now);

            await db.SaveChangesAsync(cancellationToken);
            IReadOnlyList<GuestGroup> seated = complete
                ? await SeatWaitingGroupsAsync(now, cancellationToken)
                : [];
            return (group, seated);
        }, cancellationToken);
        logger.LogInformation("group.update id={GroupId} status={Status}", group.Id, group.Status);
        LogSeating(newlySeated);
        return GroupResponse.From(group);
    }

    private async Task<IReadOnlyList<GuestGroup>> SeatWaitingGroupsAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tables = await db.Tables.AsNoTracking().OrderBy(t => t.Id).ToListAsync(cancellationToken);
        var groups = await ActiveGroups().ToListAsync(cancellationToken);
        var seated = Seating.ProcessQueue(tables, groups, now);
        await db.SaveChangesAsync(cancellationToken);
        return seated;
    }

    private async Task<T> ExecuteInTransactionAsync<T>(
        bool write, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(write, cancellationToken);
        var result = await operation();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private void LogSeating(IReadOnlyList<GuestGroup> groups)
    {
        foreach (var group in groups)
            logger.LogInformation("group.seat id={GroupId} table={TableId}", group.Id, group.TableId);
    }

    private IQueryable<GuestGroup> ActiveGroups() => db.Groups
        .Where(g => g.Status == GroupStatus.Waiting || g.Status == GroupStatus.Seated)
        .OrderBy(g => g.Id);
}
