using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Services;

public interface ITaskDependencyService
{
    Task<Result<TaskDependencyGraphDto>> GraphAsync(long taskId, CancellationToken ct = default);

    Task<Result<TaskDependencyGraphDto>> AddAsync(
        long taskId, long actingUserId, AddDependencyDto request, CancellationToken ct = default);

    Task<Result<TaskDependencyGraphDto>> RemoveAsync(
        long taskId, long dependencyId, long actingUserId, CancellationToken ct = default);

    /// <summary>
    /// Task numbers currently holding this task up: unfinished work it depends on, or unfinished
    /// work that declares it blocks this task. Empty means nothing is in the way.
    /// </summary>
    Task<IReadOnlyList<string>> BlockersAsync(long taskId, CancellationToken ct = default);
}

/// <summary>
/// The dependency graph between tasks.
///
/// Only two of the five <see cref="DependencyType"/> values impose an <b>order</b>: <c>DependsOn</c>
/// (the other task must finish first) and <c>Blocks</c> (this one must). Those two are the ones that
/// can deadlock a plan, so they are the only ones cycle-checked, and the only ones that produce a
/// blocked signal. <c>Related</c> and <c>Duplicate</c> are cross-references and impose nothing.
///
/// <see cref="DependencyType.ParentChild"/> is deliberately rejected here: parentage already lives on
/// <c>WorkTask.ParentTaskId</c>, and two places to record the same fact is one too many.
/// </summary>
public sealed class TaskDependencyService : ITaskDependencyService
{
    private static readonly WorkTaskStatus[] TerminalStatuses =
    {
        WorkTaskStatus.Closed, WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate
    };

    private readonly IWorkflowDbContext _db;
    private readonly IDateTimeProvider _clock;

    public TaskDependencyService(IWorkflowDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<TaskDependencyGraphDto>> GraphAsync(long taskId, CancellationToken ct = default)
    {
        if (!await _db.Tasks.AnyAsync(t => t.Id == taskId, ct))
            return Result<TaskDependencyGraphDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        return Result<TaskDependencyGraphDto>.Success(await BuildGraphAsync(taskId, ct));
    }

    public async Task<Result<TaskDependencyGraphDto>> AddAsync(
        long taskId, long actingUserId, AddDependencyDto request, CancellationToken ct = default)
    {
        var task = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == taskId).Select(t => new { t.Id, t.TaskNumber }).FirstOrDefaultAsync(ct);

        if (task is null)
            return Result<TaskDependencyGraphDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        var related = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == request.RelatedTaskId)
            .Select(t => new { t.Id, t.TaskNumber }).FirstOrDefaultAsync(ct);

        if (related is null)
            return Result<TaskDependencyGraphDto>.Failure(Error.NotFound(
                "dependency.related_not_found", "The related task does not exist."));

        if (request.RelatedTaskId == taskId)
            return Result<TaskDependencyGraphDto>.Failure(Error.Validation(
                "dependency.self_reference", "A task cannot depend on itself."));

        if (request.Type == DependencyType.ParentChild)
            return Result<TaskDependencyGraphDto>.Failure(Error.Validation(
                "dependency.use_subtasks",
                "Parent/child is recorded by creating a subtask, not as a dependency."));

        if (await _db.TaskDependencies.AnyAsync(
                d => d.TaskId == taskId && d.RelatedTaskId == request.RelatedTaskId && d.Type == request.Type, ct))
        {
            return Result<TaskDependencyGraphDto>.Failure(Error.Conflict(
                "dependency.duplicate", "That dependency is already recorded."));
        }

        if (IsOrdering(request.Type))
        {
            var (before, after) = Edge(taskId, request.RelatedTaskId, request.Type);

            // The new edge says `before` must finish before `after`. If `after` can already reach
            // `before`, adding it closes a loop that no order of work could ever satisfy.
            if (await CanReachAsync(after, before, ct))
                return Result<TaskDependencyGraphDto>.Failure(Error.Conflict(
                    "dependency.cycle",
                    $"That would create a circular dependency between {task.TaskNumber} and {related.TaskNumber}."));
        }

        _db.TaskDependencies.Add(new TaskDependency
        {
            TaskId = taskId,
            RelatedTaskId = request.RelatedTaskId,
            Type = request.Type
        });

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = taskId,
            Type = ActivityType.DependencyAdded,
            ActorUserId = actingUserId,
            OccurredAt = _clock.UtcNow,
            Description = $"Dependency added: {task.TaskNumber} {Describe(request.Type)} {related.TaskNumber}."
        });

        await _db.SaveChangesAsync(ct);
        return Result<TaskDependencyGraphDto>.Success(await BuildGraphAsync(taskId, ct));
    }

    public async Task<Result<TaskDependencyGraphDto>> RemoveAsync(
        long taskId, long dependencyId, long actingUserId, CancellationToken ct = default)
    {
        var dependency = await _db.TaskDependencies
            .FirstOrDefaultAsync(d => d.Id == dependencyId && d.TaskId == taskId, ct);

        if (dependency is null)
            return Result<TaskDependencyGraphDto>.Failure(Error.NotFound(
                "dependency.not_found", "That dependency is not recorded on this task."));

        var numbers = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == taskId || t.Id == dependency.RelatedTaskId)
            .ToDictionaryAsync(t => t.Id, t => t.TaskNumber, ct);

        _db.TaskDependencies.Remove(dependency);

        // The edge itself is not history, but the fact that somebody removed it is.
        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = taskId,
            Type = ActivityType.DependencyRemoved,
            ActorUserId = actingUserId,
            OccurredAt = _clock.UtcNow,
            Description = $"Dependency removed: {numbers.GetValueOrDefault(taskId)} " +
                          $"{Describe(dependency.Type)} {numbers.GetValueOrDefault(dependency.RelatedTaskId)}."
        });

        await _db.SaveChangesAsync(ct);
        return Result<TaskDependencyGraphDto>.Success(await BuildGraphAsync(taskId, ct));
    }

    public async Task<IReadOnlyList<string>> BlockersAsync(long taskId, CancellationToken ct = default)
    {
        // Blocking predecessors arrive two ways: this task declared DependsOn, or the other task
        // declared Blocks. Both mean the same thing and both have to be checked.
        var dependsOn = await _db.TaskDependencies.AsNoTracking()
            .Where(d => d.TaskId == taskId && d.Type == DependencyType.DependsOn)
            .Select(d => d.RelatedTaskId)
            .ToListAsync(ct);

        var blockedBy = await _db.TaskDependencies.AsNoTracking()
            .Where(d => d.RelatedTaskId == taskId && d.Type == DependencyType.Blocks)
            .Select(d => d.TaskId)
            .ToListAsync(ct);

        var predecessors = dependsOn.Concat(blockedBy).Distinct().ToList();
        if (predecessors.Count == 0) return Array.Empty<string>();

        return await _db.Tasks.AsNoTracking()
            .Where(t => predecessors.Contains(t.Id) && !TerminalStatuses.Contains(t.Status))
            .OrderBy(t => t.TaskNumber)
            .Select(t => t.TaskNumber)
            .ToListAsync(ct);
    }

    // --- helpers -------------------------------------------------------------------------

    private static bool IsOrdering(DependencyType type) =>
        type is DependencyType.DependsOn or DependencyType.Blocks;

    /// <summary>Normalises an edge to (must finish first, must wait).</summary>
    private static (long Before, long After) Edge(long taskId, long relatedTaskId, DependencyType type) =>
        type == DependencyType.DependsOn ? (relatedTaskId, taskId) : (taskId, relatedTaskId);

    /// <summary>
    /// Breadth-first walk of the ordering edges: can <paramref name="from"/> reach
    /// <paramref name="target"/> by following "must finish before" links?
    /// </summary>
    private async Task<bool> CanReachAsync(long from, long target, CancellationToken ct)
    {
        var edges = await _db.TaskDependencies.AsNoTracking()
            .Where(d => d.Type == DependencyType.DependsOn || d.Type == DependencyType.Blocks)
            .Select(d => new { d.TaskId, d.RelatedTaskId, d.Type })
            .ToListAsync(ct);

        var successors = edges
            .Select(e => Edge(e.TaskId, e.RelatedTaskId, e.Type))
            .GroupBy(e => e.Before)
            .ToDictionary(g => g.Key, g => g.Select(e => e.After).ToList());

        var seen = new HashSet<long> { from };
        var queue = new Queue<long>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target) return true;

            if (!successors.TryGetValue(current, out var next)) continue;

            foreach (var n in next.Where(seen.Add))
                queue.Enqueue(n);
        }

        return false;
    }

    private async Task<TaskDependencyGraphDto> BuildGraphAsync(long taskId, CancellationToken ct)
    {
        var edges = await _db.TaskDependencies.AsNoTracking()
            .Where(d => d.TaskId == taskId || d.RelatedTaskId == taskId)
            .ToListAsync(ct);

        var otherIds = edges
            .Select(d => d.TaskId == taskId ? d.RelatedTaskId : d.TaskId)
            .Distinct().ToList();

        var others = await _db.Tasks.AsNoTracking()
            .Where(t => otherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.TaskNumber, t.Title, t.Status })
            .ToDictionaryAsync(t => t.Id, t => t, ct);

        var blockers = await BlockersAsync(taskId, ct);

        TaskDependencyDto ToDto(TaskDependency d, bool outgoing)
        {
            var otherId = outgoing ? d.RelatedTaskId : d.TaskId;
            others.TryGetValue(otherId, out var other);

            // An incoming DependsOn means somebody is waiting on us, not the other way round.
            var isBlocking = other is not null
                             && blockers.Contains(other.TaskNumber)
                             && ((outgoing && d.Type == DependencyType.DependsOn)
                                 || (!outgoing && d.Type == DependencyType.Blocks));

            return new TaskDependencyDto(
                d.Id, d.TaskId, d.RelatedTaskId,
                other?.TaskNumber ?? "(deleted)", other?.Title ?? "(deleted)",
                other?.Status ?? WorkTaskStatus.Cancelled, d.Type, isBlocking);
        }

        return new TaskDependencyGraphDto(
            taskId,
            edges.Where(d => d.TaskId == taskId).Select(d => ToDto(d, outgoing: true)).ToList(),
            edges.Where(d => d.RelatedTaskId == taskId).Select(d => ToDto(d, outgoing: false)).ToList(),
            blockers.Count > 0,
            blockers);
    }

    private static string Describe(DependencyType type) => type switch
    {
        DependencyType.DependsOn => "depends on",
        DependencyType.Blocks => "blocks",
        DependencyType.Duplicate => "duplicates",
        _ => "relates to"
    };
}
