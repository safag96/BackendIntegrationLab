using BackendIntegrationLab.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendIntegrationLab.Api.Data;

// One concrete repository keeps database details out of controllers and business rules.
public sealed class BankingRepository(BankingDbContext database)
{
    public Task<Account?> FindAccountAsync(Guid id, CancellationToken ct) =>
        database.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAccountAsync(Account account, CancellationToken ct)
    {
        database.Accounts.Add(account);
        await database.SaveChangesAsync(ct);
    }

    public Task<List<Transfer>> GetTransfersAsync(CancellationToken ct) =>
        database.Transfers.AsNoTracking().OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).ToListAsync(ct);

    public async Task<Transfer> TransferAsync(
        Guid sourceId, Guid destinationId, decimal amount,
        Action<Account, Account> validate, CancellationToken ct)
    {
        // Read, validate, debit, credit and history all belong to ONE transaction.
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var source = await FindAccountAsync(sourceId, ct)
            ?? throw new Services.AccountNotFoundException(sourceId);
        var destination = await FindAccountAsync(destinationId, ct)
            ?? throw new Services.AccountNotFoundException(destinationId);
        validate(source, destination);

        source.Balance -= amount;
        await database.SaveChangesAsync(ct); // Debit reaches SQLite, but is NOT committed.

        destination.Balance += amount;
        var transfer = new Transfer
        {
            SourceAccountId = sourceId,
            DestinationAccountId = destinationId,
            Amount = amount
        };
        database.Transfers.Add(transfer);
        await database.SaveChangesAsync(ct); // Test-only SQLite trigger can reject this credit.

        await transaction.CommitAsync(ct);
        return transfer;
        // If anything throws before CommitAsync, await using disposes the transaction
        // and SQLite rolls back BOTH SaveChanges calls, including the earlier debit.
    }
}
