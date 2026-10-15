using BackendIntegrationLab.Api.Services;
using NUnit.Framework;

namespace BackendIntegrationLab.UnitTests;

[TestFixture]
public sealed class BankingRulesTests
{
    // These call a pure function directly: no server, HTTP client, or database exists.
    [Test]
    public void ValidateTransfer_InsufficientFunds_ThrowsBusinessRuleException()
    {
        Assert.That(() => BankingRules.ValidateTransfer(Guid.NewGuid(), Guid.NewGuid(), 100m, 50m, 0m),
            Throws.TypeOf<BankingRuleException>().With.Message.EqualTo("Insufficient funds."));
    }

    [Test]
    public void ValidateTransfer_SameAccount_ThrowsBusinessRuleException()
    {
        var accountId = Guid.NewGuid();
        Assert.That(() => BankingRules.ValidateTransfer(accountId, accountId, 10m, 50m, 50m),
            Throws.TypeOf<BankingRuleException>().With.Message.EqualTo("Source and destination must be different accounts."));
    }

    [Test]
    public void ValidateTransfer_EntireBalance_IsAllowed()
    {
        Assert.That(() => BankingRules.ValidateTransfer(Guid.NewGuid(), Guid.NewGuid(), 50m, 50m, 0m),
            Throws.Nothing);
    }

    [Test]
    public void ValidateMoney_FractionOfCent_IsRejected()
    {
        Assert.That(() => BankingRules.ValidateMoney(1.001m),
            Throws.TypeOf<BankingRuleException>().With.Message.EqualTo("Money must have at most two decimal places."));
    }
}
