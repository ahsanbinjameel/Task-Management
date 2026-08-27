/*
    Clears operational data from a WorkflowApp database, leaving a usable empty system.

    KEEPS:  the `admin` account with its roles and password, and the seeded catalogue —
            Permissions, Roles, RolePermissions, PauseReasons.
    DROPS:  every request (single and batched), task, quick-work record, work session, shift,
            activity event, QC review, comment, dependency, scope change, attachment row,
            notification, audit entry, login attempt, refresh token, the organisation lookups
            (clients, departments, teams, projects, modules — these are typed in by hand, not
            seeded), and every user account other than the one kept.

    Deletes run child-first so no foreign key is ever violated, and the whole thing is one
    transaction with XACT_ABORT on: any failure rolls the database back to exactly how it was.

    Reference numbers reset, so the next request is REQ-000001 rather than continuing from
    wherever the discarded data left the counter.

    In SSMS: open it, check the database dropdown shows the database you mean, and Execute.
    From the command line, -I is required — filtered indexes need QUOTED_IDENTIFIER ON and
    sqlcmd defaults it OFF, while SSMS defaults it ON:

        sqlcmd -S localhost -E -I -d WorkflowApp_Dev -b -i scripts\sql\reset-dev-data.sql

    Attachment ROWS are deleted, but the FILES on disk are not — clear FileStorage:Root
    yourself (src\WorkflowApp.Api\storage-dev in Development, C:\WorkflowApp\storage
    by default elsewhere).

    NOT for production. It deletes user accounts.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- The account to keep, found by name rather than a hardcoded id: the bootstrap admin is not
-- guaranteed to be id 1 on a database that was seeded in a different order.
DECLARE @KeepUserName nvarchar(100) = N'admin';
DECLARE @KeepUserId bigint = (SELECT Id FROM Users WHERE UserName = @KeepUserName);

IF @KeepUserId IS NULL
BEGIN
    -- Stop rather than delete every account and lock everyone out of the system.
    RAISERROR('No user named ''%s'' found. Aborting so the database is not left with no accounts.',
              16, 1, @KeepUserName);
    RETURN;
END

BEGIN TRANSACTION;

    -- --- task children ------------------------------------------------------------------
    -- Attachments first: they hang off a request, a task, a batch or a verification, and a QC
    -- evidence file also points at the numbered attempt it justified, so they precede QCReviews
    -- and Verifications too.
    DELETE FROM Attachments;

    DELETE FROM TaskActivities;
    DELETE FROM StatusHistories;
    DELETE FROM AssignmentHistories;
    DELETE FROM TaskComments;
    DELETE FROM TaskDependencies;
    DELETE FROM ScopeChanges;
    DELETE FROM QCReviews;
    DELETE FROM WorkSessions;
    DELETE FROM TaskCollaborators;

    -- Quick work points at a user, a client, and optionally the task it interrupted and the
    -- request it was promoted into — so it goes before every one of them.
    DELETE FROM QuickWork;

    -- Subtasks reference a parent task; clear the children first.
    DELETE FROM Tasks WHERE ParentTaskId IS NOT NULL;
    DELETE FROM Tasks;

    -- --- verifications ------------------------------------------------------------------
    -- Between the tasks and the requests: a verification points at a request, a module and two
    -- users, and nothing points at it except its own activity stream and its attachments, both
    -- already gone above.
    DELETE FROM VerificationActivities;
    DELETE FROM Verifications;

    -- --- request children ---------------------------------------------------------------
    DELETE FROM RequestActivities;
    DELETE FROM RequestClarifications;
    DELETE FROM Requests;

    -- A batch is only a wrapper around its items, so it goes once the items are gone.
    DELETE FROM RequestBatches;

    -- --- workforce ----------------------------------------------------------------------
    DELETE FROM ActivityEvents;
    DELETE FROM ShiftSessions;

    -- --- per-user noise -----------------------------------------------------------------
    DELETE FROM Notifications;
    DELETE FROM AuditLogs;
    DELETE FROM LoginAttempts;
    DELETE FROM RefreshTokens;

    -- --- organisation lookups -----------------------------------------------------------
    -- Not seeded — whatever is here was typed in during testing. Modules hang off projects,
    -- projects off clients, teams off departments.
    DELETE FROM Modules;
    DELETE FROM Projects;
    DELETE FROM Clients;
    DELETE FROM Teams;
    DELETE FROM Departments;

    -- --- the accounts themselves --------------------------------------------------------
    -- The kept account keeps its own grants; everyone else loses theirs, then the account.
    DELETE FROM UserRoles WHERE UserId <> @KeepUserId;
    DELETE FROM Users     WHERE Id     <> @KeepUserId;

    -- Leave the kept account off-shift and not mid-task, whatever the discarded data left behind.
    UPDATE Users SET WorkforceState = 1 WHERE Id = @KeepUserId;   -- LoggedInShiftNotStarted

    -- --- numbering ----------------------------------------------------------------------
    UPDATE NumberSequences SET NextValue = 1;

COMMIT TRANSACTION;

PRINT 'Reset committed. Kept user: ' + @KeepUserName;
