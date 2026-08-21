using WorkflowApp.Domain.Common;

namespace WorkflowApp.Domain.Entities.Requests;

public class Department : BaseEntity
{
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}

public class Team : BaseEntity
{
    public string Name { get; set; } = default!;
    public long? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Client : BaseEntity
{
    public string Name { get; set; } = default!;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Project : BaseEntity
{
    public string Name { get; set; } = default!;
    public string? Code { get; set; }
    public long? ClientId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Module : BaseEntity
{
    public string Name { get; set; } = default!;
    public long? ProjectId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Configurable pause reasons (admin-managed). Some require a comment.</summary>
public class PauseReason : BaseEntity
{
    public string Name { get; set; } = default!;      // e.g. "Waiting for client"
    public bool RequiresComment { get; set; }
    public bool IsBlocker { get; set; }               // maps pause vs blocked semantics
    public bool IsActive { get; set; } = true;
}
