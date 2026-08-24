using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Domain.Entities.Requests;

namespace WorkflowApp.Application.Common.Services;

/// <summary>A known client: the name for the type-ahead, the id for the filter.</summary>
public sealed record ClientOptionDto(long Id, string Name);


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

}

public sealed class LookupService : ILookupService
{
    private readonly IWorkflowDbContext _db;

    public LookupService(IWorkflowDbContext db) => _db = db;

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
