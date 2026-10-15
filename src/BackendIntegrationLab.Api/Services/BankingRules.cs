namespace BackendIntegrationLab.Api.Services;

public sealed class BankingRuleException(string message) : Exception(message);
public sealed class AccountNotFoundException(Guid id) : Exception($"Account '{id}' was not found.");

// Pure business rules: no HTTP, dependency injection, EF Core or database needed.
public static class BankingRules
{
    public const decimal MaximumBalance = 1_000_000_000m;

    public static void ValidateMoney(decimal amount, bool allowZero = false)
    {
        if (amount < 0m || (!allowZero && amount == 0m) || amount > MaximumBalance)
            throw new BankingRuleException("Amount is outside the allowed range.");
        if (decimal.Round(amount, 2) != amount)
            throw new BankingRuleException("Money must have at most two decimal places.");
    }

    public static void ValidateTransfer(
        Guid sourceId, Guid destinationId, decimal amount, decimal sourceBalance, decimal destinationBalance)
    {
        ValidateMoney(amount);
        if (sourceId == destinationId)
            throw new BankingRuleException("Source and destination must be different accounts.");
        if (sourceBalance < amount)
            throw new BankingRuleException("Insufficient funds.");
        if (destinationBalance + amount > MaximumBalance)
            throw new BankingRuleException("Destination balance would exceed the allowed maximum.");
    }
}
