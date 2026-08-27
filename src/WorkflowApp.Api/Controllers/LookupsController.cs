using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common.Services;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// Suggestions for the client field. Signed in is enough — see <see cref="ILookupService"/> for why.
/// </summary>
[Route("api/lookups")]
public sealed class LookupsController : ApiControllerBase
{
    private readonly ILookupService _lookups;

    public LookupsController(ILookupService lookups) => _lookups = lookups;

    /// <summary>Known clients: the name for the type-ahead, the id for the list filter.</summary>
    [HttpGet("clients")]
    [ProducesResponseType(typeof(IReadOnlyList<ClientOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Clients([FromQuery] string? search, CancellationToken ct)
        => Ok(await _lookups.ClientsAsync(search, ct));

    /// <summary>Active modules, for the verification target picker.</summary>
    [HttpGet("modules")]
    [ProducesResponseType(typeof(IReadOnlyList<ModuleOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Modules([FromQuery] string? search, CancellationToken ct)
        => Ok(await _lookups.ModulesAsync(search, ct));
}
