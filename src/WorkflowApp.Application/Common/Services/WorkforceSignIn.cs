using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Common.Services;

/// <summary>
/// What it means for somebody to arrive — the workforce half of signing in.
///
/// A named rule rather than four lines inside <c>AuthService</c>, because it has to be applied from
/// more than one place: demo mode mints its own tokens instead of going through login, and the copy
/// that did not exist there is what left the entire demo cast stuck in
/// <see cref="WorkforceState.NotLoggedIn"/> — a state with no transition to
/// <see cref="WorkforceState.Available"/>, so no shift could be opened and no task timer could be
/// started. Anything that hands out a session applies this.
///
/// It stops at "logged in, no shift" deliberately. Authentication establishes an auth session and
/// nothing more; the shift is a separate act (see <c>ShiftService</c>), and re-authenticating
/// mid-shift must not disturb an already-open one — which is why the state only ever moves *out of*
/// NotLoggedIn and is otherwise left exactly where it was.
/// </summary>
public static class WorkforceSignIn
{
    public static void Apply(User user, IActivityLogger activity, DateTimeOffset now)
    {
        if (user.WorkforceState == WorkforceState.NotLoggedIn)
            user.WorkforceState = WorkforceState.LoggedInShiftNotStarted;

        activity.Record(
            user.Id,
            ActivityLabels.LoggedIn,
            // Null when they were already somewhere: the event says they signed in, and claiming it
            // moved them to a state it did not move them to would be a lie in the timeline.
            resultingState: user.WorkforceState == WorkforceState.LoggedInShiftNotStarted
                ? WorkforceState.LoggedInShiftNotStarted
                : null,
            occurredAt: now);
    }
}
