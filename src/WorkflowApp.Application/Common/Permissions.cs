namespace WorkflowApp.Application.Common;

/// <summary>
/// The complete catalog of permission keys. Roles are bundles of these. Server-side checks
/// reference these constants so there is one authoritative list.
/// </summary>
public static class Permissions
{
    // Requests
    public const string RequestCreate = "Request.Create";
    public const string RequestViewOwn = "Request.ViewOwn";
    public const string RequestViewAll = "Request.ViewAll";

    // Triage
    public const string TaskReview = "Task.Review";
    public const string TaskApprove = "Task.Approve";

    // Assignment
    public const string TaskAssign = "Task.Assign";

    // Execution
    public const string TaskWork = "Task.Work";

    // QC & closure
    public const string TaskQCReview = "Task.QCReview";
    public const string TaskClose = "Task.Close";
    public const string TaskReopen = "Task.Reopen";
    public const string TaskCancel = "Task.Cancel";
    public const string TaskDefer = "Task.Defer";
    public const string TaskOverride = "Task.Override";

    // Verification — assigned investigation, distinct from task QC. See VerificationService.
    //
    // Three rather than four. "Assign" is deliberately not separate from Create: a verification
    // with no checker is inert, so naming one is part of raising it, and the reviewer who routes a
    // request is the same person who says who should look at it. Splitting them would mean holding
    // two permissions to perform the single action the feature exists for, with no real difference
    // in authority behind the split. If reassignment ever needs to be somebody else's job, it is
    // one constant here and a role-map line — not a schema change.

    /// <summary>Raise a verification, name its checker, re-route it, and call it off.</summary>
    public const string VerificationCreate = "Verification.Create";

    /// <summary>Investigate an assigned verification and record what was found.</summary>
    public const string VerificationWork = "Verification.Work";

    /// <summary>See every verification, not only the ones you raised or were given.</summary>
    public const string VerificationViewAll = "Verification.ViewAll";

    // Workforce / management
    public const string WorkforceViewAll = "Workforce.ViewAll";

    /// <summary>Act on someone else's shift — force-end an abandoned one, correct a state.</summary>
    public const string WorkforceManageOthers = "Workforce.ManageOthers";

    /// <summary>
    /// This user's attendance is tracked: they start and end shifts and set their availability.
    /// Held by people who execute tasks. Reviewers, coordinators, requesters and management use the
    /// system without their hours being measured, so they never open a shift.
    ///
    /// Deliberately its own permission rather than a side effect of <see cref="TaskWork"/> — who is
    /// on the clock is an operational decision, changeable in the role editor without a deploy.
    /// </summary>
    public const string WorkforceTrackShift = "Workforce.TrackShift";
    public const string DashboardManagement = "Dashboard.Management";
    public const string ReportsView = "Reports.View";

    // Admin
    public const string AdminManageUsers = "Admin.ManageUsers";
    public const string AdminManageRoles = "Admin.ManageRoles";
    public const string AdminManageConfig = "Admin.ManageConfig";
    public const string AdminViewAudit = "Admin.ViewAudit";

    public static readonly string[] All =
    {
        RequestCreate, RequestViewOwn, RequestViewAll,
        TaskReview, TaskApprove, TaskAssign, TaskWork,
        TaskQCReview, TaskClose, TaskReopen, TaskCancel, TaskDefer, TaskOverride,
        VerificationCreate, VerificationWork, VerificationViewAll,
        WorkforceViewAll, WorkforceManageOthers, WorkforceTrackShift, DashboardManagement, ReportsView,
        AdminManageUsers, AdminManageRoles, AdminManageConfig, AdminViewAudit
    };
}

/// <summary>Default seeded roles and the permissions they grant. Used by the Phase 1 seeder.</summary>
public static class DefaultRoles
{
    public const string Administrator = "Administrator";
    public const string Requester = "Requester";
    public const string Reviewer = "Reviewer";
    public const string AssignmentManager = "AssignmentManager";
    public const string Worker = "Worker";
    public const string QC = "QC";
    public const string Management = "Management";

    /// <summary>
    /// What an administrator gets by default: everything <em>except</em> the two permissions that
    /// describe an operational worker rather than an authority.
    ///
    /// <see cref="Permissions.WorkforceTrackShift"/> means "this person's attendance is measured"
    /// and <see cref="Permissions.TaskWork"/> means "this person executes tasks". Neither follows
    /// from administering the system, and granting them by default made every administrator a
    /// worker: the shell offered a shift widget nobody wanted, the account appeared in
    /// who-is-working-now, and it turned up in the assignable list for real work.
    ///
    /// An administrator who genuinely also does the work gets these the same way anybody else does
    /// — a role that grants them, through the role editor. <c>Administrator = Worker</c> is a
    /// configuration decision, and it does not belong in code.
    ///
    /// Note the seeder is additive: this changes what a <em>new</em> database grants. An existing
    /// Administrator role keeps the grants it already has until someone removes them in the editor,
    /// which is deliberate — restarting the application must not silently revoke a permission a
    /// site chose to add.
    /// </summary>
    public static readonly string[] AdministratorGrants = Permissions.All
        .Where(key => key is not (Permissions.WorkforceTrackShift or Permissions.TaskWork))
        .ToArray();

    public static readonly IReadOnlyDictionary<string, string[]> Map = new Dictionary<string, string[]>
    {
        [Administrator] = AdministratorGrants,
        [Requester] = new[] { Permissions.RequestCreate, Permissions.RequestViewOwn },
        // Reopen sits with the reviewer, not QC: it is a judgement about whether the delivered work
        // answers the original request, which is the same call they make at triage. QC's remit is to
        // pass or fail a submission, not to un-close finished work.
        [Reviewer] = new[]
        {
            Permissions.RequestViewAll, Permissions.TaskReview, Permissions.TaskApprove,
            Permissions.TaskDefer, Permissions.TaskReopen,
            // The triage route: a reviewer who cannot yet tell whether there is anything to build
            // sends it to be looked at instead of guessing, or approving to find out.
            Permissions.VerificationCreate, Permissions.VerificationViewAll
        },
        [AssignmentManager] = new[]
        {
            Permissions.RequestViewAll, Permissions.TaskAssign, Permissions.WorkforceViewAll,
            Permissions.WorkforceManageOthers, Permissions.DashboardManagement,
            Permissions.VerificationCreate, Permissions.VerificationViewAll
        },
        // Workers are the only default role on the clock — see Permissions.WorkforceTrackShift.
        //
        // They can also raise a request and read their own. Not scope creep: a worker who fields a
        // phone call and finds real work behind it has to be able to put it into the system, and
        // Quick Work's promotion step is exactly that — it creates a Request, so it is gated on
        // Request.Create like every other way of creating one. Without this the feature is dead for
        // the only role it was built for. ViewOwn comes with it so they can follow what they raised;
        // they still cannot browse anyone else's, which needs Request.ViewAll.
        [Worker] = new[]
        {
            Permissions.TaskWork, Permissions.WorkforceTrackShift,
            Permissions.RequestCreate, Permissions.RequestViewOwn
        },
        // QC checks finished work and now also investigates whether there is anything to build.
        // Note what is still absent: Workforce.TrackShift. Whether a checker's attendance is
        // measured is an independent decision for whoever configures the organisation, and
        // Verification.Work deliberately does not imply it — see Permissions.WorkforceTrackShift.
        [QC] = new[]
        {
            Permissions.TaskQCReview, Permissions.TaskClose,
            Permissions.VerificationWork, Permissions.VerificationViewAll
        },
        [Management] = new[]
        {
            Permissions.RequestViewAll, Permissions.WorkforceViewAll,
            Permissions.DashboardManagement, Permissions.ReportsView,
            Permissions.VerificationViewAll
        },
    };
}
