using System.Net;
using System.Net.Http.Json;
using BackendIntegrationLab.Api.Contracts;
using BackendIntegrationLab.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace BackendIntegrationLab.IntegrationTests;

[TestFixture]
public sealed class BankingApiTests
{
    private BankingApiFactory factory = null!;
    private HttpClient client = null!;

    [SetUp]
    public void SetUp()
    {
        factory = new BankingApiFactory();
        // Starts the REAL Program/DI/middleware/controllers using an in-process server.
        client = factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public async Task CreateAccount_ValidRequest_Returns201Created()
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Alice", 500.25m));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
        Assert.Multiple(() =>
        {
            Assert.That(account.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(account.OwnerName, Is.EqualTo("Alice"));
            Assert.That(account.Balance, Is.EqualTo(500.25m));
            Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo($"/api/accounts/{account.Id}"));
        });

        // A fresh DI scope/DbContext proves persistence, not merely a response DTO
        // or an entity still sitting in the request's EF change tracker.
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var saved = await database.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.Multiple(() =>
        {
            Assert.That(saved.OwnerName, Is.EqualTo("Alice"));
            Assert.That(saved.Balance, Is.EqualTo(500.25m));
        });
    }

    [Test]
    public async Task GetAccount_ExistingAccount_Returns200()
    {
        var created = await CreateAccount("Alice", 250m);
        using var response = await client.GetAsync($"/api/accounts/{created.Id}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.That(account, Is.EqualTo(created));
    }

    [Test]
    public async Task TransferMoney_ValidTransfer_UpdatesBothBalances()
    {
        var source = await CreateAccount("Alice", 500m);
        var destination = await CreateAccount("Bob", 50m);

        using var response = await client.PostAsJsonAsync("/api/transfers",
            new CreateTransferRequest(source.Id, destination.Id, 100m));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var transfer = (await response.Content.ReadFromJsonAsync<TransferResponse>())!;
        await AssertBalances(source.Id, 400m, destination.Id, 150m);
        var history = await GetHistory();
        Assert.Multiple(() =>
        {
            Assert.That(transfer.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(transfer.SourceAccountId, Is.EqualTo(source.Id));
            Assert.That(transfer.DestinationAccountId, Is.EqualTo(destination.Id));
            Assert.That(transfer.Amount, Is.EqualTo(100m));
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history.Single(), Is.EqualTo(transfer));
        });
    }

    [Test]
    public async Task TransferMoney_InsufficientFunds_Returns400()
    {
        var source = await CreateAccount("Alice", 50m);
        var destination = await CreateAccount("Bob", 20m);
        using var response = await client.PostAsJsonAsync("/api/transfers",
            new CreateTransferRequest(source.Id, destination.Id, 100m));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.That(problem!.Detail, Is.EqualTo("Insufficient funds."));
        await AssertBalances(source.Id, 50m, destination.Id, 20m);
        Assert.That(await GetHistory(), Is.Empty);
    }

    [Test]
    public async Task TransferMoney_InvalidDestination_Returns404()
    {
        var source = await CreateAccount("Alice", 500m);
        using var response = await client.PostAsJsonAsync("/api/transfers",
            new CreateTransferRequest(source.Id, Guid.NewGuid(), 100m));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await GetAccount(source.Id)).Balance, Is.EqualTo(500m));
        Assert.That(await GetHistory(), Is.Empty);
    }

    [Test]
    public async Task TransferMoney_DatabaseFailure_RollsBack()
    {
        var source = await CreateAccount("Alice", 500m);
        var destination = await CreateAccount("Bob", 50m);

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            // TEST-ONLY fault injection, installed directly in this test's private file.
            // This trigger fails ONLY when the recipient is credited AND the sender's
            // debit has already reached SQLite inside the same transaction.
            // EF's SQLite provider stores GUIDs as uppercase text.
            var sourceId = source.Id.ToString().ToUpperInvariant();
            var destinationId = destination.Id.ToString().ToUpperInvariant();
            var sql = $"""
                CREATE TRIGGER FailRecipientCredit
                BEFORE UPDATE OF Balance ON Accounts
                WHEN NEW.Id = '{destinationId}'
                  AND NEW.Balance > OLD.Balance
                  AND (SELECT Balance FROM Accounts WHERE Id = '{sourceId}') = 40000
                BEGIN
                    SELECT RAISE(ABORT, 'LAB_CONTROLLED_CREDIT_FAILURE_AFTER_DEBIT');
                END;
                """;
            // Only locally generated GUIDs are interpolated; no user input enters SQL.
            await database.Database.ExecuteSqlRawAsync(sql);
        }

        using var response = await client.PostAsJsonAsync("/api/transfers",
            new CreateTransferRequest(source.Id, destination.Id, 100m));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(factory.Errors.Exceptions.Any(error =>
            error.ToString().Contains("LAB_CONTROLLED_CREDIT_FAILURE_AFTER_DEBIT")), Is.True,
            "The failure must be our SQLite trigger AFTER the debit, not an unrelated HTTP 500.");

        // These are NEW HTTP requests and new DbContexts, reading committed SQLite state.
        await AssertBalances(source.Id, 500m, destination.Id, 50m);
        Assert.That(await GetHistory(), Is.Empty, "Failed transfers must leave no history row.");
    }

    [TestCase("Alice", "-1")]
    [TestCase("Alice", "1.001")]
    [TestCase(" ", "100")]
    public async Task CreateAccount_InvalidRequest_Returns400(string name, string balance)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest(name, decimal.Parse(balance, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var scope = factory.Services.CreateScope();
        Assert.That(await scope.ServiceProvider.GetRequiredService<BankingDbContext>().Accounts.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task TransferMoney_FractionalCents_PreservesExactBalances()
    {
        var source = await CreateAccount("Alice", 1.01m);
        var destination = await CreateAccount("Bob", 0.02m);
        using var response = await client.PostAsJsonAsync("/api/transfers",
            new CreateTransferRequest(source.Id, destination.Id, 0.01m));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        await AssertBalances(source.Id, 1.00m, destination.Id, 0.03m);
    }

    private async Task<AccountResponse> CreateAccount(string name, decimal balance)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest(name, balance));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private async Task<AccountResponse> GetAccount(Guid id) =>
        (await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{id}"))!;

    private async Task<List<TransferResponse>> GetHistory() =>
        (await client.GetFromJsonAsync<List<TransferResponse>>("/api/transfers"))!;

    private async Task AssertBalances(Guid sourceId, decimal sourceBalance, Guid destinationId, decimal destinationBalance)
    {
        var source = await GetAccount(sourceId);
        var destination = await GetAccount(destinationId);
        Assert.Multiple(() =>
        {
            Assert.That(source.Balance, Is.EqualTo(sourceBalance), "Source balance");
            Assert.That(destination.Balance, Is.EqualTo(destinationBalance), "Destination balance");
        });
    }
}
