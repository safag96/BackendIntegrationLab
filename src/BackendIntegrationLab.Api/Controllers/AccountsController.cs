using BackendIntegrationLab.Api.Contracts;
using BackendIntegrationLab.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BackendIntegrationLab.Api.Controllers;

[ApiController] // Invalid request DTOs automatically produce HTTP 400 validation details.
[Route("api/accounts")]
public sealed class AccountsController(BankingService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AccountResponse>> Create(CreateAccountRequest request, CancellationToken ct)
    {
        var account = await service.CreateAccountAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = account.Id }, account);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await service.GetAccountAsync(id, ct));
}
