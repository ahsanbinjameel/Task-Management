using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// The executable side of the pipeline: the workflow engine, assignment and the work timer.
/// Tasks are never created here — they only come from an approved request.
/// </summary>
public sealed class TasksController : ApiControllerBase
{
    private readonly ITaskQueryService _queries;
    private readonly ITaskWorkflowService _workflow;
    private readonly ITaskAssignmentService _assignment;
    private readonly IWorkSessionService _sessions;
    private readonly IQCService _qc;
    private readonly IClosureService _closure;
    private readonly ITaskCommentService _comments;
    private readonly ITaskDependencyService _dependencies;
    private readonly IScopeChangeService _scope;
    private readonly ITaskCreationService _creation;
    private readonly IAttachmentService _attachments;

    public TasksController(
        ITaskQueryService queries,
        ITaskWorkflowService workflow,
        ITaskAssignmentService assignment,
        IWorkSessionService sessions,
        IQCService qc,
        IClosureService closure,
        ITaskCommentService comments,
        ITaskDependencyService dependencies,
        IScopeChangeService scope,
        ITaskCreationService creation,
        IAttachmentService attachments)
    {
        _queries = queries;
        _workflow = workflow;
        _assignment = assignment;
        _sessions = sessions;
        _qc = qc;
        _closure = closure;
        _comments = comments;
        _dependencies = dependencies;
        _scope = scope;
        _creation = creation;
        _attachments = attachments;
    }

    // --- reading -------------------------------------------------------------------------

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] WorkTaskStatus? status,
        [FromQuery] string? view,
        [FromQuery] Priority? priority,
        [FromQuery] long? assigneeUserId,
        [FromQuery] bool? unassigned,
        [FromQuery] long? clientId,
        [FromQuery] bool openOnly = true,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        // The grid's filter row — see ColumnFilters.
        [FromQuery(Name = "col")] Dictionary<string, string?>? col = null,
        CancellationToken ct = default)
    {
        // Anyone who coordinates, reviews, checks or reports on work needs the whole picture.
        // Everyone else sees the work they are part of.
        var seesEverything =
            HasPermission(Permissions.TaskAssign)
            || HasPermission(Permissions.TaskReview)
            || HasPermission(Permissions.TaskQCReview)
            || HasPermission(Permissions.WorkforceViewAll)
            || HasPermission(Permissions.RequestViewAll)
            || HasPermission(Permissions.DashboardManagement);

        var query = new TaskQuery
        {
            VisibleToUserId = seesEverything ? null : CurrentUserId,
            Status = status,
            View = view,
            Audience = Audience,
            Priority = priority,
            AssigneeUserId = assigneeUserId,
            Unassigned = unassigned,
            ClientId = clientId,
            OpenOnly = openOnly,
            Search = search,
            SortBy = sortBy,
            SortDescending = sortDescending,
            Columns = new ColumnFilters(col)
        };

        return Ok(await _queries.ListAsync(query, new PageQuery { Page = page, PageSize = pageSize }, ct));
    }

    /// <summary>Counts for the status tiles above the list, under the caller's own visibility.</summary>
    /// <summary>
    /// What each column's filter can still be narrowed by, given the others — the "like Excel"
    /// behaviour. Same scoping as the list, so it can never offer a value the caller cannot see.
    /// </summary>
    [HttpGet("filter-options")]
    [ProducesResponseType(typeof(FilterOptionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> FilterOptions(
        [FromQuery] string? view,
        [FromQuery] bool openOnly = true,
        [FromQuery(Name = "col")] Dictionary<string, string?>? col = null,
        CancellationToken ct = default)
    {
        var seesEverything =
            HasPermission(Permissions.TaskAssign)
            || HasPermission(Permissions.TaskReview)
            || HasPermission(Permissions.TaskQCReview)
            || HasPermission(Permissions.WorkforceViewAll)
            || HasPermission(Permissions.RequestViewAll)
            || HasPermission(Permissions.DashboardManagement);

        return Ok(await _queries.FilterOptionsAsync(new TaskQuery
        {
            VisibleToUserId = seesEverything ? null : CurrentUserId,
            View = view,
            Audience = Audience,
            OpenOnly = openOnly,
            Columns = new ColumnFilters(col),
        }, ct));
    }

    [HttpGet("status-counts")]
    [ProducesResponseType(typeof(IReadOnlyList<StatusCountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> StatusCounts(
        [FromQuery] long? clientId,
        [FromQuery] string? search,
        [FromQuery] bool openOnly = true,
        CancellationToken ct = default)
    {
        var seesEverything =
            HasPermission(Permissions.TaskAssign)
            || HasPermission(Permissions.TaskReview)
            || HasPermission(Permissions.TaskQCReview)
            || HasPermission(Permissions.WorkforceViewAll)
            || HasPermission(Permissions.RequestViewAll)
            || HasPermission(Permissions.DashboardManagement);

        return Ok(await _queries.StatusCountsAsync(new TaskQuery
        {
            VisibleToUserId = seesEverything ? null : CurrentUserId,
            Audience = Audience,
            ClientId = clientId,
            Search = search,
            OpenOnly = openOnly,
        }, ct));
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => FromResult(await _queries.GetAsync(id, ct));

    /// <summary>The caller's own ordered work queue.</summary>
    [HttpGet("my-queue")]
    [ProducesResponseType(typeof(IReadOnlyList<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyQueue(CancellationToken ct)
        => Ok(await _queries.MyQueueAsync(CurrentUserId, ct));

    /// <summary>Approved work waiting for an assignee.</summary>
    [HttpGet("assignment-queue")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(PagedResult<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignmentQueue(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await _queries.AssignmentQueueAsync(new PageQuery { Page = page, PageSize = pageSize }, ct));

    /// <summary>Open load per assignee, for capacity decisions.</summary>
    [HttpGet("workload")]
    [HasPermission(Permissions.WorkforceViewAll)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkloadDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Workload(CancellationToken ct)
        => Ok(await _queries.WorkloadAsync(ct));

    /// <summary>Active people who can be given work — fills the assign dialog.</summary>
    [HttpGet("assignable-users")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(IReadOnlyList<AssignableUserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignableUsers(CancellationToken ct)
        => Ok(await _queries.AssignableUsersAsync(ct));

    /// <summary>Configured pause reasons, for the pause/block dialogs.</summary>
    [HttpGet("pause-reasons")]
    [ProducesResponseType(typeof(IReadOnlyList<PauseReasonDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PauseReasons(CancellationToken ct)
        => Ok(await _queries.PauseReasonsAsync(ct));

    /// <summary>The caller's currently running work session, if any.</summary>
    [HttpGet("active-session")]
    [ProducesResponseType(typeof(WorkSessionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActiveSession(CancellationToken ct)
    {
        var session = await _sessions.ActiveSessionAsync(CurrentUserId, ct);
        return session is null ? NoContent() : Ok(session);
    }

    // --- workflow ------------------------------------------------------------------------

    /// <summary>
    /// Moves the task to a new status. The workflow map, the caller's permissions and the reason
    /// requirement are all enforced server-side.
    /// </summary>
    [HttpPost("{id:long}/transition")]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Transition(long id, [FromBody] TransitionTaskDto dto, CancellationToken ct)
        => FromResult(await _workflow.TransitionAsync(id, CurrentUserId, dto, ct));

    // --- assignment ----------------------------------------------------------------------

    [HttpPut("{id:long}/assignee")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(long id, [FromBody] AssignTaskDto dto, CancellationToken ct)
        => FromResult(await _assignment.AssignAsync(id, CurrentUserId, dto, ct));

    [HttpPost("{id:long}/collaborators")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddCollaborator(long id, [FromBody] AddCollaboratorDto dto, CancellationToken ct)
        => FromResult(await _assignment.AddCollaboratorAsync(id, dto.UserId, CurrentUserId, ct));

    [HttpDelete("{id:long}/collaborators/{userId:long}")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveCollaborator(long id, long userId, CancellationToken ct)
        => FromResult(await _assignment.RemoveCollaboratorAsync(id, userId, ct));

    /// <summary>Sets the reviewer and QC owner. QC may not be the assignee.</summary>
    [HttpPut("{id:long}/roles")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRoles(long id, [FromBody] SetTaskRolesDto dto, CancellationToken ct)
        => FromResult(await _assignment.SetRolesAsync(id, dto, ct));

    [HttpPatch("{id:long}")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDetails(long id, [FromBody] UpdateTaskDetailsDto dto, CancellationToken ct)
        => FromResult(await _assignment.UpdateDetailsAsync(id, CurrentUserId, dto, ct));

    /// <summary>Rewrites the caller's own queue order.</summary>
    [HttpPut("my-queue/order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReorderQueue([FromBody] ReorderQueueDto dto, CancellationToken ct)
        => FromResult(await _assignment.ReorderQueueAsync(CurrentUserId, dto.TaskIdsInOrder, ct));

    // --- work sessions -------------------------------------------------------------------

    /// <summary>Starts the timer. Requires an open shift and no other running session.</summary>
    [HttpPost("{id:long}/start")]
    [HasPermission(Permissions.TaskWork)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(long id, CancellationToken ct)
        => FromResult(await _sessions.StartAsync(id, CurrentUserId, ct));

    [HttpPost("{id:long}/pause")]
    [HasPermission(Permissions.TaskWork)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause(long id, [FromBody] StopWorkDto dto, CancellationToken ct)
        => FromResult(await _sessions.PauseAsync(id, CurrentUserId, dto, ct));

    [HttpPost("{id:long}/block")]
    [HasPermission(Permissions.TaskWork)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Block(long id, [FromBody] StopWorkDto dto, CancellationToken ct)
        => FromResult(await _sessions.BlockAsync(id, CurrentUserId, dto, ct));

    /// <summary>Finishes the work. Lands in CompletedReadyForQC — never straight to Closed.</summary>
    [HttpPost("{id:long}/complete")]
    [HasPermission(Permissions.TaskWork)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(long id, [FromBody] CompleteTaskDto? dto, CancellationToken ct)
        => FromResult(await _sessions.CompleteAsync(id, CurrentUserId, dto?.Resolution, ct));

    /// <summary>
    /// Emergency switch: pauses whatever is running and starts the urgent task, in one atomic
    /// operation so the single-active-session rule is never briefly violated.
    /// </summary>
    [HttpPost("interrupt")]
    [HasPermission(Permissions.TaskWork)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Interrupt([FromBody] InterruptDto dto, CancellationToken ct)
        => FromResult(await _sessions.InterruptAsync(CurrentUserId, dto, ct));

    // --- QC ------------------------------------------------------------------------------

    /// <summary>Completed work waiting for a reviewer.</summary>
    [HttpGet("qc-queue")]
    [HasPermission(Permissions.TaskQCReview)]
    [ProducesResponseType(typeof(PagedResult<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> QCQueue(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await _qc.QueueAsync(new PageQuery { Page = page, PageSize = pageSize }, ct));

    /// <summary>Claims the task for QC. The assignee may not review their own work.</summary>
    [HttpPost("{id:long}/qc/start")]
    [HasPermission(Permissions.TaskQCReview)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartQC(long id, CancellationToken ct)
        => FromResult(await _qc.StartReviewAsync(id, CurrentUserId, ct));

    /// <summary>
    /// Records a QC verdict. Passing requires every acceptance criterion to be evaluated and met;
    /// failing requires comments and sends the task back for rework.
    /// </summary>
    [HttpPost("{id:long}/qc/review")]
    [HasPermission(Permissions.TaskQCReview)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitQC(long id, [FromBody] SubmitQCReviewDto dto, CancellationToken ct)
        => FromResult(await _qc.SubmitAsync(id, CurrentUserId, dto, ct));

    /// <summary>Every QC attempt on the task, oldest first.</summary>
    [HttpGet("{id:long}/qc")]
    [ProducesResponseType(typeof(IReadOnlyList<QCReviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> QCHistory(long id, CancellationToken ct)
        => Ok(await _qc.HistoryAsync(id, ct));

    /// <summary>The task's acceptance criteria with the latest verdict against each.</summary>
    [HttpGet("{id:long}/acceptance-criteria")]
    [ProducesResponseType(typeof(AcceptanceCriteriaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Criteria(long id, CancellationToken ct)
        => FromResult(await _qc.CriteriaAsync(id, ct));

    // --- closure -------------------------------------------------------------------------

    /// <summary>What still stands between this task and closure.</summary>
    [HttpGet("{id:long}/closure-check")]
    [HasPermission(Permissions.TaskClose)]
    [ProducesResponseType(typeof(ClosureChecklistDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ClosureCheck(long id, CancellationToken ct)
        => FromResult(await _closure.EvaluateAsync(id, ct));

    /// <summary>Closes the task, once every closure requirement is satisfied.</summary>
    [HttpPost("{id:long}/close")]
    [HasPermission(Permissions.TaskClose)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close(long id, [FromBody] CloseTaskDto? dto, CancellationToken ct)
        => FromResult(await _closure.CloseAsync(id, CurrentUserId, dto ?? new CloseTaskDto(), ct));

    // --- requester acceptance (PRODUCT-CORE §7) ------------------------------------------------
    //
    // Neither carries a permission attribute, deliberately. The rule is "you are the person who
    // asked for this work", which depends on the task rather than on the caller's authority — the
    // same shape as completion proof and a checker's evidence, and not something a policy could
    // express. `ClosureService` refuses anyone else with `acceptance.not_requester`.
    //
    // This is the last hop of the relay the product exists to remove: today it is the requester
    // telling Ahsan on WhatsApp, and Ahsan updating a sheet.

    /// <summary>The requester confirming the work really is fixed. Closes it in their name.</summary>
    [HttpPost("{id:long}/accept")]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(long id, [FromBody] AcceptFixDto? dto, CancellationToken ct)
        => FromResult(await _closure.AcceptAsync(id, CurrentUserId, dto ?? new AcceptFixDto(), ct));

    /// <summary>The requester saying it is still not fixed. Sends it back with their words.</summary>
    [HttpPost("{id:long}/reject")]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(long id, [FromBody] RejectFixDto dto, CancellationToken ct)
        => FromResult(await _closure.RejectAsync(id, CurrentUserId, dto, ct));

    /// <summary>Puts closed work back in play. Always requires a reason.</summary>
    [HttpPost("{id:long}/reopen")]
    [HasPermission(Permissions.TaskReopen)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen(long id, [FromBody] ReopenTaskDto dto, CancellationToken ct)
        => FromResult(await _closure.ReopenAsync(id, CurrentUserId, dto, ct));

    // --- comments ------------------------------------------------------------------------

    /// <summary>
    /// The comments the caller may see. A requester viewing their own task gets only the
    /// customer-facing ones; the filtering is server-side, not a UI convention.
    /// </summary>
    [HttpGet("{id:long}/comments")]
    [ProducesResponseType(typeof(IReadOnlyList<TaskCommentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Comments(long id, CancellationToken ct)
        => FromResult(await _comments.ListAsync(id, CurrentUserId, ct));

    [HttpPost("{id:long}/comments")]
    [ProducesResponseType(typeof(TaskCommentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddComment(long id, [FromBody] AddCommentDto dto, CancellationToken ct)
        => FromResult(await _comments.AddAsync(id, CurrentUserId, dto, ct));

    // --- dependencies --------------------------------------------------------------------

    [HttpGet("{id:long}/dependencies")]
    [ProducesResponseType(typeof(TaskDependencyGraphDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Dependencies(long id, CancellationToken ct)
        => FromResult(await _dependencies.GraphAsync(id, ct));

    /// <summary>Declares a dependency. Circular ordering is refused.</summary>
    [HttpPost("{id:long}/dependencies")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(TaskDependencyGraphDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddDependency(long id, [FromBody] AddDependencyDto dto, CancellationToken ct)
        => FromResult(await _dependencies.AddAsync(id, CurrentUserId, dto, ct));

    [HttpDelete("{id:long}/dependencies/{dependencyId:long}")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(TaskDependencyGraphDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveDependency(long id, long dependencyId, CancellationToken ct)
        => FromResult(await _dependencies.RemoveAsync(id, dependencyId, CurrentUserId, ct));

    // --- subtasks ------------------------------------------------------------------------

    [HttpGet("{id:long}/subtasks")]
    [ProducesResponseType(typeof(PagedResult<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Subtasks(long id, CancellationToken ct)
        => Ok(await _queries.ListAsync(
            new TaskQuery { ParentTaskId = id }, new PageQuery { PageSize = 100 }, ct));

    /// <summary>
    /// Breaks the task down. The subtask is a task in its own right, with its own number, assignee,
    /// timer and history — and the parent cannot close until it is finished.
    /// </summary>
    [HttpPost("{id:long}/subtasks")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateSubtask(long id, [FromBody] CreateSubtaskDto dto, CancellationToken ct)
    {
        var created = await _creation.CreateSubtaskAsync(id, CurrentUserId, dto, ct);
        return created.IsFailure
            ? Problem(created.Error!)
            : FromResult(await _queries.GetAsync(created.Value!.Id, ct));
    }

    // --- scope changes -------------------------------------------------------------------

    [HttpGet("{id:long}/scope-changes")]
    [ProducesResponseType(typeof(IReadOnlyList<ScopeChangeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ScopeChanges(long id, CancellationToken ct)
        => Ok(await _scope.ListAsync(id, ct));

    /// <summary>Records that the work has changed shape. The task's numbers do not move yet.</summary>
    [HttpPost("{id:long}/scope-changes")]
    [ProducesResponseType(typeof(ScopeChangeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestScopeChange(
        long id, [FromBody] RequestScopeChangeDto dto, CancellationToken ct)
        => FromResult(await _scope.RequestAsync(id, CurrentUserId, dto, ct));

    /// <summary>Accepts the change and applies its estimate and deadline impact.</summary>
    [HttpPost("scope-changes/{scopeChangeId:long}/approve")]
    [HasPermission(Permissions.TaskApprove)]
    [ProducesResponseType(typeof(ScopeChangeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveScopeChange(long scopeChangeId, CancellationToken ct)
        => FromResult(await _scope.ApproveAsync(scopeChangeId, CurrentUserId, ct));

    // --- attachments ---------------------------------------------------------------------

    /// <summary>
    /// Attaches a file to the task.
    ///
    /// <paramref name="kind"/> says what it is <i>for</i>: context by default,
    /// <c>CompletionProof</c> for the evidence the responsible person supplies when marking the
    /// work finished, <c>QCEvidence</c> for what a checker attaches to a verdict. The kinds are
    /// authorized in the service, not here — who may claim to have proved something depends on the
    /// task, not only on a permission.
    ///
    /// Quality-check evidence is uploaded <b>before</b> the verdict: the attempt does not exist
    /// until the verdict is recorded, so the files are staged and claimed by it. A verdict that is
    /// refused therefore leaves them staged for the retry rather than stranding them.
    /// </summary>
    [HttpPost("{id:long}/attachments")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [ProducesResponseType(typeof(Application.Requests.Dtos.AttachmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload(
        long id, IFormFile file, CancellationToken ct,
        [FromQuery] AttachmentKind kind = AttachmentKind.General)
    {
        if (file is null || file.Length == 0)
            return Problem(Error.Validation("attachment.empty", "No file was supplied."));

        await using var stream = file.OpenReadStream();

        return FromResult(await _attachments.UploadAsync(
            requestId: null, taskId: id, uploaderId: CurrentUserId,
            stream, file.FileName, file.ContentType, ct, batchId: null, kind: kind));
    }
}

public sealed record CompleteTaskDto(string? Resolution);
