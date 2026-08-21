# Workflow App — Architecture & System Design

## 1. Technology Stack

| Layer | Choice | Why |
|---|---|---|
| Backend | ASP.NET Core 8 Web API (C#) | Native IIS hosting, mature security, SignalR built-in |
| Real-time | SignalR | WebSockets + auto fallback + reconnection + group targeting |
| Frontend | Angular 17+ | Matches company's existing Angular skillset |
| ORM / Schema | EF Core 8 (code-first migrations) | Version-controlled schema; SQL Server provider |
| Heavy reads / reports | SQL Server stored procedures | Report + dashboard queries best expressed in T-SQL |
| Auth | ASP.NET Core Identity + JWT access/refresh tokens | Standard, IIS-friendly |
| DB | SQL Server 2019+ | Preferred datastore; source of truth |

## 2. Architecture Style: Modular Monolith, Layered

```
WorkflowApp.Api            → Controllers, SignalR hubs, middleware, DI composition root
WorkflowApp.Application    → Use-case services, DTOs, workflow rules, interfaces
WorkflowApp.Domain         → Entities, enums, value objects, state machine (no dependencies)
WorkflowApp.Infrastructure → EF Core DbContext, repositories, file storage, external services
```

Dependency direction: Api → Application → Domain; Infrastructure → Application/Domain.
Domain depends on nothing. This keeps business rules testable and swappable.

**Why not microservices / CQRS / vertical slices in v1:** the system is a single internal
app with one team and one database. Distributed complexity would add cost without benefit.
The layered modular monolith gives clean boundaries; modules can later be extracted if ever needed.

## 3. Core Domain Model — Three Distinct Session Concepts

The prompt's central insight: **auth session ≠ shift session ≠ task work session.** They are
modeled as three separate aggregates:

1. **Auth Session** — token lifecycle (login/logout, refresh). Security concern.
2. **Shift Session** — one per employee per working day. Attendance/availability.
3. **Task Work Session** — many per task; each start/resume opens one, each pause/stop closes it.

A user can be authenticated while on lunch (no active work session) while their shift is still open.

## 4. Request vs Task Separation

A `Request` is what someone submits. It is NOT executable work. Only after triage + approval
does the system create a `Task` (the executable unit). Rejected/duplicate requests never create
tasks, so they never pollute worker queues. `Task.RequestId` links back for traceability.

```
Request (intake)  --approved-->  Task (executable)  --> WorkSessions, QC, Closure
```

## 5. Workflow State Model

Task statuses (enforced transitions, not free-form):

```
Requested → AwaitingReview → (ClarificationRequired → AwaitingReview) → Approved
→ ReadyForAssignment → Assigned → ReadyToStart → InProgress
→ (Paused | Blocked → InProgress)
→ CompletedReadyForQC → QCReview → (QCFailedRework → InProgress | QCPassed)
→ ReadyForClosure → Closed

Cross-cutting: Cancelled, Deferred, OnHold, Duplicate, Reopened
```

Transitions are defined in `Domain/Workflow/TaskWorkflow.cs` as an allowed-transition map.
Each transition declares: source state(s), target state, required permission, whether a reason
is mandatory. The workflow engine rejects any transition not in the map — this is what makes
the system *enforce* the workflow rather than merely display statuses.

## 6. Permission Model

Permission-based, not just role-based. Roles are bundles of permissions. A user's effective
permissions = union of their roles' permissions. Server-side checks on every mutating endpoint
and every workflow transition. UI hiding is convenience only, never the security boundary.

Example permissions: `Request.Create`, `Request.Review`, `Task.Assign`, `Task.Start`,
`Task.QCReview`, `Task.Close`, `Admin.ManageUsers`, `Task.Override`.

## 7. Real-Time Design (SignalR)

Groups:
- `user:{userId}` — personal notifications, assignments
- `role:reviewer`, `role:assignment`, `role:qc` — queue updates
- `dashboard:management` — workforce + pipeline cards
- `task:{taskId}` — anyone viewing a task detail

Domain services raise integration events after a successful DB commit; a broadcaster maps events
to hub groups. **DB is source of truth**; SignalR only synchronizes clients — a dropped message
never corrupts state because clients re-fetch on reconnect.

## 8. Concurrency & Integrity

- `rowversion` (SQL `ROWVERSION`) on Task, Request, WorkSession, ShiftSession for optimistic concurrency.
- Single-active-work-session rule enforced by a filtered unique index + transactional check.
- Assignment guarded by concurrency token so two coordinators can't assign the same task.
- Idempotency keys on transition endpoints to swallow double-clicks / retries.
- All multi-step operations wrapped in a single EF Core transaction.

## 9. Audit & History (two separate streams)

- **Activity History** (`TaskActivity`) — business timeline shown inside a task (human-readable).
- **Audit Log** (`AuditLog`) — technical/security events (auth, permission change, override,
  attachment removal, config change) with before/after values, IP, device. Append-only;
  admins cannot silently delete.

## 10. File Storage

Files stored on disk (configurable root, per-environment), metadata + auth in DB. Never store
large binaries in transactional tables. Access always goes through an authorized endpoint.

## 11. Deployment

Windows Server + IIS + SQL Server. ASP.NET Core hosted in-process under IIS. Angular built to
static files served by IIS (or same host). Config via `appsettings.{Environment}.json` +
environment variables for secrets. EF Core migrations applied on deploy. HTTPS enforced.

## 12. Phase Plan

See `02-PHASE-PLAN.md`.
