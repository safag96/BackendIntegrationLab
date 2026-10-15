using BackendIntegrationLab.Api.Contracts;
using BackendIntegrationLab.Api.Data;
using BackendIntegrationLab.Api.Models;

namespace BackendIntegrationLab.Api.Services;

public sealed class BankingService(BankingRepository repository)
{
    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct)
    {
        BankingRules.ValidateMoney(request.InitialBalance, allowZero: true);
        if (string.IsNullOrWhiteSpace(request.OwnerName))
            throw new BankingRuleException("Owner name is required.");
        var account = new Account { OwnerName = request.OwnerName.Trim(), Balance = request.InitialBalance };
        await repository.AddAccountAsync(account, ct);
        return AccountResponse.From(account);
    }

    public async Task<AccountResponse> GetAccountAsync(Guid id, CancellationToken ct) =>
        AccountResponse.From(await repository.FindAccountAsync(id, ct) ?? throw new AccountNotFoundException(id));

    public async Task<TransferResponse> TransferAsync(CreateTransferRequest request, CancellationToken ct)
    {
        var transfer = await repository.TransferAsync(
            request.SourceAccountId, request.DestinationAccountId, request.Amount,
            (source, destination) => BankingRules.ValidateTransfer(
                source.Id, destination.Id, request.Amount, source.Balance, destination.Balance), ct);
        return TransferResponse.From(transfer);
    }

    public async Task<IReadOnlyList<TransferResponse>> GetTransfersAsync(CancellationToken ct) =>
        (await repository.GetTransfersAsync(ct)).Select(TransferResponse.From).ToList();
}
