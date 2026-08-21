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

## Phase 0 — Foundation (complete)
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

## Phase 1 — Identity & Authorization (code complete)
- Custom identity tables (`User`/`Role`/`Permission`/`UserRole`/`RolePermission`) with ASP.NET
  Core Identity's PBKDF2 password hashing behind an adapter
- JWT access tokens carrying role + `permission` claims; opaque refresh tokens stored **hashed**,
  rotated on every use, with token-reuse detection that revokes the whole family
- Endpoints: `POST /api/auth/login|refresh|logout|change-password`, `GET /api/auth/me`,
  `GET|POST /api/users`, `PUT /api/users/{id}/active|roles`, `POST /api/users/{id}/reset-password`,
  `GET /api/roles`, `GET /api/roles/permissions`, `GET /health`
- Permission-based authorization: `[HasPermission(Permissions.X)]` + a policy provider that
  materialises `perm:{key}` policies on demand — no per-permission startup registration
- Account protection: login-attempt log, failed-count lockout, IP-partitioned rate limiting on
  the credential endpoints
- Idempotent seeder: permission catalog, system roles + grants, pause reasons, bootstrap admin
- `AuditableEntityInterceptor` stamps CreatedAt/UpdatedAt/By on every save
- Global `ProblemDetails` exception middleware (workflow violations → 400, concurrency → 409)
- `InitialCreate` migration + reviewable idempotent script at `scripts/sql/`

## Phase 2 — Shift & Workforce States (code complete)
- **Workforce state machine** (`Domain/Workflow/WorkforceStateMachine.cs`) — the availability
  counterpart to `TaskWorkflow`. Each transition carries the label written to the timeline
- Three separate session concepts kept genuinely separate: logging in does **not** open a shift,
  logging out does **not** close one, and a shift cannot be closed while a work session is running
- `Working` and `ShiftEnded` are reachable but not self-settable — `Working` comes from starting a
  task, so availability can never claim work that is not happening
- Endpoints: `GET /api/shifts/current`, `POST /api/shifts/start|end`, `PUT /api/shifts/state`,
  `GET /api/shifts/timeline|activity|history`; supervisory `GET /api/workforce/active`,
  `GET /api/workforce/{id}/status|timeline|activity|shifts`, `POST /api/workforce/{id}/end-shift`
- **Daily timeline** built from the activity stream, with business-timezone day boundaries and
  overnight carry-over so a night shift's hours land on both days rather than vanishing
- **Improper-logout handling**: a background sweep closes shifts left open past
  `Workforce:MaxShiftHours`, ending them at the user's last recorded activity rather than at sweep
  time, flagging `EndedImproperly` and writing both a timeline entry and an audit record

## Phases 3-6 — The pipeline (code complete)

**Phase 3 · Request intake & triage** — request CRUD, attachments on disk (generated names,
traversal guard, extension allow-list, SHA-256, authorized+audited download), the review queue,
all six triage outcomes, and the clarification loop. Five of the six outcomes end a request without
producing any work at all.

**Phase 4 · Task creation & workflow engine** — `TaskCreationService` is the only place a task is
born, and triage approval is its only caller. `TaskWorkflowService` persists transitions, appends to
both history streams, echoes onto the workforce timeline, closes stranded sessions, honours
idempotency keys, and records overrides.

**Phase 5 · Assignment & queue** — assignment guarded by the row version, collaborators plus
reviewer/QC roles, ordered per-assignee queues, append-only assignment history, and a workload view.

**Phase 6 · Work sessions & timer** — start/pause/resume/block/complete, configurable pause reasons,
the single-active-session rule, the emergency interruption flow, and totals summed from closed
sessions. Work requires an open shift; completing lands in QC, never Closed.

## Try it without SQL Server

There is a **Demo** profile that runs against a SQLite file and seeds sample people and a pipeline,
so you can click around before the database exists:

```bash
dotnet run --project src/WorkflowApp.Api --launch-profile Demo
# then open http://localhost:5099
```

That serves a plain-HTML **dev console** (`wwwroot/index.html`) with dashboard, requests, triage,
assignment, my-work and workforce views. It is a throwaway harness for exercising the API, not a
design — the Angular front end has not been started, and this file is meant to be deleted when it
is. Swagger is at `/swagger`.

Demo accounts, all with password `Demo!Pass123`:

| User | Role |
|---|---|
| `rachel` | Requester |
| `victor` | Reviewer |
| `amara` | Assignment coordinator |
| `wu`, `priya` | Workers |
| `quentin` | QC |
| `morgan` | Management |

> Demo mode is for local evaluation only: SQLite, a shared well-known password, and
> `EnsureCreated` instead of migrations. SQL Server remains the deployment target and the only
> store the migrations are authored for.

## Getting it running

```bash
dotnet restore
dotnet build
dotnet test          # 190 tests, none of which need SQL Server
```

The `InitialCreate` migration is already committed. To create the database (needs SQL Server):

```bash
cd src/WorkflowApp.Api
dotnet ef database update --project ../WorkflowApp.Infrastructure --startup-project .
dotnet run           # Swagger UI at /swagger in Development
```

If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`.
Prefer to review the DDL first? `scripts/sql/001-InitialCreate.idempotent.sql` is the same
migration as a re-runnable script you can open in SSMS.

In Development, `Database:ApplyMigrationsOnStartup` is `true`, so running the API migrates and
seeds automatically. It is `false` everywhere else — deployed environments apply migrations from
the pipeline, not from app startup.

**First login:** the seeder creates `admin` / `ChangeMe!2024` only when the database has no users
at all. Change it immediately, and override `Auth:DefaultAdminPassword` before any real deployment.

> The projects target `net8.0`. If your machine only has a newer shared runtime, the test projects
> already set `RollForward=Major`; to run the API itself you will need the .NET 8 runtime installed
> (which is what the IIS hosting bundle provides on the server anyway).

## Next: Phase 7 (QC & Closure) — see docs/02-PHASE-PLAN.md.

## Non-negotiable business rules (see phase plan for full list)
1. A request never auto-becomes a task.
2. One active primary work session per user.
2b. Shifts are tracked only for people who execute tasks (`Workforce.TrackShift`).
3. No status transition outside `TaskWorkflow.Transitions`.
4. Every mutating transition is permission-checked server-side.
5. Reason mandatory for: reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only.
7. DB is source of truth; SignalR only notifies.
