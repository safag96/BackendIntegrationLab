namespace BackendIntegrationLab.Api.Models;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}
