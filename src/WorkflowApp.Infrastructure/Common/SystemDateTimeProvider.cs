using WorkflowApp.Application.Common.Interfaces;

namespace WorkflowApp.Infrastructure.Common;

/// <summary>The real clock. Tests substitute a fixed implementation.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
