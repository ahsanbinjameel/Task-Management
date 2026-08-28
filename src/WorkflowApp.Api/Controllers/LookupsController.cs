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

    /// <summary>Active modules: the verification target picker, and the top of the ERP context.</summary>
    [HttpGet("modules")]
    [ProducesResponseType(typeof(IReadOnlyList<ModuleOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Modules([FromQuery] string? search, CancellationToken ct)
        => Ok(await _lookups.ModulesAsync(search, ct));

    /// <summary>
    /// Forms, narrowed to a module. Note the absence of a client parameter and the absence of any
    /// way to add one: the catalog describes the product, not a client's copy of it
    /// (PRODUCT-CORE §5).
    /// </summary>
    [HttpGet("forms")]
    [ProducesResponseType(typeof(IReadOnlyList<FormOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Forms(
        [FromQuery] long? moduleId, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _lookups.FormsAsync(moduleId, search, ct));

    /// <summary>The ways of looking at one form: the form itself, History, Detail/Master Report.</summary>
    [HttpGet("form-surfaces")]
    [ProducesResponseType(typeof(IReadOnlyList<FormSurfaceOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FormSurfaces(
        [FromQuery] long? formId, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _lookups.FormSurfacesAsync(formId, search, ct));
}
