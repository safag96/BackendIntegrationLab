using System.ComponentModel.DataAnnotations;

namespace BackendIntegrationLab.Api.Contracts;

public sealed record CreateAccountRequest(
    [Required, StringLength(100, MinimumLength = 1)] string OwnerName,
    [Range(typeof(decimal), "0", "1000000000"), MoneyPrecision] decimal InitialBalance);

public sealed record CreateTransferRequest(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    [Range(typeof(decimal), "0.01", "1000000000"), MoneyPrecision] decimal Amount);

// Reject fractions of a cent rather than silently rounding the customer's money.
public sealed class MoneyPrecisionAttribute : ValidationAttribute
{
    public MoneyPrecisionAttribute() : base("Money must have at most two decimal places.") { }
    public override bool IsValid(object? value) =>
        value is decimal amount && decimal.Round(amount, 2) == amount;
}
