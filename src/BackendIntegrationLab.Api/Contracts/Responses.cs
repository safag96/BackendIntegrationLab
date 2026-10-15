using BackendIntegrationLab.Api.Models;

namespace BackendIntegrationLab.Api.Contracts;

public sealed record AccountResponse(Guid Id, string OwnerName, decimal Balance)
{
    public static AccountResponse From(Account account) =>
        new(account.Id, account.OwnerName, account.Balance);
}

public sealed record TransferResponse(
    Guid Id, Guid SourceAccountId, Guid DestinationAccountId, decimal Amount, DateTime CreatedAtUtc)
{
    public static TransferResponse From(Transfer transfer) => new(
        transfer.Id, transfer.SourceAccountId, transfer.DestinationAccountId,
        transfer.Amount, transfer.CreatedAtUtc);
}
