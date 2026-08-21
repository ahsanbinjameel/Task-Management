using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Infrastructure.Persistence.Seed;

/// <summary>
/// Populates a demo database with people, requests and tasks spread across the pipeline, so the
/// running application has something to show instead of empty lists.
///
/// Strictly for local evaluation: it creates accounts with a shared, well-known password. It only
/// runs when explicitly enabled AND when no request data exists yet, so it can never overwrite
/// real work.
/// </summary>
public sealed class DemoDataSeeder
{
    /// <summary>Same password for every demo account. Obvious, and obviously not for production.</summary>
    public const string DemoPassword = "Demo!Pass123";

    private readonly WorkflowDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        WorkflowDbContext db,
        IPasswordHasher passwordHasher,
        IDateTimeProvider clock,
        ILogger<DemoDataSeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await _db.Requests.AnyAsync(ct))
        {
            _logger.LogInformation("Demo data already present; skipping.");
            return;
        }

        var now = _clock.UtcNow;

        var org = await SeedOrganizationAsync(ct);
        var people = await SeedPeopleAsync(ct);
        await SeedPipelineAsync(people, org, now, ct);

        _logger.LogWarning(
            "Seeded DEMO data. Accounts: {Accounts} — all with password '{Password}'. " +
            "Never enable Database:SeedDemoData outside local evaluation.",
            string.Join(", ", people.Keys), DemoPassword);
    }

    private async Task<(long ClientId, long ProjectId)> SeedOrganizationAsync(CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Name == "Northwind Logistics", ct);
        if (client is null)
        {
            client = new Client { Name = "Northwind Logistics", Code = "NWL" };
            _db.Clients.Add(client);
            await _db.SaveChangesAsync(ct);
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Name == "Dispatch Portal", ct);
        if (project is null)
        {
            project = new Project { Name = "Dispatch Portal", Code = "DP", ClientId = client.Id };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync(ct);
        }

        return (client.Id, project.Id);
    }

    /// <summary>One account per role, so every screen can be viewed from the right perspective.</summary>
    private async Task<Dictionary<string, User>> SeedPeopleAsync(CancellationToken ct)
    {
        var definitions = new (string UserName, string DisplayName, string[] Roles)[]
        {
            ("rachel",  "Rachel Owens (Requester)",   new[] { DefaultRoles.Requester }),
            ("victor",  "Victor Reyes (Reviewer)",    new[] { DefaultRoles.Reviewer, DefaultRoles.Requester }),
            ("amara",   "Amara Diallo (Coordinator)", new[] { DefaultRoles.AssignmentManager }),
            ("wu",      "Wu Chen (Worker)",           new[] { DefaultRoles.Worker }),
            ("priya",   "Priya Nair (Worker)",        new[] { DefaultRoles.Worker }),
            ("quentin", "Quentin Blake (QC)",         new[] { DefaultRoles.QC }),
            ("morgan",  "Morgan Lee (Management)",    new[] { DefaultRoles.Management })
        };

        var roles = await _db.Roles.ToDictionaryAsync(r => r.Name, ct);
        var people = new Dictionary<string, User>();

        foreach (var (userName, displayName, roleNames) in definitions)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == userName, ct);

            if (user is null)
            {
                user = new User
                {
                    UserName = userName,
                    Email = $"{userName}@workflowapp.local",
                    DisplayName = displayName,
                    PasswordHash = _passwordHasher.Hash(DemoPassword),
                    IsActive = true,
                    WorkforceState = WorkforceState.NotLoggedIn
                };

                _db.Users.Add(user);
                await _db.SaveChangesAsync(ct);

                foreach (var roleName in roleNames.Where(roles.ContainsKey))
                    _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roles[roleName].Id });

                await _db.SaveChangesAsync(ct);
            }

            people[userName] = user;
        }

        return people;
    }

    /// <summary>
    /// Builds a pipeline with something in most states, so the queues, the workflow buttons and the
    /// timeline all have real content the moment the app opens.
    /// </summary>
    private async Task SeedPipelineAsync(
        Dictionary<string, User> people, (long ClientId, long ProjectId) org, DateTimeOffset now, CancellationToken ct)
    {
        var rachel = people["rachel"];
        var victor = people["victor"];
        var amara = people["amara"];
        var wu = people["wu"];
        var priya = people["priya"];

        var sequence = 1;
        var taskSequence = 1;

        Request NewRequest(string title, string description, RequestType type,
            RequestedUrgency urgency, RequestStatus status, int daysAgo)
        {
            var request = new Request
            {
                RequestNumber = $"REQ-{sequence++:D6}",
                Title = title,
                Description = description,
                Type = type,
                RequestedUrgency = urgency,
                Status = status,
                RequestedByUserId = rachel.Id,
                RequestedAt = now.AddDays(-daysAgo),
                ClientId = org.ClientId,
                ProjectId = org.ProjectId,
                BusinessImpact = "Dispatch team is working around this manually."
            };

            _db.Requests.Add(request);
            return request;
        }

        WorkTask NewTask(Request source, WorkTaskStatus status, Priority priority,
            long? assignee, decimal? estimate, int queueOrder = 0)
        {
            var task = new WorkTask
            {
                TaskNumber = $"TSK-{taskSequence++:D6}",
                RequestId = source.Id,
                Title = source.Title,
                Description = source.Description,
                Type = source.Type,
                ClientId = source.ClientId,
                ProjectId = source.ProjectId,
                Priority = priority,
                Status = status,
                PrimaryAssigneeUserId = assignee,
                EstimatedEffortHours = estimate,
                DueDate = now.AddDays(5),
                QueueOrder = queueOrder,
                AcceptanceCriteria = "Behaviour matches the agreed specification and QC signs off."
            };

            _db.Tasks.Add(task);
            return task;
        }

        // --- Awaiting triage: gives the reviewer a non-empty queue ---
        NewRequest("Consignment labels print with the wrong depot code",
            "Labels printed from the dispatch screen show the origin depot instead of the destination depot.",
            RequestType.Bug, RequestedUrgency.High, RequestStatus.Submitted, 2);

        NewRequest("Add a weekly carrier performance report",
            "Operations needs on-time delivery percentage per carrier, weekly, exportable to Excel.",
            RequestType.Report, RequestedUrgency.Normal, RequestStatus.Submitted, 4);

        NewRequest("Bulk import for customer addresses",
            "Uploading a CSV of addresses would save a day of manual entry each month.",
            RequestType.NewFeature, RequestedUrgency.Low, RequestStatus.Submitted, 9);

        // --- Waiting on the requester: exercises the clarification loop ---
        var needsInfo = NewRequest("Timeout when saving a large manifest",
            "Saving a manifest with more than 400 lines times out.",
            RequestType.Bug, RequestedUrgency.Critical, RequestStatus.ClarificationRequired, 3);

        await _db.SaveChangesAsync(ct);

        _db.RequestClarifications.Add(new RequestClarification
        {
            RequestId = needsInfo.Id,
            AskedByUserId = victor.Id,
            Question = "Roughly how many lines does it take to fail, and does it fail every time?",
            AskedAt = now.AddDays(-2)
        });

        // --- Approved and flowing through the pipeline ---
        var approvedForAssignment = NewRequest("Driver app shows stale ETA after a re-route",
            "After dispatch re-routes a driver, the app keeps showing the previous ETA until restart.",
            RequestType.Bug, RequestedUrgency.High, RequestStatus.Approved, 6);

        var approvedInProgress = NewRequest("Proof-of-delivery photos are not attached to invoices",
            "POD photos captured by drivers do not appear on the generated invoice PDF.",
            RequestType.Bug, RequestedUrgency.Critical, RequestStatus.Approved, 8);

        var approvedForQc = NewRequest("Depot contact numbers need a second field",
            "Each depot needs an out-of-hours number alongside the main one.",
            RequestType.ChangeRequest, RequestedUrgency.Normal, RequestStatus.Approved, 12);

        await _db.SaveChangesAsync(ct);

        // Unassigned — populates the assignment coordinator's queue.
        var readyTask = NewTask(approvedForAssignment, WorkTaskStatus.ReadyForAssignment, Priority.High, null, 6m);

        // Assigned and running — Wu has a live work session against this one.
        var runningTask = NewTask(approvedInProgress, WorkTaskStatus.InProgress, Priority.Critical, wu.Id, 12m, 1);

        // Sitting with QC.
        var qcTask = NewTask(approvedForQc, WorkTaskStatus.CompletedReadyForQC, Priority.Normal, priya.Id, 3m, 1);

        await _db.SaveChangesAsync(ct);

        approvedForAssignment.GeneratedTaskId = readyTask.Id;
        approvedInProgress.GeneratedTaskId = runningTask.Id;
        approvedForQc.GeneratedTaskId = qcTask.Id;

        foreach (var (task, actor) in new[]
                 {
                     (readyTask, victor), (runningTask, victor), (qcTask, victor)
                 })
        {
            _db.StatusHistories.Add(new StatusHistory
            {
                TaskId = task.Id,
                FromStatus = WorkTaskStatus.Approved,
                ToStatus = WorkTaskStatus.ReadyForAssignment,
                ChangedByUserId = actor.Id,
                ChangedAt = task.CreatedAt,
                Reason = "Created from approved request"
            });
        }

        _db.AssignmentHistories.Add(new AssignmentHistory
        {
            TaskId = runningTask.Id,
            ToUserId = wu.Id,
            AssignedByUserId = amara.Id,
            AssignedAt = now.AddDays(-2)
        });

        // Wu is mid-shift with one closed session and one still running, so the timer and the
        // "who's working now" view both have something real in them.
        wu.WorkforceState = WorkforceState.Working;

        var shift = new ShiftSession { UserId = wu.Id, ShiftStart = now.AddHours(-3) };
        _db.ShiftSessions.Add(shift);
        await _db.SaveChangesAsync(ct);

        _db.ActivityEvents.Add(new ActivityEvent
        {
            UserId = wu.Id,
            ShiftSessionId = shift.Id,
            OccurredAt = now.AddHours(-3),
            Label = "Shift Started",
            ResultingState = WorkforceState.Available
        });

        _db.WorkSessions.AddRange(
            new WorkSession
            {
                TaskId = runningTask.Id,
                UserId = wu.Id,
                SessionStart = now.AddHours(-3),
                SessionEnd = now.AddHours(-2),
                Status = WorkSessionStatus.Paused,
                EndComment = "Stopped to join the stand-up"
            },
            new WorkSession
            {
                TaskId = runningTask.Id,
                UserId = wu.Id,
                SessionStart = now.AddMinutes(-40),
                Status = WorkSessionStatus.Active
            });

        _db.ActivityEvents.Add(new ActivityEvent
        {
            UserId = wu.Id,
            ShiftSessionId = shift.Id,
            OccurredAt = now.AddMinutes(-40),
            Label = $"Task {runningTask.TaskNumber} — In Progress",
            RelatedTaskId = runningTask.Id
        });

        // Priya is on shift but between tasks.
        priya.WorkforceState = WorkforceState.Available;
        var priyaShift = new ShiftSession { UserId = priya.Id, ShiftStart = now.AddHours(-5) };
        _db.ShiftSessions.Add(priyaShift);

        await _db.SaveChangesAsync(ct);

        // The generated numbers above bypassed the counter, so move it past what was used.
        await AdvanceSequenceAsync("Request", sequence, ct);
        await AdvanceSequenceAsync("Task", taskSequence, ct);
    }

    /// <summary>
    /// Keeps the shared counter ahead of the hand-written demo numbers, so the first real request
    /// created through the API does not collide with a seeded one.
    /// </summary>
    private async Task AdvanceSequenceAsync(string key, long nextValue, CancellationToken ct)
    {
        var sequence = await _db.NumberSequences.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (sequence is null)
        {
            _db.NumberSequences.Add(new Domain.Entities.Common.NumberSequence { Key = key, NextValue = nextValue });
        }
        else if (sequence.NextValue < nextValue)
        {
            sequence.NextValue = nextValue;
            sequence.Version++;
        }

        await _db.SaveChangesAsync(ct);
    }
}
