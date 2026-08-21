using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Application.Tasks.Services;
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
    private readonly IAttachmentService _attachments;

    public TasksController(
        ITaskQueryService queries,
        ITaskWorkflowService workflow,
        ITaskAssignmentService assignment,
        IWorkSessionService sessions,
        IAttachmentService attachments)
    {
        _queries = queries;
        _workflow = workflow;
        _assignment = assignment;
        _sessions = sessions;
        _attachments = attachments;
    }

    // --- reading -------------------------------------------------------------------------

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] WorkTaskStatus? status,
        [FromQuery] Priority? priority,
        [FromQuery] long? assigneeUserId,
        [FromQuery] bool? unassigned,
        [FromQuery] bool openOnly = true,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var query = new TaskQuery
        {
            Status = status,
            Priority = priority,
            AssigneeUserId = assigneeUserId,
            Unassigned = unassigned,
            OpenOnly = openOnly,
            Search = search
        };

        return Ok(await _queries.ListAsync(query, new PageQuery { Page = page, PageSize = pageSize }, ct));
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

    // --- attachments ---------------------------------------------------------------------

    [HttpPost("{id:long}/attachments")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [ProducesResponseType(typeof(Application.Requests.Dtos.AttachmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload(long id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem(Error.Validation("attachment.empty", "No file was supplied."));

        await using var stream = file.OpenReadStream();

        return FromResult(await _attachments.UploadAsync(
            requestId: null, taskId: id, uploaderId: CurrentUserId,
            stream, file.FileName, file.ContentType, ct));
    }
}

public sealed record CompleteTaskDto(string? Resolution);
