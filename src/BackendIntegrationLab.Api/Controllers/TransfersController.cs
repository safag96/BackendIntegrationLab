using BackendIntegrationLab.Api.Contracts;
using BackendIntegrationLab.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BackendIntegrationLab.Api.Controllers;

[ApiController]
[Route("api/transfers")]
public sealed class TransfersController(BankingService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransferResponse>> Create(CreateTransferRequest request, CancellationToken ct)
    {
        var transfer = await service.TransferAsync(request, ct);
        return Created("/api/transfers", transfer);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TransferResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TransferResponse>>> Get(CancellationToken ct) =>
        Ok(await service.GetTransfersAsync(ct));
}
