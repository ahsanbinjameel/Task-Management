using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Demo;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Infrastructure.Persistence;
using WorkflowApp.Infrastructure.Persistence.Seed;

namespace WorkflowApp.Infrastructure.Demo;

/// <summary>
/// The demo catalog, built and torn down by the same migrations that build the live one.
///
/// It opens its own <see cref="WorkflowDbContext"/> against the demo connection rather than taking
/// the request-scoped one, because the request-scoped one points at whichever catalog the caller is
/// already in — and both of the things this class does happen from the other side: preparing a
/// demonstration from a live session, and resetting one from inside a demo session.
///
/// The cast is one account per role in the workflow, because the workflow is what a demonstration is
/// for. Watching a request travel from Faisal to Ahsan to Hanzala to Uzair is the product; a single
/// account with every permission would show all the screens and none of the point.
/// </summary>
public sealed class DemoEnvironment : IDemoEnvironment
{
    /// <summary>
    /// The cast, in workflow order. Names are obviously fictional on purpose: nobody should be able
    /// to mistake a demonstration for a record of something a real colleague did.
    /// </summary>
    private static readonly (string UserName, string DisplayName, string Role, string Purpose)[] Cast =
    {
        ("demo.requester", "Rina (Requester)", DefaultRoles.Requester,
            "Asks for work and follows it without chasing anyone"),
        ("demo.reviewer", "Rehan (Reviewer)", DefaultRoles.Reviewer,
            "Decides what actually needs doing, and approves the work"),
        ("demo.coordinator", "Coral (Coordinator)", DefaultRoles.AssignmentManager,
            "Gives work out, and watches who is carrying what"),
        ("demo.worker", "Wasim (Worker)", DefaultRoles.Worker,
            "Does the work, on the clock, one task at a time"),
        ("demo.qc", "Qadir (Quality)", DefaultRoles.QC,
            "Checks finished work against what was asked for"),
        ("demo.manager", "Mira (Management)", DefaultRoles.Management,
            "Reads the dashboards and the reports"),
    };

    /// <summary>The one password every demo account shares. It only ever opens the demo catalog.</summary>
    private const string DemoPassword = "Demo!2026";

    private readonly string? _connectionString;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DemoEnvironment> _logger;

    public DemoEnvironment(
        IConfiguration configuration,
        IPasswordHasher hasher,
        IDateTimeProvider clock,
        IOptions<AuthOptions> authOptions,
        ILoggerFactory loggerFactory,
        ILogger<DemoEnvironment> logger)
    {
        _connectionString = configuration.GetConnectionString("Demo") ?? Derive(configuration);
        _hasher = hasher;
        _clock = clock;
        _authOptions = authOptions;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>The same seeder the live catalog uses — permissions, roles, grants, pause reasons.</summary>
    private DatabaseSeeder SeederFor(WorkflowDbContext db) =>
        new(db, _hasher, _authOptions, _loggerFactory.CreateLogger<DatabaseSeeder>());

    public bool IsConfigured => _connectionString is not null;

    private static string? Derive(IConfiguration configuration)
    {
        var live = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(live)) return null;

        try
        {
            var builder = new SqlConnectionStringBuilder(live);
            if (string.IsNullOrWhiteSpace(builder.InitialCatalog)) return null;

            builder.InitialCatalog += "_Demo";
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A context on the demo catalog, and only ever on the demo catalog.</summary>
    private WorkflowDbContext Open()
    {
        if (_connectionString is null)
            throw new InvalidOperationException("No demo database is configured.");

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(
                _connectionString,
                sql => sql.MigrationsAssembly(typeof(WorkflowDbContext).Assembly.FullName))
            .Options;

        // No interceptors. The auditing interceptor stamps CreatedBy from the *caller*, and the
        // caller here is an administrator in the live catalog whose id means nothing in this one.
        // Seeding sets its own timestamps instead.
        return new WorkflowDbContext(options);
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        await using var db = Open();

        // The same migrations that build live. That is the whole claim of this feature: a
        // demonstration runs on the schema production runs, with the same indexes, the same
        // ROWVERSION columns and the same constraints, so anything that would fail in front of a
        // client fails here first.
        await db.Database.MigrateAsync(ct);

        await SeederFor(db).SeedAsync(ct);
        await SeedCastAsync(db, ct);
    }

    public async Task<IReadOnlyList<DemoUserDto>> CastAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return Array.Empty<DemoUserDto>();

        await using var db = Open();

        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .ToDictionaryAsync(u => u.UserName, u => u, ct);

        // Ordered by the cast list rather than by name, so the switcher reads as the workflow does:
        // asked for, reviewed, given out, done, checked.
        return Cast
            .Where(c => users.ContainsKey(c.UserName))
            .Select(c => new DemoUserDto(
                users[c.UserName].Id, c.UserName, c.DisplayName, c.Role, c.Purpose))
            .ToList();
    }

    public async Task<DemoPrincipal?> FindAsync(long demoUserId, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        await using var db = Open();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == demoUserId, ct);

        // Must be one of the cast. Without this the switcher would be a way to mint a token for any
        // row in the demo catalog, including one a demonstration happened to create.
        if (user is null || !user.IsActive || !Cast.Any(c => c.UserName == user.UserName))
            return null;

        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync(ct);

        var roles = await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);

        var permissions = await db.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(db.Permissions.AsNoTracking(), rp => rp.PermissionId, p => p.Id, (rp, p) => p.Key)
            .Distinct()
            .ToListAsync(ct);

        return new DemoPrincipal(user, roles, permissions);
    }

    public async Task SignInAsync(long demoUserId, CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        await using var db = Open();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == demoUserId, ct);
        if (user is null) return;

        // The same rule login applies, not a demo-flavoured copy of it. The logger is built over
        // *this* context rather than injected, because the injected one follows the caller's token
        // and on entry that is the live catalog.
        WorkforceSignIn.Apply(user, new ActivityLogger(db, _clock), _clock.UtcNow);

        await db.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        await using var db = Open();

        // Dropped and rebuilt rather than emptied table by table. A delete list is a thing to keep
        // in step with the schema forever, and the one time somebody forgets to add a table is the
        // time a demonstration opens with the last demonstration's data still in it.
        //
        // The pools are cleared first: a demonstration that is still open holds connections to this
        // catalog, and they would block the drop — on exactly the reset meant to clear up after it.
        SqlConnection.ClearAllPools();

        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.MigrateAsync(ct);

        await SeederFor(db).SeedAsync(ct);
        await SeedCastAsync(db, ct);

        _logger.LogInformation("Demo environment reset.");
    }

    /// <summary>
    /// The cast and enough around them to be worth looking at: a client, and a request part-way
    /// through the workflow so the queues are not all empty on the first screen.
    /// </summary>
    private async Task SeedCastAsync(WorkflowDbContext db, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var existing = await db.Users.Select(u => u.UserName).ToListAsync(ct);
        var added = false;

        foreach (var member in Cast)
        {
            if (existing.Contains(member.UserName)) continue;

            var user = new User
            {
                UserName = member.UserName,
                DisplayName = member.DisplayName,
                Email = $"{member.UserName}@demo.local",
                PasswordHash = _hasher.Hash(DemoPassword),
                IsActive = true,
                CreatedAt = now,
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == member.Role, ct);
            if (role is not null)
                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

            added = true;
        }

        if (added) await db.SaveChangesAsync(ct);

        if (!await db.Clients.AnyAsync(ct))
        {
            db.Clients.AddRange(
                new Client { Name = "Northwind Foods", CreatedAt = now },
                new Client { Name = "Bluebird Logistics", CreatedAt = now },
                new Client { Name = "Cedar Retail", CreatedAt = now });

            await db.SaveChangesAsync(ct);
        }
    }
}
