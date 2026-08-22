/*
    Clears operational data from a WorkflowApp database, leaving a usable empty system.

    KEEPS:  the `admin` account with its roles and password, and the seeded catalogue —
            Permissions, Roles, RolePermissions, PauseReasons.
    DROPS:  every request, task, work session, shift, activity event, QC review, comment,
            dependency, scope change, attachment row, notification, audit entry, login
            attempt, refresh token, and every user account other than the one kept.

    Deletes run child-first so no foreign key is ever violated, and the whole thing is one
    transaction with XACT_ABORT on: any failure rolls the database back to exactly how it was.

    Reference numbers reset, so the next request is REQ-000001 rather than continuing from
    wherever the discarded data left the counter.

    In SSMS: open it, check the database dropdown shows the database you mean, and Execute.
    From the command line, -I is required — filtered indexes need QUOTED_IDENTIFIER ON and
    sqlcmd defaults it OFF, while SSMS defaults it ON:

        sqlcmd -S localhost -E -I -d WorkflowApp_Dev -b -i scripts\sql\reset-dev-data.sql

    Attachment ROWS are deleted, but the FILES on disk are not — clear FileStorage:Root
    yourself (./storage-dev in Development, C:\WorkflowApp\storage by default elsewhere).

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
    DELETE FROM TaskActivities;
    DELETE FROM StatusHistories;
    DELETE FROM AssignmentHistories;
    DELETE FROM TaskComments;
    DELETE FROM TaskDependencies;
    DELETE FROM ScopeChanges;
    DELETE FROM QCReviews;
    DELETE FROM WorkSessions;
    DELETE FROM TaskCollaborators;

    -- Attachments hang off either a request or a task, so they go before both.
    DELETE FROM Attachments;

    -- Subtasks reference a parent task; clear the children first.
    DELETE FROM Tasks WHERE ParentTaskId IS NOT NULL;
    DELETE FROM Tasks;

    -- --- request children ---------------------------------------------------------------
    DELETE FROM RequestClarifications;
    DELETE FROM Requests;

    -- --- workforce ----------------------------------------------------------------------
    DELETE FROM ActivityEvents;
    DELETE FROM ShiftSessions;

    -- --- per-user noise -----------------------------------------------------------------
    DELETE FROM Notifications;
    DELETE FROM AuditLogs;
    DELETE FROM LoginAttempts;
    DELETE FROM RefreshTokens;

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
