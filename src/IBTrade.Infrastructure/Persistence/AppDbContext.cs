using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence;

/// <summary>
/// EF Core 数据库上下文：映射领域实体到 PostgreSQL 表。
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<DailySnapshot> DailySnapshots => Set<DailySnapshot>();
    public DbSet<PositionDailySnapshot> PositionDailySnapshots => Set<PositionDailySnapshot>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<RealizedPnl> RealizedPnls => Set<RealizedPnl>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<DataFeedStatus> DataFeedStatuses => Set<DataFeedStatus>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<NotificationHistory> NotificationHistories => Set<NotificationHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 自动应用本程序集内所有 IEntityTypeConfiguration
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
