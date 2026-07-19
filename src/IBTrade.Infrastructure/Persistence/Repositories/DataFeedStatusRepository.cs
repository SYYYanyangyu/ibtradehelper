using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

public class DataFeedStatusRepository(AppDbContext db) : IDataFeedStatusRepository
{
    public async Task<IReadOnlyList<DataFeedStatus>> GetAllAsync(CancellationToken ct = default) =>
        await db.DataFeedStatuses.AsNoTracking().OrderBy(x => x.Feed).ThenBy(x => x.Environment).ToListAsync(ct);

    public async Task UpsertAsync(DataFeedStatus status, CancellationToken ct = default)
    {
        var env = status.Environment ?? "";
        var existing = await db.DataFeedStatuses
            .FirstOrDefaultAsync(x => x.Feed == status.Feed && (x.Environment ?? "") == env, ct);

        if (existing is null)
        {
            status.Environment = env; // Gateway 用 ""，避免 PG 唯一索引对 NULL 不去重
            db.DataFeedStatuses.Add(status);
            return;
        }

        existing.LastSuccessUtc = status.LastSuccessUtc;
        existing.AccountId = status.AccountId;
        existing.ConnectedPort = status.ConnectedPort;
        existing.Message = status.Message;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
