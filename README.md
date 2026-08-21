# WorkflowApp — Task Management & Workforce Workflow

Internal operations system enforcing: **Request → Review → Approval → Assignment →
Execution → QC → Closure**, with shift/attendance tracking and real-time updates.

## Stack
ASP.NET Core 8 Web API · SignalR · EF Core (SQL Server) · Angular (frontend, added later) ·
IIS deployment.

## Solution layout
```
WorkflowApp.sln
src/
  WorkflowApp.Domain          entities, enums, workflow state machine (no dependencies)
  WorkflowApp.Application      use-case services, permission catalog, transition validation
  WorkflowApp.Infrastructure   EF Core DbContext + configurations
  WorkflowApp.Api              host: controllers, SignalR hubs, DI, config
docs/
  01-ARCHITECTURE.md          full design rationale
  02-PHASE-PLAN.md            phase checklist + business rules + edge cases
```

## What's done (Phase 0)
- Layered project structure + solution + project references
- Domain entities: identity, workforce (shift/activity), requests, tasks, sessions, QC,
  history, comments, dependencies, scope changes, notifications, audit
- Enums incl. the enforced task status set
- **Workflow state machine** (`Domain/Workflow/TaskWorkflow.cs`) — the allowed-transition map
- **Transition validation service** (`Application/Common/TaskTransitionService.cs`) — pure,
  testable, checks workflow + permission + reason
- Permission catalog + default role→permission map
- DbContext + EF configurations, including two critical DB constraints:
  - `UX_WorkSession_OneActivePerUser` — one active work session per user
  - `UX_ShiftSession_OneOpenPerUser` — one open shift per user
- Program.cs host skeleton, appsettings for base/dev

## First steps in Claude Code
```bash
# 1. Restore & build (verifies the scaffold compiles)
dotnet restore
dotnet build

# 2. Create the initial migration and database
cd src/WorkflowApp.Api
dotnet ef migrations add InitialCreate --project ../WorkflowApp.Infrastructure --startup-project .
dotnet ef database update --project ../WorkflowApp.Infrastructure --startup-project .

# 3. Run
dotnet run
```
If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`.

## Then continue with Phase 1 (Identity & Authorization) from docs/02-PHASE-PLAN.md.

## Non-negotiable business rules (see phase plan for full list)
1. A request never auto-becomes a task.
2. One active primary work session per user.
3. No status transition outside `TaskWorkflow.Transitions`.
4. Every mutating transition is permission-checked server-side.
5. Reason mandatory for: reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only.
7. DB is source of truth; SignalR only notifies.
