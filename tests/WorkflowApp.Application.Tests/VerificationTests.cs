using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Verifications.Dtos;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Verification: assigned investigation, with no completed task required.
///
/// The invariant almost every test here circles is the one the feature was built around — a check
/// establishes whether there is a problem, and establishing that never creates work. Approving
/// stays a reviewer's explicit decision through <c>TaskCreationService</c>.
/// </summary>
public class VerificationTests
{
    private static readonly IReadOnlySet<string> ViewAll =
        new HashSet<string> { Permissions.VerificationViewAll };

    private static readonly IReadOnlySet<string> NoPermissions = new HashSet<string>();

    private static async Task<(TestHarness H, long RequesterId, long ReviewerId, long CheckerId)> ReadyAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var requester = await h.CreateUserAsync("rachel", roles: DefaultRoles.Requester);
        var reviewer = await h.CreateUserAsync("victor", roles: DefaultRoles.Reviewer);
        var checker = await h.CreateUserAsync("quentin", roles: DefaultRoles.QC);
        h.ActingAsAdmin(reviewer.Id);
        return (h, requester.Id, reviewer.Id, checker.Id);
    }

    private static CreateRequestDto NewRequest() => new()
    {
        Title = "Employee Salary form is not calculating tax correctly",
        Description = "The tax column shows zero for staff on the higher band.",
        Type = RequestType.Bug,
        RequestedUrgency = RequestedUrgency.High
    };

    /// <summary>Raise a request and route it to the checker in one step, as triage does.</summary>
    private static async Task<(long RequestId, long VerificationId)> RoutedAsync(
        TestHarness h, long requesterId, long reviewerId, long checkerId)
    {
        var request = await h.Requests.CreateAsync(requesterId, NewRequest());
        await h.Triage.StartReviewAsync(request.Value!.Id, reviewerId);

        var decision = await h.Triage.DecideAsync(request.Value.Id, reviewerId, new TriageDecisionDto
        {
            Outcome = TriageOutcome.SendForVerification,
            Verification = new SendForVerificationDto
            {
                Instructions = "Check whether the higher band is configured on this client.",
                AssignToUserId = checkerId
            }
        });

        return (request.Value.Id, decision.Value!.VerificationId!.Value);
    }

    // --- the triage route ------------------------------------------------------------------

    [Fact]
    public async Task Sending_for_verification_creates_a_check_and_no_task()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        // The whole point: a reviewer who cannot yet tell whether there is anything to build has a
        // way forward that does not commit the organisation to building it.
        Assert.False(await h.Db.Tasks.AnyAsync());

        var verification = await h.Db.Verifications.SingleAsync();
        Assert.Equal(verificationId, verification.Id);
        Assert.Equal(requestId, verification.RequestId);
        Assert.Equal(checker, verification.AssignedToUserId);
        Assert.Equal(VerificationStatus.Assigned, verification.Status);

        var request = await h.Db.Requests.SingleAsync();
        Assert.Equal(RequestStatus.UnderVerification, request.Status);
        Assert.Null(request.GeneratedTaskId);
    }

    [Fact]
    public async Task Verification_numbers_are_their_own_dense_sequence()
    {
        var (h, _, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var first = await h.Verifications.CreateAsync(reviewer, NewCheck("Salary form"));
        var second = await h.Verifications.CreateAsync(reviewer, NewCheck("Leave form"));

        Assert.Equal("VER-000001", first.Value!.VerificationNumber);
        Assert.Equal("VER-000002", second.Value!.VerificationNumber);
    }

    [Fact]
    public async Task A_request_cannot_be_sent_for_verification_twice_at_once()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, _) = await RoutedAsync(h, requester, reviewer, checker);

        var second = await h.Triage.DecideAsync(requestId, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.SendForVerification,
            Verification = new SendForVerificationDto { AssignToUserId = checker }
        });

        Assert.False(second.IsSuccess);
        Assert.Equal("request.verification_pending", second.Error!.Code);
    }

    [Fact]
    public async Task Sending_for_verification_without_details_is_refused()
    {
        var (h, requester, reviewer, _) = await ReadyAsync();
        using var _d = h;

        var request = await h.Requests.CreateAsync(requester, NewRequest());

        var result = await h.Triage.DecideAsync(request.Value!.Id, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.SendForVerification
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("triage.verification_details_required", result.Error!.Code);
    }

    // --- the invariant ---------------------------------------------------------------------

    [Fact]
    public async Task A_confirmed_issue_creates_no_task_and_returns_the_request_to_review()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        await h.Verifications.StartAsync(verificationId, checker);
        var reported = await h.Verifications.RecordResultAsync(verificationId, checker, new RecordVerificationResultDto
        {
            Result = VerificationResult.IssueConfirmed,
            Findings = "Reproduced on the higher band. The rate table has no row above 40%."
        });

        Assert.True(reported.IsSuccess);

        // The load-bearing assertion of this whole feature.
        Assert.False(await h.Db.Tasks.AnyAsync());

        var request = await h.Db.Requests.SingleAsync();
        Assert.Equal(RequestStatus.InReview, request.Status);
        Assert.Null(request.GeneratedTaskId);
    }

    [Theory]
    [InlineData(VerificationResult.IssueConfirmed)]
    [InlineData(VerificationResult.WorkingCorrectly)]
    [InlineData(VerificationResult.ConfigurationOrDataIssue)]
    [InlineData(VerificationResult.NeedsClarification)]
    [InlineData(VerificationResult.Inconclusive)]
    public async Task Every_result_hands_the_request_back_to_the_reviewer(VerificationResult result)
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (_, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        await h.Verifications.RecordResultAsync(verificationId, checker, new RecordVerificationResultDto
        {
            Result = result,
            Findings = "Looked at it."
        });

        // One rule rather than five. The reviewer has every triage outcome available and is the one
        // who should be choosing between them.
        var request = await h.Db.Requests.SingleAsync();
        Assert.Equal(RequestStatus.InReview, request.Status);
        Assert.False(await h.Db.Tasks.AnyAsync());
    }

    [Fact]
    public async Task Approval_after_a_confirmed_issue_is_what_finally_creates_the_task()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        await h.Verifications.RecordResultAsync(verificationId, checker, new RecordVerificationResultDto
        {
            Result = VerificationResult.IssueConfirmed,
            Findings = "Confirmed."
        });

        var approved = await h.Triage.DecideAsync(requestId, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve
        });

        Assert.True(approved.IsSuccess);
        Assert.NotNull(approved.Value!.CreatedTaskId);
        Assert.Single(await h.Db.Tasks.ToListAsync());
    }

    [Fact]
    public async Task A_request_cannot_be_approved_while_a_check_is_still_running()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, _) = await RoutedAsync(h, requester, reviewer, checker);

        var approved = await h.Triage.DecideAsync(requestId, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve
        });

        Assert.False(approved.IsSuccess);
        Assert.Equal("request.verification_pending", approved.Error!.Code);
        Assert.False(await h.Db.Tasks.AnyAsync());
    }

    [Fact]
    public async Task A_request_cannot_be_rejected_out_from_under_a_running_check_either()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, _) = await RoutedAsync(h, requester, reviewer, checker);

        // The loose end is the same as approving: a checker submitting findings against a request
        // that was decided underneath them has done the work for nothing.
        var rejected = await h.Triage.DecideAsync(requestId, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Reject,
            Reason = "Changed our minds."
        });

        Assert.False(rejected.IsSuccess);
        Assert.Equal("request.verification_pending", rejected.Error!.Code);
    }

    [Fact]
    public async Task Calling_a_check_off_releases_the_request()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        var cancelled = await h.Verifications.CancelAsync(verificationId, reviewer, new CancelVerificationDto
        {
            Reason = "The requester withdrew it."
        });

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(VerificationStatus.Cancelled, cancelled.Value!.Status);

        // A request must never be left waiting on a check that is no longer happening.
        var request = await h.Db.Requests.SingleAsync();
        Assert.Equal(RequestStatus.InReview, request.Status);

        var approved = await h.Triage.DecideAsync(requestId, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve
        });
        Assert.True(approved.IsSuccess);
    }

    [Fact]
    public async Task Calling_a_check_off_needs_a_reason()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (_, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        var result = await h.Verifications.CancelAsync(
            verificationId, reviewer, new CancelVerificationDto { Reason = "  " });

        Assert.False(result.IsSuccess);
        Assert.Equal("verification.cancel_reason_required", result.Error!.Code);
    }

    // --- independent verification ------------------------------------------------------------

    [Fact]
    public async Task A_check_can_be_raised_with_no_request_and_no_task()
    {
        var (h, _, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var result = await h.Verifications.CreateAsync(reviewer, new CreateVerificationDto
        {
            Title = "Check whether Employee Salary generation form is functioning correctly",
            TargetType = VerificationTargetType.Form,
            TargetName = "Employee Salary generation",
            AssignToUserId = checker
        });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.RequestId);
        Assert.Equal(VerificationStatus.Assigned, result.Value.Status);

        // No fake lifecycle: nothing was created but the check itself.
        Assert.False(await h.Db.Tasks.AnyAsync());
        Assert.False(await h.Db.Requests.AnyAsync());
        Assert.False(await h.Db.QuickWork.AnyAsync());
    }

    [Fact]
    public async Task A_check_raised_without_a_checker_waits_rather_than_failing()
    {
        var (h, _, reviewer, _) = await ReadyAsync();
        using var _d = h;

        var result = await h.Verifications.CreateAsync(reviewer, NewCheck("Leave balance report"));

        Assert.True(result.IsSuccess);
        Assert.Equal(VerificationStatus.Requested, result.Value!.Status);
        Assert.Null(result.Value.AssignedToUserId);
    }

    [Fact]
    public async Task A_module_target_takes_a_real_foreign_key_and_is_checked()
    {
        var (h, _, reviewer, _) = await ReadyAsync();
        using var _d = h;

        var module = new Module { Name = "Payroll" };
        h.Db.Modules.Add(module);
        await h.Db.SaveChangesAsync();

        var ok = await h.Verifications.CreateAsync(reviewer, new CreateVerificationDto
        {
            Title = "Check payroll totals",
            TargetType = VerificationTargetType.Module,
            ModuleId = module.Id
        });
        Assert.True(ok.IsSuccess);
        Assert.Equal(module.Id, ok.Value!.ModuleId);
        Assert.Equal("Payroll", ok.Value.ModuleName);

        var missing = await h.Verifications.CreateAsync(reviewer, new CreateVerificationDto
        {
            Title = "Check something",
            TargetType = VerificationTargetType.Module,
            ModuleId = 9999
        });
        Assert.False(missing.IsSuccess);
        Assert.Equal("verification.module_not_found", missing.Error!.Code);

        var unnamed = await h.Verifications.CreateAsync(reviewer, new CreateVerificationDto
        {
            Title = "Check something",
            TargetType = VerificationTargetType.Module
        });
        Assert.False(unnamed.IsSuccess);
        Assert.Equal("verification.module_required", unnamed.Error!.Code);
    }

    [Fact]
    public async Task A_form_target_must_be_named_because_no_row_represents_it()
    {
        var (h, _, reviewer, _) = await ReadyAsync();
        using var _d = h;

        var result = await h.Verifications.CreateAsync(reviewer, new CreateVerificationDto
        {
            Title = "Check a form",
            TargetType = VerificationTargetType.Form
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("verification.target_name_required", result.Error!.Code);
    }

    // --- who may do what ---------------------------------------------------------------------

    [Fact]
    public async Task Only_the_assigned_checker_can_start_or_report()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (_, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        // The reviewer holds Verification.Create and could hold every permission there is. The
        // check is against the record, not the caller's grants.
        var started = await h.Verifications.StartAsync(verificationId, reviewer);
        Assert.False(started.IsSuccess);
        Assert.Equal("verification.not_checker", started.Error!.Code);

        var reported = await h.Verifications.RecordResultAsync(verificationId, reviewer, new RecordVerificationResultDto
        {
            Result = VerificationResult.WorkingCorrectly,
            Findings = "Looks fine to me."
        });
        Assert.False(reported.IsSuccess);
        Assert.Equal("verification.not_checker", reported.Error!.Code);
    }

    [Fact]
    public async Task A_check_cannot_be_given_to_somebody_who_cannot_carry_one_out()
    {
        var (h, requester, reviewer, _) = await ReadyAsync();
        using var _d = h;

        // The requester holds Request.Create and nothing that lets them investigate. Assigning to
        // them would produce a record that looks assigned and can never move.
        var result = await h.Verifications.CreateAsync(reviewer, new CreateVerificationDto
        {
            Title = "Check the salary form",
            TargetType = VerificationTargetType.Form,
            TargetName = "Salary",
            AssignToUserId = requester
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("verification.checker_cannot_work", result.Error!.Code);
    }

    [Fact]
    public async Task An_unclaimed_check_can_be_picked_up_by_a_checker()
    {
        var (h, _, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        // Raised with nobody on it - the case the "needs a checker" notification is sent for.
        var raised = await h.Verifications.CreateAsync(reviewer, NewCheck("Leave balance report"));
        Assert.Equal(VerificationStatus.Requested, raised.Value!.Status);

        var claimed = await h.Verifications.ClaimAsync(raised.Value.Id, checker);

        Assert.True(claimed.IsSuccess);
        Assert.Equal(checker, claimed.Value!.AssignedToUserId);
        Assert.Equal(VerificationStatus.Assigned, claimed.Value.Status);

        // And now they can actually do the work, which was the whole point.
        var started = await h.Verifications.StartAsync(raised.Value.Id, checker);
        Assert.True(started.IsSuccess);
    }

    [Fact]
    public async Task A_check_somebody_already_holds_cannot_be_taken()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var other = await h.CreateUserAsync("priya", roles: DefaultRoles.QC);
        var (_, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        // Taking work off somebody is a decision about two people's workloads. That goes through
        // assignment, which asks why - claiming is only for what nobody holds.
        var taken = await h.Verifications.ClaimAsync(verificationId, other.Id);

        Assert.False(taken.IsSuccess);
        Assert.Equal("verification.already_claimed", taken.Error!.Code);
    }

    [Fact]
    public async Task Claiming_your_own_check_twice_changes_nothing()
    {
        var (h, _, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var raised = await h.Verifications.CreateAsync(reviewer, NewCheck("Payslip export"));
        await h.Verifications.ClaimAsync(raised.Value!.Id, checker);

        var again = await h.Verifications.ClaimAsync(raised.Value.Id, checker);

        Assert.True(again.IsSuccess);
        Assert.Equal(checker, again.Value!.AssignedToUserId);
    }

    [Fact]
    public async Task Somebody_who_cannot_investigate_cannot_take_a_check_either()
    {
        var (h, requester, reviewer, _) = await ReadyAsync();
        using var _d = h;

        var raised = await h.Verifications.CreateAsync(reviewer, NewCheck("Payslip export"));

        // The requester holds Request.Create and nothing that lets them investigate. The endpoint
        // is gated on Verification.Work as well, but the service must not depend on that alone.
        var taken = await h.Verifications.ClaimAsync(raised.Value!.Id, requester);

        Assert.False(taken.IsSuccess);
        Assert.Equal("verification.checker_cannot_work", taken.Error!.Code);
    }

    [Fact]
    public async Task Moving_a_check_to_another_checker_needs_a_reason()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var other = await h.CreateUserAsync("priya", roles: DefaultRoles.QC);
        var (_, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        var withoutReason = await h.Verifications.AssignAsync(
            verificationId, reviewer, new AssignVerificationDto { AssignToUserId = other.Id });

        Assert.False(withoutReason.IsSuccess);
        Assert.Equal("verification.reassign_reason_required", withoutReason.Error!.Code);

        var withReason = await h.Verifications.AssignAsync(
            verificationId, reviewer, new AssignVerificationDto
            {
                AssignToUserId = other.Id,
                Reason = "Quentin is on leave."
            });

        Assert.True(withReason.IsSuccess);
        Assert.Equal(other.Id, withReason.Value!.AssignedToUserId);
    }

    [Fact]
    public async Task Reporting_requires_findings()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (_, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        var result = await h.Verifications.RecordResultAsync(verificationId, checker, new RecordVerificationResultDto
        {
            Result = VerificationResult.Inconclusive,
            Findings = "   "
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("verification.findings_required", result.Error!.Code);
    }

    [Fact]
    public async Task A_reported_check_cannot_be_reported_on_again()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (_, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        await h.Verifications.RecordResultAsync(verificationId, checker, new RecordVerificationResultDto
        {
            Result = VerificationResult.WorkingCorrectly,
            Findings = "Behaves as designed."
        });

        var again = await h.Verifications.RecordResultAsync(verificationId, checker, new RecordVerificationResultDto
        {
            Result = VerificationResult.IssueConfirmed,
            Findings = "Actually, no."
        });

        Assert.False(again.IsSuccess);
        Assert.Equal("verification.not_reportable", again.Error!.Code);
    }

    // --- visibility ---------------------------------------------------------------------------

    [Fact]
    public async Task Without_ViewAll_you_see_what_you_raised_and_what_you_hold()
    {
        var (h, _, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var stranger = await h.CreateUserAsync("wu", roles: DefaultRoles.Worker);

        var mine = await h.Verifications.CreateAsync(reviewer, NewCheck("Raised by the reviewer"));
        var theirs = await h.Verifications.CreateAsync(reviewer, new CreateVerificationDto
        {
            Title = "Given to the checker",
            TargetType = VerificationTargetType.Form,
            TargetName = "Salary",
            AssignToUserId = checker
        });

        var checkerSees = await h.Verifications.ListAsync(
            checker, NoPermissions, status: null, mineOnly: false, new PageQuery());
        Assert.Equal(theirs.Value!.Id, Assert.Single(checkerSees.Items).Id);

        var strangerSees = await h.Verifications.ListAsync(
            stranger.Id, NoPermissions, status: null, mineOnly: false, new PageQuery());
        Assert.Empty(strangerSees.Items);

        var everything = await h.Verifications.ListAsync(
            stranger.Id, ViewAll, status: null, mineOnly: false, new PageQuery());
        Assert.Equal(2, everything.TotalCount);
        Assert.Contains(everything.Items, v => v.Id == mine.Value!.Id);
    }

    [Fact]
    public async Task An_out_of_scope_check_reports_404_rather_than_403()
    {
        var (h, _, reviewer, _) = await ReadyAsync();
        using var _d = h;

        var stranger = await h.CreateUserAsync("wu", roles: DefaultRoles.Worker);
        var raised = await h.Verifications.CreateAsync(reviewer, NewCheck("Private"));

        var result = await h.Verifications.GetAsync(raised.Value!.Id, stranger.Id, NoPermissions);

        // "You may not see this" still confirms the number exists.
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task The_request_screen_carries_its_checks_and_their_findings()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, verificationId) = await RoutedAsync(h, requester, reviewer, checker);

        await h.Verifications.RecordResultAsync(verificationId, checker, new RecordVerificationResultDto
        {
            Result = VerificationResult.ConfigurationOrDataIssue,
            Findings = "The higher band is missing from this client's rate table."
        });

        var detail = await h.Requests.GetAsync(requestId);

        var check = Assert.Single(detail.Value!.Verifications);
        Assert.Equal(VerificationStatus.Completed, check.Status);
        Assert.Equal(VerificationResult.ConfigurationOrDataIssue, check.Result);
        Assert.Contains("rate table", check.Findings);
        // Words, not enum names — the reviewer reads this on the screen where they decide.
        Assert.Equal("Settings or data, not software", check.ResultLabel);
    }

    // --- what a requester is told ------------------------------------------------------------

    [Fact]
    public async Task A_requester_is_told_Being_Checked_not_the_internal_vocabulary()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (requestId, _) = await RoutedAsync(h, requester, reviewer, checker);

        var asRequester = await h.Requests.GetAsync(requestId, StatusAudience.Requester);
        Assert.Equal("checking", asRequester.Value!.ViewKey);
        Assert.Equal("Being Checked", asRequester.Value.ViewLabel);

        // A reviewer acts on the difference, so they get the real state.
        var asReviewer = await h.Requests.GetAsync(requestId, StatusAudience.Coordinator);
        Assert.Equal("verifying", asReviewer.Value!.ViewKey);
        Assert.Equal("Being verified", asReviewer.Value.ViewLabel);
    }

    // --- history and audit --------------------------------------------------------------------

    [Fact]
    public async Task The_whole_life_of_a_check_is_recorded_in_both_streams()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        var (_, verificationId) = await RoutedAsync(h, requester, reviewer, checker);
        await h.Verifications.StartAsync(verificationId, checker);
        var final = await h.Verifications.RecordResultAsync(verificationId, checker, new RecordVerificationResultDto
        {
            Result = VerificationResult.IssueConfirmed,
            Findings = "Reproduced."
        });

        // The account a person reads.
        var activity = final.Value!.Activity.Select(a => a.Type).ToList();
        Assert.Equal(
            new[] { "VerificationRequested", "VerificationAssigned", "VerificationStarted", "VerificationCompleted" },
            activity);

        // And the administrator's trail, kept separate.
        var audited = await h.Db.AuditLogs.Where(a => a.EntityType == "Verification")
            .Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditActions.VerificationRaised, audited);
        Assert.Contains(AuditActions.VerificationAssigned, audited);
        Assert.Contains(AuditActions.VerificationCompleted, audited);
    }

    [Fact]
    public async Task The_checker_is_notified_when_a_check_is_given_to_them()
    {
        var (h, requester, reviewer, checker) = await ReadyAsync();
        using var _d = h;

        await RoutedAsync(h, requester, reviewer, checker);

        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.RecipientUserId == checker && n.LinkEntityType == "Verification"));

        // And the person who asked hears it in their own words.
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.RecipientUserId == requester && n.Title.Contains("being checked")));
    }

    private static CreateVerificationDto NewCheck(string title) => new()
    {
        Title = title,
        TargetType = VerificationTargetType.Form,
        TargetName = title
    };
}
