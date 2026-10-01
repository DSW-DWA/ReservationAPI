using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSeating.Api.Models.Entities;

namespace RestaurantSeating.Api.Data;

public sealed class RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) : DbContext(options)
{
    public DbSet<RestaurantTable> Tables => Set<RestaurantTable>();
    public DbSet<GuestGroup> Groups => Set<GuestGroup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tables = modelBuilder.Entity<RestaurantTable>();
        tables.ToTable("RestaurantTables");
        tables.HasKey(t => t.Id);
        tables.Property(t => t.Id).UseIdentityColumn();

        var groups = modelBuilder.Entity<GuestGroup>();
        groups.ToTable("GuestGroups");
        groups.HasKey(g => g.Id);
        groups.Property(g => g.Id).UseIdentityColumn();
        groups.Property(g => g.Status).HasConversion<string>().HasColumnType("varchar(16)");
        groups.HasOne<RestaurantTable>().WithMany().HasForeignKey(g => g.TableId)
            .OnDelete(DeleteBehavior.Restrict);
        groups.HasIndex(g => new { g.Status, g.Id })
            .IncludeProperties(g => new { g.Size, g.TableId, g.ArrivedAt, g.SeatedAt, g.EndedAt });
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(bool write, CancellationToken cancellationToken, int lockTimeoutMs = 5000)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var mode = write ? "Exclusive" : "Shared";
            await Database.ExecuteSqlInterpolatedAsync($"""
                SET XACT_ABORT ON;
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = 'RestaurantSeating.State', @LockMode = {mode},
                    @LockOwner = 'Transaction', @LockTimeout = {lockTimeoutMs};
                IF @result < 0 THROW 51006, 'Restaurant lock failed.', 1;
                """, cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var previousTimeout = Database.GetCommandTimeout();
        Database.SetCommandTimeout(90);
        try
        {
            await using var transaction = await BeginTransactionAsync(write: true, cancellationToken, lockTimeoutMs: 60000);
            await Database.MigrateAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            Database.SetCommandTimeout(previousTimeout);
        }
    }
}
