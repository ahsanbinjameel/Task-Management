using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Events;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Phases 9-11: after-commit integration events, in-app notifications, the audit stream, and the
/// dashboards and reports built on top of them.
/// </summary>
public class RealtimeReportingTests
{
    private sealed record Fixture(TestHarness H, long TaskId, long RequesterId, long WorkerId,
        long CoordinatorId, long QCUserId);

    private static async Task<Fixture> AssignedTaskAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var coordinator = await h.CreateUserAsync("amara");
        var worker = await h.CreateUserAsync("wu");
        var qc = await h.CreateUserAsync("quentin");

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Proof-of-delivery photos missing from invoices",
            Description = "POD photos do not appear on the generated PDF.",
            Type = RequestType.Bug
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.High,
            EstimatedEffortHours = 6m
        });

        var task = await h.Db.Tasks.SingleAsync();

        h.ActingAsAdmin(coordinator.Id);
        await h.Assignment.AssignAsync(task.Id, coordinator.Id,
            new AssignTaskDto { AssigneeUserId = worker.Id });

        return new Fixture(h, task.Id, requester.Id, worker.Id, coordinator.Id, qc.Id);
    }

    // --- Phase 9: integration events -----------------------------------------------------------

    [Fact]
    public async Task Committing_a_task_change_publishes_an_event_derived_from_the_write()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        f.H.Events.Published.Clear();

        await f.H.Assignment.UpdateDetailsAsync(f.TaskId, f.CoordinatorId,
            new UpdateTaskDetailsDto { Priority = Priority.Critical });

        var published = f.H.Events.Published.OfType<TaskChangedEvent>().ToList();

        Assert.Single(published);
        Assert.Equal(f.TaskId, published[0].TaskId);
        Assert.Equal(ChangeKind.Updated, published[0].Kind);
        Assert.Equal(f.WorkerId, published[0].AssigneeUserId);
    }

    [Fact]
    public async Task A_new_task_publishes_a_created_event()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        f.H.Events.Published.Clear();

        await f.H.TaskCreation.CreateSubtaskAsync(f.TaskId, f.CoordinatorId,
            new CreateSubtaskDto { Title = "Backfill", Description = "Regenerate Q2 PDFs." });

        Assert.Contains(f.H.Events.Published.OfType<TaskChangedEvent>(),
            e => e.Kind == ChangeKind.Created);
    }

    [Fact]
    public async Task Changing_availability_publishes_a_workforce_event()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);
        f.H.Events.Published.Clear();

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        Assert.Contains(f.H.Events.Published.OfType<WorkforceChangedEvent>(),
            e => e.UserId == f.WorkerId && e.State == WorkforceState.Working);
    }

    [Fact]
    public async Task Events_carry_an_identifier_and_a_status_not_a_copy_of_the_record()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;

        var published = f.H.Events.Published.OfType<TaskChangedEvent>().Last();

        // The whole point of the thin payload: enough to route and to know something moved,
        // never enough to render from. Clients re-fetch.
        Assert.NotEqual(0, published.TaskId);
        Assert.False(string.IsNullOrWhiteSpace(published.TaskNumber));
        Assert.Equal(WorkTaskStatus.Assigned, published.Status);
    }

    [Fact]
    public void Group_names_are_built_in_one_place_so_sender_and_receiver_cannot_drift()
    {
        Assert.Equal("user:42", RealtimeGroups.User(42));
        Assert.Equal("task:7", RealtimeGroups.Task(7));
        Assert.Equal("perm:Task.Assign", RealtimeGroups.Permission("Task.Assign"));
    }

    // --- Phase 11: notifications ----------------------------------------------------------------

    [Fact]
    public async Task Being_assigned_work_notifies_the_assignee_but_not_the_coordinator()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;

        var theirs = await f.H.Notifications.ListAsync(f.WorkerId, unreadOnly: true, new PageQuery());
        var mine = await f.H.Notifications.ListAsync(f.CoordinatorId, unreadOnly: true, new PageQuery());

        Assert.Single(theirs.Items);
        Assert.Contains("assigned to you", theirs.Items[0].Title);
        Assert.Empty(mine.Items);   // you know what you just did
    }

    [Fact]
    public async Task A_notification_publishes_a_realtime_event_to_its_recipient()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;

        Assert.Contains(f.H.Events.Published.OfType<NotificationRaisedEvent>(),
            e => e.RecipientUserId == f.WorkerId);
    }

    [Fact]
    public async Task A_failed_QC_tells_the_assignee_why()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Done.");

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "Photos are stretched on A4."
        });

        var theirs = await f.H.Notifications.ListAsync(f.WorkerId, unreadOnly: true, new PageQuery());

        Assert.Contains(theirs.Items, n => n.Title.Contains("failed QC"));
        Assert.Contains(theirs.Items, n => n.Body == "Photos are stretched on A4.");
    }

    [Fact]
    public async Task Marking_read_only_touches_your_own_notifications()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;

        var theirs = await f.H.Notifications.ListAsync(f.WorkerId, unreadOnly: true, new PageQuery());
        var id = theirs.Items[0].Id;

        // The coordinator tries to mark the worker's notification read.
        await f.H.Notifications.MarkReadAsync(f.CoordinatorId, new[] { id });
        Assert.Equal(1, await f.H.Notifications.UnreadCountAsync(f.WorkerId));

        await f.H.Notifications.MarkReadAsync(f.WorkerId, new[] { id });
        Assert.Equal(0, await f.H.Notifications.UnreadCountAsync(f.WorkerId));
    }

    // --- Phase 11: audit stream ------------------------------------------------------------------

    [Fact]
    public async Task The_audit_stream_can_be_filtered_by_entity_and_action()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Done.");

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto { Result = QCResult.Passed });

        var all = await f.H.AuditQueries.ListAsync(
            new Notifications.AuditQuery { EntityType = "WorkTask", EntityId = f.TaskId }, new PageQuery());

        Assert.NotEmpty(all.Items);
        Assert.All(all.Items, a => Assert.Equal(f.TaskId, a.EntityId));

        var passed = await f.H.AuditQueries.ListAsync(
            new Notifications.AuditQuery { Action = "Task.QCPassed" }, new PageQuery());

        Assert.Single(passed.Items);
        Assert.Equal(f.QCUserId, passed.Items[0].ActorUserId);
    }

    // --- Phase 10: dashboards ---------------------------------------------------------------------

    [Fact]
    public async Task The_worker_dashboard_reports_the_queue_the_timer_and_the_hours()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(2));
        await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId, new StopWorkDto { Comment = "lunch" });

        var dashboard = await f.H.Dashboards.WorkerAsync(f.WorkerId);

        Assert.Equal(1, dashboard.QueueLength);
        Assert.True(dashboard.IsOnShift);
        Assert.Null(dashboard.ActiveTaskId);                       // paused, so nothing running
        Assert.Equal(TimeSpan.FromHours(2), dashboard.WorkedToday);
        Assert.Equal(1, dashboard.UnreadNotifications);            // the assignment
    }

    [Fact]
    public async Task The_coordinator_dashboard_separates_unassigned_from_late()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;

        // A second task, left unassigned and already overdue.
        var extra = await f.H.TaskCreation.CreateSubtaskAsync(f.TaskId, f.CoordinatorId,
            new CreateSubtaskDto
            {
                Title = "Backfill",
                Description = "Regenerate Q2 PDFs.",
                DueDate = f.H.Clock.UtcNow.AddDays(-1)
            });

        var task = await f.H.Db.Tasks.FirstAsync(t => t.Id == extra.Value!.Id);
        task.ParentTaskId = null;
        await f.H.Db.SaveChangesAsync();

        var dashboard = await f.H.Dashboards.CoordinatorAsync();

        Assert.Equal(1, dashboard.UnassignedCount);
        Assert.Equal(1, dashboard.OverdueCount);
        Assert.Contains(dashboard.Overdue, i => i.IsOverdue);
    }

    [Fact]
    public async Task The_management_dashboard_measures_throughput_and_QC_pass_rate()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(3));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Template corrected.");

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId,
            new SubmitQCReviewDto { Result = QCResult.Failed, Comments = "Stretched." });

        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(1));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Fixed.");

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto { Result = QCResult.Passed });
        await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto());

        var today = f.H.Calendar.ToBusinessDate(f.H.Clock.UtcNow);
        var dashboard = await f.H.Dashboards.ManagementAsync(today.AddDays(-1), today);

        Assert.Equal(1, dashboard.TasksClosed);
        Assert.Equal(2, dashboard.QCAttempts);
        Assert.Equal(1, dashboard.QCFailures);
        Assert.Equal(0.5, dashboard.QCPassRate);
        Assert.Equal(4m, dashboard.TotalHoursWorked);
        Assert.NotNull(dashboard.AverageCycleTimeHours);
        Assert.Equal(0, dashboard.OpenTaskCount);
    }

    // --- Phase 10: reports -------------------------------------------------------------------------

    [Fact]
    public async Task The_daily_user_report_breaks_time_down_by_task()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(2));
        await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId, new StopWorkDto { Comment = "break" });
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromMinutes(30));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Done.");

        var report = await f.H.Reports.DailyUserAsync(
            f.WorkerId, f.H.Calendar.ToBusinessDate(f.H.Clock.UtcNow));

        Assert.Equal(1, report.TasksWorked);
        Assert.Equal(1, report.TasksCompleted);

        var line = Assert.Single(report.OwnedWork);
        Assert.Equal(2, line.Sessions);                              // both stretches, not one span
        Assert.Equal(TimeSpan.FromMinutes(150), line.TimeSpent);

        // The worker owns this task, so none of it may be reported as work they merely supported.
        Assert.Empty(report.SupportWork);
    }

    [Fact]
    public async Task The_team_report_totals_everyone_who_was_on_shift()
    {
        var f = await AssignedTaskAsync();
        using var _d = f.H;
        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(1));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Done.");

        var today = f.H.Calendar.ToBusinessDate(f.H.Clock.UtcNow);
        var report = await f.H.Reports.DailyTeamAsync(today);

        Assert.Equal(1, report.PeopleOnShift);
        Assert.Equal(1, report.TasksCompleted);
        Assert.Equal(f.WorkerId, report.Users[0].UserId);
    }

    [Fact]
    public async Task The_CSV_export_quotes_fields_so_a_comma_in_a_name_cannot_shift_a_column()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        using var _d = h;

        var user = await h.CreateUserAsync("awkward");
        user.DisplayName = "Doe, Jane \"JD\"";
        await h.Db.SaveChangesAsync();
        await h.StartShiftAsync(user.Id);

        var csv = await h.Reports.DailyTeamCsvAsync(h.Calendar.ToBusinessDate(h.Clock.UtcNow));

        var header = csv.Split('\n')[0].Trim();
        var row = csv.Split('\n')[1].Trim();

        // The row has to have as many fields as the header, counting the way a spreadsheet counts:
        // a comma inside quotes is part of a name, not a column break. Asserting a fixed number
        // here would only pin down today's column list, and the failure that produced when Quick
        // Work added three columns said nothing at all about quoting.
        Assert.Equal(FieldCount(header), FieldCount(row));
        Assert.Contains("\"Doe, Jane \"\"JD\"\"\"", row);
    }

    /// <summary>Splits on commas that are outside quotes, the way a CSV reader does.</summary>
    private static int FieldCount(string line)
    {
        var fields = 1;
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) fields++;
        }

        return fields;
    }
}
