# Learn backend integration testing, one request at a time

## 1. First run

Open PowerShell in this folder (the folder containing `BackendIntegrationLab.sln`):

```powershell
dotnet restore BackendIntegrationLab.sln
dotnet build BackendIntegrationLab.sln --no-restore
dotnet test BackendIntegrationLab.sln --no-build
dotnet run --project src/BackendIntegrationLab.Api --launch-profile http
```

Visit **http://localhost:5080/swagger**. Swagger UI is a browser client for the API.
Expand an endpoint, select **Try it out**, enter JSON and select **Execute**.
Swagger shows the actual response status, headers, body, and an equivalent curl command.
The OpenAPI description is at `http://localhost:5080/swagger/v1/swagger.json`.
No authentication, Docker, cloud account or external database is required.

### Try a complete transfer manually

1. POST `/api/accounts`: `{"ownerName":"Alice","initialBalance":500}`. Expect **201**.
2. POST `/api/accounts`: `{"ownerName":"Bob","initialBalance":50}`. Expect **201**.
3. Copy both returned `id` values. The API generates GUIDs; it does not seed accounts.
4. POST `/api/transfers` with:

   ```json
   {
     "sourceAccountId": "PASTE-ALICE-GUID-HERE",
     "destinationAccountId": "PASTE-BOB-GUID-HERE",
     "amount": 100
   }
   ```

5. GET each `/api/accounts/{id}`. Expect **400** and **150** as balances, both with HTTP **200**.
6. GET `/api/transfers`. Expect a JSON array containing the transfer.
7. Try transferring 1000. Expect HTTP **400**, `Insufficient funds.`, and unchanged balances.

The lab normally uses two accounts, but allows creating more for experiments.
`Banking.http` contains the same requests for Visual Studio's HTTP editor (or a
VS Code HTTP client extension). Replace its GUID variables with your returned IDs.

### The same experiment in PowerShell

Leave the backend running and use a second terminal:

```powershell
$base = 'http://localhost:5080'
$alice = Invoke-RestMethod -Method Post -Uri "$base/api/accounts" -ContentType 'application/json' -Body '{"ownerName":"Alice","initialBalance":500}'
$bob = Invoke-RestMethod -Method Post -Uri "$base/api/accounts" -ContentType 'application/json' -Body '{"ownerName":"Bob","initialBalance":50}'
$transfer = @{ sourceAccountId = $alice.id; destinationAccountId = $bob.id; amount = 100 } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$base/api/transfers" -ContentType 'application/json' -Body $transfer
Invoke-RestMethod -Uri "$base/api/accounts/$($alice.id)"
Invoke-RestMethod -Uri "$base/api/accounts/$($bob.id)"
Invoke-RestMethod -Uri "$base/api/transfers"
```

## 2. What happens when a request arrives?

```text
Browser / HttpClient
  -> ASP.NET Core middleware (exception handling)
  -> routing + JSON model binding + request validation
  -> Controller (HTTP concerns)
  -> Service (business decisions)
  -> Repository (database workflow)
  -> EF Core (objects, change tracking, SQL generation)
  -> SQLite (real tables and transactions)
  -> response DTO -> JSON -> HTTP response
```

* **Program.cs** builds the application, registers dependencies, creates the lab
  database and maps controller routes. `app.Run()` starts request processing.
* **Controllers** select an operation from an HTTP method and URL. ASP.NET Core
  deserializes the JSON body into a request DTO. `[ApiController]` automatically
  returns 400 for invalid annotated input, before calling the action.
* **Services** coordinate business behavior. `BankingRules` contains pure checks:
  enough funds, different accounts, allowed amounts and destination balance limit.
* **Repository** loads and saves entities and owns the transfer transaction. It
  calls a validation callback supplied by the service after loading both accounts.
  This small callback keeps rule code in Services and validation inside the transaction.
* **EF Core** tracks loaded entities. Changing `source.Balance` changes an object
  in memory; `SaveChangesAsync` generates and executes SQL for that change.
* **SQLite** is the database engine. It stores rows in a local file and guarantees
  that an uncommitted transaction can be rolled back.
* **DTOs** define the public JSON contract. The API doesn't expose EF entities directly.

### Dependency injection, without magic

`AddScoped<BankingService>()`, `AddScoped<BankingRepository>()` and `AddDbContext`
tell ASP.NET Core how to create objects. Constructor parameters request those objects.
For each HTTP request a scope provides one service/repository/DbContext instance,
and disposes them when that request finishes. A later GET uses a fresh DbContext.
The exception handler is registered separately using ASP.NET Core's handler infrastructure.

### Trace POST /api/transfers

For Alice=500, Bob=50, amount=100:

1. ASP.NET Core routes the POST to `TransfersController.Create`.
2. JSON becomes `CreateTransferRequest`. Amount must be positive, at most one
   billion, and contain no fractions of a cent.
3. The controller awaits `BankingService.TransferAsync`.
4. The service asks `BankingRepository.TransferAsync` to perform the workflow.
5. Repository begins a transaction and loads Alice and Bob using EF Core.
6. The service's callback invokes `BankingRules.ValidateTransfer` with the loaded balances.
7. Repository subtracts 100 from Alice and calls `SaveChangesAsync`: SQL UPDATE
   reaches SQLite. Alice is 400 **inside this uncommitted transaction**.
8. Repository credits Bob, adds a history entity, and calls `SaveChangesAsync` again.
9. `CommitAsync` makes the entire transaction durable: Alice=400, Bob=150, one history row.
10. Service maps the entity to `TransferResponse`; controller returns **201 Created** with JSON.

The two saves are intentionally visible learning steps. **SaveChanges is not the same
as committing an explicitly started transaction.** Without the surrounding transaction,
each save would commit independently and could leave only half a transfer applied.

### HTTP responses

| Operation/outcome | Status |
| --- | --- |
| Account created | 201, JSON account, Location header pointing to its GET route |
| Existing account or transfer history retrieved | 200 |
| Transfer created | 201, JSON transfer, Location pointing to history collection |
| Invalid DTO or business rule violation | 400 |
| Source/destination account missing | 404 |
| Unexpected database/server failure | 500 |

`ApiExceptionHandler` maps business exceptions to error responses. Business errors
include useful details; unexpected errors are logged and return a generic body.
Invalid DTOs use ASP.NET Core's validation problem response (with an `errors` map).

### Money and local storage

All money values in C#, contracts, rules and tests use **decimal**. SQLite has no
native fixed-precision decimal type, so EF value converters store **integer cents**:
500.25m becomes 50025 in SQLite and converts back to 500.25m when read.
Input with more than two decimal places is rejected instead of silently rounded.

The development file is `src/BackendIntegrationLab.Api/App_Data/banking.db`.
It persists across restarts. To reset it, stop the backend and delete the `App_Data`
folder in File Explorer, then run again. `EnsureCreated` recreates an empty schema.
This lab uses EnsureCreated for simplicity; a schema change requires recreating
the database. EF migrations are the next step for evolving a database while retaining data.

## 3. What WebApplicationFactory does

`BankingApiFactory : WebApplicationFactory<Program>` boots the **actual application**
entry point with a test web host. It uses `TestServer`, an in-process HTTP server.
`factory.CreateClient()` returns an `HttpClient` connected to that server.

`PostAsJsonAsync` serializes a real HTTP request body. The request goes through
ASP.NET Core routing, model binding, validation, controller, service, repository,
EF Core, and SQLite. The response is serialized JSON and an HTTP status code.
The client is not directly calling controller methods.

This avoids opening a TCP port and does not require a separately running backend.
The normal `dotnet run` path uses Kestrel, a network-facing server. These tests
exercise the application HTTP pipeline but do not test TLS, proxies or networking.

| Component | Integration test behavior |
| --- | --- |
| Program, DI, middleware, routing, validation | Real |
| Controllers, service, rules, repository | Real |
| EF Core SQLite provider and SQLite engine | Real |
| Development DbContext configuration | Replaced with an isolated SQLite file connection |
| Kestrel network listener | Replaced by in-process TestServer |
| Logging | Real, with an extra test observer for the injected database error |
| Rollback fault | Test creates one SQLite trigger in its own database only |

There are no mocked repositories, services or database providers.

## 4. How each test stays independent

Every NUnit `[SetUp]` creates a **new factory and GUID-named SQLite file** under
Windows `%TEMP%`, e.g. `BackendIntegrationLab-test-<guid>.db`.
The factory sets environment `Testing`, removes the original DbContext registration
and supplies the test connection before Program resolves the database.
Program's EnsureCreated therefore creates the test schema using the replacement registration.

Each test starts empty and creates its own accounts via HTTP. There is no fixed ID,
shared seed data, required execution order, or shared test transaction. Every HTTP
request can open its own connection to the same per-test file. A filename is never
reused between tests, and the factory never points to the development file.

`[TearDown]` disposes client and host, then deletes that factory's database and
SQLite sidecar files. Connection pooling is disabled for tests to release file handles.
An abruptly terminated debugging session may leave a GUID-named temp file; future
tests still get new filenames and remain independent.

The account-creation test also reads the saved row through a **fresh DI scope and
AsNoTracking DbContext query**, proving the returned DTO corresponds to persisted data.

## 5. Why these are integration tests (and the other tests aren't)

`BankingApiTests` checks cooperation between HTTP serialization, routing, DI,
validation, business logic, EF mappings, SQL execution and transaction behavior.
A successful direct call to a business rule cannot prove those boundaries work.

`BankingRulesTests` directly calls a pure function with values and checks its result
or exception. No host, HTTP request, DI container or actual database is created.
Those are **unit tests**, even though their project references the API assembly.
Test scope is determined by what executes, not by the names of referenced packages.
Both kinds use NUnit: the test framework doesn't determine the test's scope.

For example, a broken controller route can fail integration tests while all unit
tests pass. A broken insufficient-funds rule can fail both kinds.

```powershell
# All tests (10 integration cases and 4 unit tests):
dotnet test BackendIntegrationLab.sln

# Integration only:
dotnet test tests/BackendIntegrationLab.IntegrationTests

# One exact NUnit test:
dotnet test tests/BackendIntegrationLab.IntegrationTests --filter "FullyQualifiedName=BackendIntegrationLab.IntegrationTests.BankingApiTests.TransferMoney_ValidTransfer_UpdatesBothBalances"

# Rollback only:
dotnet test tests/BackendIntegrationLab.IntegrationTests --filter "FullyQualifiedName~TransferMoney_DatabaseFailure_RollsBack"

# Unit only:
dotnet test tests/BackendIntegrationLab.UnitTests
```

Use `--no-build` only when the current source has already been built. After changing
code, use normal `dotnet test` so your edits actually execute.

## 6. The rollback test: fail after the debit

`TransferMoney_DatabaseFailure_RollsBack` creates Alice=500 and Bob=50 over HTTP.
Then it installs a SQLite trigger directly in **its private test database**. The
production API contains no fault endpoint, failure flag, or injected throwing service.

The trigger rejects Bob's balance increase **only if Alice's balance is already
400 in the same database transaction**. Amounts in that SQL are cents (40000).
This ensures the sender's first SaveChanges really executed before the fault.
The trigger uses `RAISE(ABORT, 'LAB_CONTROLLED_CREDIT_FAILURE_AFTER_DEBIT')`, causing
a real SQLite provider exception during the second SaveChanges.

EF can roll back that SaveChanges to its savepoint. The surrounding explicit
transaction must still unwind to undo the **earlier successful SaveChanges**.
The repository's `await using` disposes the uncommitted transaction when the
exception exits the method; SQLite rolls it back. The error handler returns 500.

The test verifies all four facts:

1. The transfer HTTP response is 500.
2. The real logged exception contains the trigger's unique marker. An unrelated
   500 does not count as a successful rollback test.
3. New GET requests read Alice=500 and Bob=50, from new DbContexts/connections.
4. GET history contains no transfer row.

Checking only a 500 would miss lost money. Checking tracked objects in the failed
DbContext would also be misleading: rollback restores **database** state, not the
C# property values of tracked objects. The fresh GETs test what another client sees.

## 7. Debug step by step

### Visual Studio

1. Open `BackendIntegrationLab.sln`, build Debug, then **Test > Test Explorer**.
2. Find **TransferMoney_ValidTransfer_UpdatesBothBalances**. Start here before
   exploring the failure path.
3. Place breakpoints on these executable statements (search by statement/name):
   * `BankingApiTests.cs`: the transfer `client.PostAsJsonAsync` inside that test.
   * `Controllers/TransfersController.cs`: `var transfer = await service.TransferAsync(...)`.
   * `Services/BankingService.cs`: `var transfer = await repository.TransferAsync(...)`.
   * `Services/BankingRules.cs`: `if (sourceBalance < amount)`.
   * `Data/BankingRepository.cs`: `BeginTransactionAsync`, `source.Balance -= amount`,
     both `SaveChangesAsync` calls, and `transaction.CommitAsync`.
4. Right-click the test and choose **Debug**. The API runs inside the test host;
   you don't need to launch the API project separately.
5. Use F10/F11, Locals, Watch and Call Stack. Inspect `request`, `source.Balance`,
   `destination.Balance`, and `database.ChangeTracker.Entries()`.
6. Stop just after the first save: the debit has reached SQLite, but hasn't committed.
7. Continue to commit, then back to the test's new GET requests and assertions.

Next debug `TransferMoney_DatabaseFailure_RollsBack`. Watch the first save succeed
and the second throw. Add a breakpoint in `ApiExceptionHandler.TryHandleAsync`
and inspect the inner SQLite exception. Visual Studio **Debug > Windows > Exception
Settings** can break on thrown `Microsoft.EntityFrameworkCore.DbUpdateException`.
Continue after the intentional exception so the rollback assertions execute.

For manual request debugging, set the API as startup project, choose the `http`
profile and press F5. Send requests through Swagger while controller breakpoints are set.

### VS Code (free Microsoft C# extension)

Open **BackendIntegrationLab itself** as the VS Code folder so the supplied
`.vscode` configurations resolve correctly. Install Microsoft's **C#** extension
(`ms-dotnettools.csharp`). Select **Debug banking API** and press F5 to debug manual requests.

To debug a test without requiring an additional test-explorer extension:

```powershell
dotnet build BackendIntegrationLab.sln
$env:VSTEST_HOST_DEBUG = '1'
try {
    dotnet test tests/BackendIntegrationLab.IntegrationTests --no-build --filter "FullyQualifiedName~TransferMoney_ValidTransfer_UpdatesBothBalances"
} finally {
    Remove-Item Env:VSTEST_HOST_DEBUG
}
```

The test runner prints a **testhost process ID** and waits for a debugger.
Set the breakpoints above, select **Attach to NUnit test host**, press F5, and
choose that exact process ID in the process picker. Continue if paused. The test
then runs through the API code in the same host. The command removes the environment
variable afterwards so future tests do not unexpectedly wait for a debugger.

## 8. Five small break-it exercises

Make one edit at a time, run `dotnet test BackendIntegrationLab.sln`, inspect the
failure, then undo the edit and confirm green before trying the next exercise.

1. **Wrong HTTP contract:** in `AccountsController.Create`, change
   `CreatedAtAction(...)` to `Ok(account)`. Expect
   `CreateAccount_ValidRequest_Returns201Created` to fail (and other tests that
   create accounts via the helper). Unit tests still pass.
2. **Incorrect credit:** change `destination.Balance += amount` to
   `destination.Balance += amount / 2m` in the repository. Expect the valid-transfer
   test to observe Bob=100 instead of 150. The cent precision test also detects
   the incorrect credit/storage effect. Unit rules still pass.
3. **Lost history:** remove `database.Transfers.Add(transfer)` from the repository.
   Balances still change, but `TransferMoney_ValidTransfer_UpdatesBothBalances`
   fails because history is empty.
4. **Wrong error mapping:** map `AccountNotFoundException` to 400 instead of 404 in
   `ApiExceptionHandler`. Expect `TransferMoney_InvalidDestination_Returns404`
   to fail. The business-rule unit tests cannot see HTTP mapping errors.
5. **Lost atomicity:** remove the `BeginTransactionAsync` statement and its
   matching `CommitAsync` statement. Each SaveChanges now commits separately.
   Expect `TransferMoney_DatabaseFailure_RollsBack` to detect Alice stuck at 400.
   Happy-path tests may still pass, which shows why a failure-path test matters.

## Suggested learning order

1. Run Swagger and manually create/transfer/read.
2. Read `AccountsController` and debug the account-creation test.
3. Debug the successful transfer test across all layers.
4. Compare that call stack with a pure `BankingRulesTests` call.
5. Debug the rollback test, then try the five exercises.
