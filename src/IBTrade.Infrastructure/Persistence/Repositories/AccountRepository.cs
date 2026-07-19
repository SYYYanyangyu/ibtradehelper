using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

public class AccountRepository(AppDbContext db) : IAccountRepository
{
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct = default) =>
        await db.Accounts.AsNoTracking().ToListAsync(ct);

    public async Task<Account?> GetByAccountIdAsync(string accountId, CancellationToken ct = default) =>
        await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId, ct);

    public async Task AddAsync(Account account, CancellationToken ct = default) =>
        await db.Accounts.AddAsync(account, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await db.SaveChangesAsync(ct);
}
