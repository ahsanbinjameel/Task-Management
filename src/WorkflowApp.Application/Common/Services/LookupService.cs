using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Domain.Entities.Requests;

namespace WorkflowApp.Application.Common.Services;

/// <summary>A known client: the name for the type-ahead, the id for the filter.</summary>
/// <summary>A module for the picker: the name for the type-ahead, the id for the column.</summary>
public sealed record ModuleOptionDto(long Id, string Name, string? ProjectName);

public sealed record ClientOptionDto(long Id, string Name);

/// <summary>A form for the picker. The module comes with it, because "Adjustment" alone is ambiguous.</summary>
public sealed record FormOptionDto(long Id, string Name, long ModuleId, string ModuleName);

/// <summary>A surface for the picker, carrying its form so the choice reads in full.</summary>
public sealed record FormSurfaceOptionDto(long Id, string Name, long FormId, string FormName);


/// <summary>
/// Client names, for the type-ahead on the request form.
///
/// There is deliberately no screen for managing clients. A name is typed once and remembered, so
/// the list builds itself from real use and the next person picks the existing spelling instead of
/// inventing a new one. Maintaining a separate client register for a field this small would cost
/// more than it is worth.
///
/// Not permission-gated beyond being signed in: anyone who can raise a request has to be able to
/// say who it is for, and the list is nothing but names.
/// </summary>
public interface ILookupService
{
    /// <summary>Known clients, alphabetical, optionally narrowed by what has been typed.</summary>
    Task<IReadOnlyList<ClientOptionDto>> ClientsAsync(string? search, CancellationToken ct = default);

    /// <summary>
    /// Turns a typed name into a client id, creating the client the first time a name is used.
    /// Matching ignores case and surrounding space, so "abc company" and "ABC Company " land on the
    /// same record rather than quietly forking it.
    ///
    /// Returns null for blank input — the field is optional and "no client" is a real answer.
    /// Staged, not saved: the caller commits it with the request that referenced it.
    /// </summary>
    Task<long?> ResolveClientAsync(string? name, CancellationToken ct = default);

    /// <summary>
    /// Active modules, alphabetical. Needed because a verification can target one by real foreign
    /// key, and a picker is the only way that column gets a valid value from a human.
    ///
    /// Deliberately not a create-on-type list like clients: a module is part of a project's
    /// structure that an administrator maintains, not a label somebody invents at the point of use.
    /// </summary>
    Task<IReadOnlyList<ModuleOptionDto>> ModulesAsync(string? search, CancellationToken ct = default);

    /// <summary>
    /// Forms, optionally narrowed to one module. The narrowing is by <em>module</em> and never by
    /// client — the catalog describes the product, not any one client's copy of it
    /// (PRODUCT-CORE §5).
    /// </summary>
    Task<IReadOnlyList<FormOptionDto>> FormsAsync(
        long? moduleId, string? search, CancellationToken ct = default);

    /// <summary>The ways of looking at a form: the form itself, History, Detail/Master Report.</summary>
    Task<IReadOnlyList<FormSurfaceOptionDto>> FormSurfacesAsync(
        long? formId, string? search, CancellationToken ct = default);
}

public sealed class LookupService : ILookupService
{
    private readonly IWorkflowDbContext _db;

    public LookupService(IWorkflowDbContext db) => _db = db;

    public async Task<IReadOnlyList<FormOptionDto>> FormsAsync(
        long? moduleId, string? search, CancellationToken ct = default)
    {
        var query = _db.Forms.AsNoTracking().Where(f => f.IsActive);

        if (moduleId is { } id) query = query.Where(f => f.ModuleId == id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(f => f.Name.Contains(term));
        }

        return await query
            .Join(_db.Modules.AsNoTracking(), f => f.ModuleId, m => m.Id, (f, m) => new { f, m })
            .OrderBy(x => x.m.Name).ThenBy(x => x.f.Name)
            .Select(x => new FormOptionDto(x.f.Id, x.f.Name, x.f.ModuleId, x.m.Name))
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FormSurfaceOptionDto>> FormSurfacesAsync(
        long? formId, string? search, CancellationToken ct = default)
    {
        var query = _db.FormSurfaces.AsNoTracking().Where(s => s.IsActive);

        if (formId is { } id) query = query.Where(s => s.FormId == id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s => s.Name.Contains(term));
        }

        return await query
            .Join(_db.Forms.AsNoTracking(), s => s.FormId, f => f.Id, (s, f) => new { s, f })
            .OrderBy(x => x.f.Name).ThenBy(x => x.s.Name)
            .Select(x => new FormSurfaceOptionDto(x.s.Id, x.s.Name, x.s.FormId, x.f.Name))
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ModuleOptionDto>> ModulesAsync(
        string? search, CancellationToken ct = default)
    {
        var query = _db.Modules.AsNoTracking().Where(m => m.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.Name.Contains(term));
        }

        return await query
            .OrderBy(m => m.Name)
            .Select(m => new ModuleOptionDto(
                m.Id,
                m.Name,
                _db.Projects.Where(p => p.Id == m.ProjectId).Select(p => p.Name).FirstOrDefault()))
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ClientOptionDto>> ClientsAsync(
        string? search, CancellationToken ct = default)
    {
        var query = _db.Clients.AsNoTracking().Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.Name.Contains(term));
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new ClientOptionDto(c.Id, c.Name))
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<long?> ResolveClientAsync(string? name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var trimmed = name.Trim();

        var existing = await _db.Clients
            .FirstOrDefaultAsync(c => c.Name.ToLower() == trimmed.ToLower(), ct);

        if (existing is not null) return existing.Id;

        var created = new Client { Name = trimmed, IsActive = true };
        _db.Clients.Add(created);

        // Saved here rather than left staged: the id is needed immediately by the caller, and a
        // client name is harmless on its own if the request that prompted it then fails.
        await _db.SaveChangesAsync(ct);
        return created.Id;
    }
}
