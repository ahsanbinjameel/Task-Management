# CLAUDE.md — Project Context Map

**Read this file instead of scanning the repo.** It is the authoritative map of what exists,
where it lives, and what the conventions are. Only open the specific files a task touches.
When you add/move/rename anything structural, update this file in the same change.

> Status legend: ✅ built & compiling · 🚧 partially built · ⛔ not started · 🔒 blocked on SQL Server

---

## 1. What this is

Internal operations system enforcing **Request → Review → Approval → Assignment → Execution →
QC → Closure**, plus shift/attendance tracking and real-time updates.

Design rationale: `docs/01-ARCHITECTURE.md` · Phase checklist: `docs/02-PHASE-PLAN.md`
(⚠️ identical copies also exist at repo root — keep both in sync, or delete the root pair.)

## 2. Stack & environment

| | |
|---|---|
| Target framework | `net8.0` (all projects) |
| Installed SDK | .NET 10.0.101 — builds net8.0 fine; **do not** bump TFM without asking |
| DB | SQL Server 2019+, EF Core 8 code-first |
| Auth | Custom identity tables + JWT access/refresh (see §6) |
| Real-time | SignalR (Phase 9, not started) |
| Frontend | Angular (not started) |
| Host | IIS in-process, Windows Server |

**Local dev DB:** `(localdb)\MSSQLLocalDB`, database `WorkflowApp_Dev`
(`appsettings.Development.json`). Base/prod: `Server=localhost;Database=WorkflowApp`.

### Commands

    dotnet build                    # from repo root
    dotnet test                     # 54 tests; none require SQL Server

    # migrations — run from src/WorkflowApp.Api
    dotnet ef migrations add <Name> --project ../WorkflowApp.Infrastructure --startup-project . --output-dir Persistence/Migrations
    dotnet ef migrations script --idempotent --project ../WorkflowApp.Infrastructure --startup-project . --output ../../scripts/sql/<n>.sql
    dotnet ef database update --project ../WorkflowApp.Infrastructure --startup-project .   # needs SQL Server
    dotnet run --project src/WorkflowApp.Api                                                # needs SQL Server

**Demo mode** (no SQL Server needed) — see §10:

    dotnet run --project src/WorkflowApp.Api --launch-profile Demo   # http://localhost:5099

**Runtime gotcha on this machine:** only the .NET 10 runtime is installed, but everything targets
`net8.0`. Consequences, already handled — don't "fix" them again:
- Test projects set `<RollForward>Major</RollForward>`, so `dotnet test` just works.
- `dotnet-ef` (8.0.30, global tool) needs `DOTNET_ROLL_FORWARD=Major` exported before any `ef`
  command, otherwise it fails with "You must install or update .NET to run this application".
- The Api project sets `<RollForward>Major</RollForward>` too, so `dotnet run` works here. On the
  server the .NET 8 hosting bundle is an exact match and roll-forward never engages.

## 3. Solution layout & dependency direction

    Api → Application → Domain
    Infrastructure → Application, Domain
    Domain → (nothing)

    src/
      WorkflowApp.Domain           entities, enums, workflow state machine. Zero dependencies.
      WorkflowApp.Application      use-case services, DTOs, interfaces, permission catalog.
                                   References EF Core abstractions only, for IWorkflowDbContext.
      WorkflowApp.Infrastructure   EF Core DbContext, configurations, migrations, JWT, hashing, seeding.
      WorkflowApp.Api              controllers, authorization plumbing, middleware, DI composition root.
    tests/
      WorkflowApp.Domain.Tests         pure workflow/state-machine tests (xunit).
      WorkflowApp.Application.Tests    service tests on EF Core InMemory — no SQL Server required.

## 4. File index (what lives where)

### Domain — ✅

| File | Contains |
|---|---|
| `Entities/Common/NumberSequence.cs` | Named counter behind `REQ-`/`TSK-` numbers; `Version` is a plain int concurrency token |
| `Common/BaseEntity.cs` | `Id`, `CreatedAt`, `UpdatedAt`, `CreatedByUserId`, `UpdatedByUserId`, `RowVersion` |
| `Enums/TaskStatus.cs` | `WorkTaskStatus` — the enforced lifecycle (named WorkTask*, avoids System.Threading.Tasks clash) |
| `Enums/Enums.cs` | `Priority`, `RequestedUrgency`, `RequestStatus`, `RequestType`, `WorkforceState`, `WorkSessionStatus`, `QCResult`, `CommentCategory`, `DependencyType`, `ActivityType` |
| `Workflow/TaskWorkflow.cs` | **The allowed-transition map** — single source of truth. `Find` / `IsAllowed` / `NextStates` |
| `Workflow/WorkforceStateMachine.cs` | **The workforce availability map** — transitions carry their timeline label. `IsOnShift` / `IsAway` / `IsProductive` / `IsSelfServiceTarget` |
| `Workflow/WorkflowExceptions.cs` | `InvalidWorkflowTransitionException`, `TransitionReasonRequiredException` |
| `Entities/Identity/UserRolePermission.cs` | `User`, `Role`, `Permission`, `UserRole`, `RolePermission`, `LoginAttempt`, `RefreshToken` |
| `Entities/Workforce/ShiftAndActivity.cs` | `ShiftSession` (incl. `EndedImproperly`, `EndedByUserId`, `EndNote`), `ActivityEvent` |
| `Entities/Requests/Organization.cs` | `Department`, `Team`, `Client`, `Project`, `Module`, `PauseReason` |
| `Entities/Requests/Request.cs` | `Request`, `RequestClarification`, `Attachment` |
| `Entities/Tasks/WorkTask.cs` | `WorkTask`, `TaskCollaborator` |
| `Entities/Tasks/WorkSessionAndHistory.cs` | `WorkSession`, `QCReview`, `AssignmentHistory`, `StatusHistory`, `TaskActivity` |
| `Entities/Tasks/CommentsDependenciesAudit.cs` | `TaskComment`, `TaskDependency`, `ScopeChange`, `Notification`, `AuditLog` |

### Application

| File | Status | Contains |
|---|---|---|
| `Common/Permissions.cs` | ✅ | `Permissions.*` key catalog + `DefaultRoles.Map` (role → permission bundles) |
| `Common/TaskTransitionService.cs` | ✅ | Pure transition validation (workflow map + permission + reason + override) |
| `Common/Interfaces/IWorkflowDbContext.cs` | ✅ | The persistence surface the Application layer sees (all DbSets + `Database` + `SaveChangesAsync`) |
| `Common/Interfaces/IIdentityAbstractions.cs` | ✅ | `ICurrentUser`, `IDateTimeProvider`, `IPasswordHasher`, `ITokenService`, `AccessToken`, `IssuedRefreshToken` |
| `Common/Models/Result.cs` | ✅ | `Result` / `Result<T>` / `Error` / `ErrorType` — expected failures are returned, not thrown |
| `Common/Models/PagedResult.cs` | ✅ | `PagedResult<T>`, `PageQuery` (clamps page/pageSize) |
| `Common/Options/AuthOptions.cs` | ✅ | `JwtOptions` (section `Jwt`), `AuthOptions` (section `Auth`) |
| `Common/Services/AuditService.cs` | ✅ | `IAuditService.Record(...)` + `AuditActions` constants. Stages rows; caller saves |
| `Common/Services/ActivityLogger.cs` | ✅ | `IActivityLogger.Record(...)` + `ActivityLabels`. Writes the workforce timeline stream |
| `Common/Services/BusinessCalendar.cs` | ✅ | `IBusinessCalendar` — business-timezone day boundaries; falls back to UTC on a bad zone id |
| `Common/Options/WorkforceOptions.cs` | ✅ | Section `Workforce`: `TimeZoneId`, `MaxShiftHours`, `AutoCloseStaleShifts`, `StaleShiftScanMinutes` |
| `Identity/Dtos/AuthDtos.cs` | ✅ | All auth/admin request+response records, incl. `UserDto`, `AuthResponse`, `RoleDto` |
| `Identity/Services/AuthService.cs` | ✅ | Login, refresh rotation, logout, change password, `me`. Also `PasswordPolicy`, `UserMapper` |
| `Identity/Services/UserAdminService.cs` | ✅ | Create/list/get user, activate, assign roles, admin reset, list roles |
| `Identity/Services/PermissionService.cs` | ✅ | Effective permissions = union across the user's roles |
| `Workforce/Dtos/WorkforceDtos.cs` | ✅ | Shift/state requests, `WorkforceStatusDto`, `TimelineEntryDto`, `DailyTimelineDto`, `ActiveWorkforceDto` |
| `Workforce/Services/ShiftService.cs` | ✅ | Start/end shift, change availability, status, supervisor force-end |
| `Workforce/Services/DailyTimelineBuilder.cs` | ✅ | **Pure**: events → intervals + totals. Handles carry-over, open entries, clock skew |
| `Workforce/Services/WorkforceQueryService.cs` | ✅ | Who's-working-now, daily timeline, activity list, shift history |
| `Workforce/Services/ShiftMaintenanceService.cs` | ✅ | Closes abandoned shifts at the last sign of life; flags + audits |
| `Common/Services/NumberGenerator.cs` | ✅ | `INumberGenerator` + `NumberSequences` names. Retry loop on concurrency conflict |
| `Common/Interfaces/IFileStorage.cs` | ✅ | Attachment binary storage contract |
| `Common/Options/FileStorageOptions.cs` | ✅ | Section `FileStorage`: `Root`, `MaxFileSizeBytes`, `AllowedExtensions` |
| `Requests/Dtos/RequestDtos.cs` | ✅ | Create/update/triage DTOs, `TriageOutcome`, summary + detail projections |
| `Requests/Services/RequestService.cs` | ✅ | Intake CRUD, listing, review queue |
| `Requests/Services/RequestTriageService.cs` | ✅ | **The request→work gate.** Six outcomes; only Approve creates a task |
| `Requests/Services/AttachmentService.cs` | ✅ | Metadata + access control; owner must be exactly one of request/task |
| `Tasks/Dtos/TaskDtos.cs` | ✅ | Transition/assign/queue DTOs, task summary + detail, workload, sessions |
| `Tasks/Services/TaskCreationService.cs` | ✅ | **The only place a WorkTask is created.** One caller: triage approval |
| `Tasks/Services/TaskWorkflowService.cs` | ✅ | Persistent workflow engine: status, both history streams, idempotency, overrides |
| `Tasks/Services/TaskQueryService.cs` | ✅ | Task reads, queues, workload, assignable users, pause reasons |
| `Tasks/Services/TaskAssignmentService.cs` | ✅ | Assignment (row-version guarded), collaborators, roles, queue order |
| `Tasks/Services/WorkSessionService.cs` | ✅ | The timer: start/pause/block/complete/interrupt; single-active rule |
| `DependencyInjection.cs` | ✅ | `AddApplication()` |

### Infrastructure

| File | Status | Contains |
|---|---|---|
| `Persistence/WorkflowDbContext.cs` | ✅ | All DbSets; implements `IWorkflowDbContext`; applies configurations from assembly |
| `Persistence/WorkflowDbContextFactory.cs` | ✅ | Design-time factory so `dotnet ef` works without booting the API |
| `Persistence/Configurations/CoreConfigurations.cs` | ✅ | User/Role/Permission, Request, WorkTask, WorkSession, ShiftSession, Attachment, Dependency, AuditLog |
| `Persistence/Configurations/IdentityConfigurations.cs` | ✅ | RefreshToken (unique hash index), LoginAttempt |
| `Persistence/Configurations/SupportingConfigurations.cs` | ✅ | Org lookups, ActivityEvent, decimal precision, task history/comments/QC/notifications |
| `Persistence/Interceptors/AuditableEntityInterceptor.cs` | ✅ | **Sole** writer of CreatedAt/UpdatedAt/CreatedByUserId/UpdatedByUserId — never set these by hand |
| `Persistence/Seed/DatabaseSeeder.cs` | ✅ | Idempotent: permissions, roles+grants, pause reasons, bootstrap admin |
| `Persistence/Migrations/` | ✅ | Single `InitialCreate` + model snapshot. Squashed while still unapplied — **do not squash again once it has run anywhere** |
| `Identity/JwtTokenService.cs` | ✅ | Access-token issuance + `AppClaimTypes`; refresh token generation and SHA-256 hashing |
| `Identity/PasswordHasherAdapter.cs` | ✅ | Wraps `PasswordHasher<User>` (PBKDF2-HMAC-SHA256) |
| `Storage/DiskFileStorage.cs` | ✅ | Generated stored names, path-traversal guard, hash-while-writing |
| `Persistence/Seed/DemoDataSeeder.cs` | ✅ | Local-evaluation data only; refuses to run if any request exists |
| `Common/SystemDateTimeProvider.cs` | ✅ | The real clock |
| `DependencyInjection.cs` | ✅ | `AddInfrastructure(configuration)` |

### Api

| File | Status | Contains |
|---|---|---|
| `Program.cs` | ✅ | JWT bearer, permission policies, rate limiter, Swagger+bearer, CORS, optional migrate+seed, `/health`. SignalR hub mapping still TODO (Phase 9) |
| `Authorization/PermissionAuthorization.cs` | ✅ | `PermissionRequirement`, handler, `PermissionPolicyProvider` (`perm:{key}`), `[HasPermission]` |
| `Common/ApiControllerBase.cs` | ✅ | `[ApiController] [Authorize] api/[controller]`, `CurrentUserId`, `Result` → `ProblemDetails` mapping |
| `Common/RateLimitPolicies.cs` | ✅ | Policy names |
| `Middleware/ExceptionHandlingMiddleware.cs` | ✅ | Unhandled → ProblemDetails; workflow → 400, `DbUpdateConcurrencyException` → 409 |
| `Services/CurrentUserService.cs` | ✅ | `ICurrentUser` from the JWT principal + request metadata |
| `Controllers/AuthController.cs` | ✅ | login, refresh, logout, me, change-password |
| `Controllers/UsersController.cs` | ✅ | Users CRUD-ish + `RolesController` (roles, permission catalog) |
| `Controllers/ShiftsController.cs` | ✅ | Self-service shift/availability — always acts on the token's user |
| `Controllers/WorkforceController.cs` | ✅ | Supervisory views + force-end |
| `Services/StaleShiftSweepService.cs` | ✅ | `BackgroundService` driving `IShiftMaintenanceService`; fails soft |
| `Controllers/RequestsController.cs` | ✅ | Intake, review queue, triage, clarifications + `AttachmentsController` |
| `Controllers/TasksController.cs` | ✅ | Queues, workflow, assignment, timer, attachments |
| `wwwroot/index.html` | ✅ | Throwaway dev console (see §10). **Not** the front end — Angular is still unstarted and this file is meant to be deleted |
| `appsettings.json` | ✅ | `ConnectionStrings`, `Cors`, `Jwt`, `Auth`, `Workforce`, `Database:ApplyMigrationsOnStartup`, `FileStorage` |

### Endpoints (Phase 1)

| Method | Route | Permission |
|---|---|---|
| POST | `/api/auth/login` | anonymous, rate-limited |
| POST | `/api/auth/refresh` | anonymous, rate-limited |
| POST | `/api/auth/logout` | anonymous (idempotent) |
| GET | `/api/auth/me` | authenticated |
| POST | `/api/auth/change-password` | authenticated |
| GET/POST | `/api/users`, `GET /api/users/{id}` | `Admin.ManageUsers` |
| PUT | `/api/users/{id}/active` | `Admin.ManageUsers` |
| PUT | `/api/users/{id}/roles` | `Admin.ManageRoles` |
| POST | `/api/users/{id}/reset-password` | `Admin.ManageUsers` |
| GET | `/api/roles`, `/api/roles/permissions` | `Admin.ManageRoles` |
| GET | `/health` | anonymous, no DB call |

### Endpoints (Phase 2)

| Method | Route | Permission |
|---|---|---|
| GET | `/api/shifts/current` | authenticated (own record) |
| POST | `/api/shifts/start` | `Workforce.TrackShift` (own record) |
| POST | `/api/shifts/end` | authenticated (own record — deliberately ungated) |
| PUT | `/api/shifts/state` | authenticated (own record) |
| GET | `/api/shifts/timeline`, `/activity`, `/history` | authenticated (own record) |
| GET | `/api/workforce/active` | `Workforce.ViewAll` |
| GET | `/api/workforce/{userId}/status\|timeline\|activity\|shifts` | `Workforce.ViewAll` |
| POST | `/api/workforce/{userId}/end-shift` | `Workforce.ManageOthers` |

### Endpoints (Phases 3-6)

| Method | Route | Permission |
|---|---|---|
| POST/GET/PUT | `/api/requests`, `/api/requests/{id}` | `Request.Create` / scoped by `Request.ViewAll` |
| GET | `/api/requests/review-queue` | `Task.Review` |
| POST | `/api/requests/{id}/start-review`, `/triage` | `Task.Review` (+ `Task.Approve` to approve) |
| POST | `/api/requests/clarifications/{id}/answer` | authenticated (requester only) |
| POST | `/api/requests/{id}/attachments`, `/api/tasks/{id}/attachments` | authenticated |
| GET/DELETE | `/api/attachments/{id}` | authenticated (uploader only to delete) |
| GET | `/api/tasks`, `/api/tasks/{id}`, `/my-queue`, `/pause-reasons`, `/active-session` | authenticated |
| GET | `/api/tasks/assignment-queue`, `/assignable-users` | `Task.Assign` |
| GET | `/api/tasks/workload` | `Workforce.ViewAll` |
| POST | `/api/tasks/{id}/transition` | per-transition (see `TaskWorkflow`) |
| PUT/POST/DELETE | `/api/tasks/{id}/assignee\|roles\|collaborators`, `PATCH /api/tasks/{id}` | `Task.Assign` |
| PUT | `/api/tasks/my-queue/order` | authenticated (own tasks only) |
| POST | `/api/tasks/{id}/start\|pause\|block\|complete`, `/api/tasks/interrupt` | `Task.Work` |

### Scripts

| Path | Contains |
|---|---|
| `scripts/sql/001-InitialCreate.idempotent.sql` | The `InitialCreate` migration as a re-runnable script for SSMS |

## 5. Non-negotiable business rules (enforce in every phase)

1. A request never auto-becomes a task — approval creates it explicitly.
2. Only one active primary work session per user.
3. No status transition outside `TaskWorkflow.Transitions`.
4. Every mutating transition is permission-checked **server-side**; UI hiding is not security.
5. Reason mandatory for: reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only — never overwrite comments, sessions, QC attempts, or status history.
7. DB is source of truth; SignalR only notifies.
8. **Three distinct session concepts** — auth session ≠ shift session ≠ task work session.

### DB-level guarantees already declared

- `UX_WorkSession_OneActivePerUser` — filtered unique index, `WHERE [Status] = 0`
- `UX_ShiftSession_OneOpenPerUser` — filtered unique index, `WHERE [ShiftEnd] IS NULL`
- `RowVersion` concurrency token on `User`, `Request`, `WorkTask`, `WorkSession`, `ShiftSession`

## 6. Decisions made (don't re-litigate)

- **Custom identity tables, not ASP.NET Core Identity's `IdentityDbContext`.** The scaffold already
  models `User` / `Role` / `Permission` / `UserRole` / `RolePermission`. Password hashing reuses
  `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (PBKDF2-HMAC-SHA256) behind an adapter, so we get
  Identity's vetted hashing without the AspNetUsers schema. Architecture doc §1 says "ASP.NET Core
  Identity" — this is the narrowed interpretation actually implemented.
- **Permission-based authorization, not role-based.** Roles are only bundles. Policies are named after
  permission keys and resolved dynamically, so no hand-registered policy per permission.
- **Modular monolith, layered** — no CQRS / MediatR / microservices in v1. Deliberate.
- `long` keys everywhere; `DateTimeOffset` for all timestamps; UTC.
- Refresh tokens stored **hashed** (SHA-256), rotated on every refresh, revocation recorded.
- **`Working` is never self-declared.** It is reachable in the state machine but excluded from
  `SelfServiceStates`, so it can only be entered by starting a task (Phase 6). Same for
  `ShiftEnded`, which only the end-shift operation may set.
- **Timestamps are UTC; days are business-local.** `IBusinessCalendar` resolves day boundaries in
  `Workforce:TimeZoneId` so overnight shifts land on the right report.
- **Abandoned shifts end at the last sign of life**, not at sweep time — crediting the user until
  the sweep noticed would inflate attendance by hours.
- **A task is created in exactly one place** — `TaskCreationService`, called only by triage
  approval. That is what makes "a request never auto-becomes a task" auditable rather than hopeful.
- **State machines are the source of truth for shape; services own cross-aggregate rules.** The map
  says Working→ShiftEnded is legal; `ShiftService` is what refuses it while a work session is open.
- **`Result`/`Error` codes are stable strings** (`shift.not_open`, `worksession.already_active`).
  Clients branch on `code`, never on prose. Missing permission → 403, illegal move → 409.
- **Reference numbers come from a sequence table**, not from identity columns — printed numbers must
  be dense, and requests and tasks must not share a counter.
- **Shifts are only for people who execute tasks.** Gated by its own permission,
  `Workforce.TrackShift`, held by Worker (and Administrator) by default — *not* by Reviewer,
  AssignmentManager, Requester, Management or QC. It is a separate permission rather than a side
  effect of `Task.Work` so "who is on the clock" is changeable in the role editor, not in code.
  Only **starting** a shift is gated: ending one and changing availability are not, because a user
  whose permission is revoked mid-shift must still be able to clock out unaided.
  `WorkforceStatusDto.IsShiftTracked` lets a client hide the controls instead of offering a 403.
- **Activity events ordered by `(OccurredAt, Id)` everywhere.** Two events can share a timestamp;
  without the `Id` tie-break, "the latest state" resolves arbitrarily and timelines go wrong.

## 7. Conventions

- Nullable enabled, implicit usings enabled, file-scoped namespaces.
- Entities use `= default!` for required reference properties.
- One `IEntityTypeConfiguration` class per aggregate area, all under `Configurations/`.
- Interfaces live in `Application`; implementations in `Application` (pure use-case) or
  `Infrastructure` (I/O, crypto, EF).
- Controllers surface failures as `ProblemDetails` via the global exception middleware — don't
  hand-roll error shapes.
- Tests: xunit, `Method_scenario_expected` naming, never require SQL Server. New service tests
  go through `TestHarness` in `tests/WorkflowApp.Application.Tests/TestHarness.cs`, which wires
  the real services against InMemory with a controllable `FixedClock`.
- Never assign `CreatedAt`/`UpdatedAt`/`*ByUserId` by hand — `AuditableEntityInterceptor` owns them.
- Expected failures return `Result`/`Result<T>`; only genuinely exceptional conditions throw.
- State machines live in `Domain` and govern *shape only*. Cross-aggregate rules (e.g. "cannot end
  a shift while a work session is open") belong in the Application service.

## 8. Progress

| Phase | Status |
|---|---|
| 0 — Foundation | ✅ |
| 1 — Identity & Authorization | ✅ code complete; 🔒 not yet run against a real database |
| 2 — Shift & Workforce States | ✅ code complete |
| 3 — Request Intake & Triage | ✅ code complete |
| 4 — Task Creation & Workflow Engine | ✅ code complete |
| 5 — Assignment & Queue | ✅ code complete |
| 6 — Work Sessions & Timer | ✅ code complete |
| 7 — QC & Closure | ⛔ **next** |
| 8–12 | ⛔ not started (see `docs/02-PHASE-PLAN.md`) |

Phases 1–6 are code-complete and smoke-tested end to end against SQLite. None of them has run
against SQL Server yet — see §9.

**Tests:** 184 passing (`dotnet test`) — 29 domain state machines, 155 application services.
All on EF Core InMemory or pure functions, so the suite runs with no SQL Server.

## 9. Blocked on SQL Server

Nothing in the codebase requires a live database to *write*, *compile*, or *test*. These do:

- [ ] `dotnet ef database update` — apply `InitialCreate`
      (or run `scripts/sql/001-InitialCreate.idempotent.sql` in SSMS)
- [ ] Install the .NET 8 runtime, then `dotnet run --project src/WorkflowApp.Api`
- [ ] Verify the seeder populated permissions, the 7 system roles with grants, pause reasons,
      and the bootstrap admin
- [ ] Log in as `admin` / `ChangeMe!2024`, confirm the JWT carries `permission` claims, then
      change the password
- [ ] Exercise refresh rotation and confirm replaying an old refresh token is rejected
- [ ] Confirm SQL Server actually created the filtered unique indexes
      (`UX_WorkSession_OneActivePerUser`, `UX_ShiftSession_OneOpenPerUser`, `UX_RefreshToken_TokenHash`)
- [ ] Set a real `Jwt:SigningKey` via `Jwt__SigningKey` env var / user-secrets — startup refuses
      the placeholder outside Development
- [ ] Set a real `Workforce:TimeZoneId` — it defaults to UTC, which will skew daily reports
- [ ] Confirm the ROWVERSION concurrency guard actually fires on concurrent assignment. SQLite has
      no ROWVERSION, so the demo run exercises the code path but not the database guarantee
- [ ] Re-run the phase 3-6 smoke sequence against SQL Server (request → triage → task → assign →
      timer → QC → close)
- [ ] Phase 2: start a shift, step through Break/Lunch/Meeting, end it, and check the timeline
      totals against the wall clock
- [ ] Phase 2: confirm the stale-shift sweep runs on startup and closes an artificially old
      open shift (set `Workforce:MaxShiftHours` low to force it)

## 10. Demo mode (how to see it running)

A local evaluation profile that needs no SQL Server:

    dotnet run --project src/WorkflowApp.Api --launch-profile Demo
    # http://localhost:5099  ·  Swagger at /swagger

Configured by `appsettings.Demo.json`. What it changes, and why none of it is production-shaped:

| | |
|---|---|
| Store | SQLite file `workflowapp-demo.db` (gitignored) |
| Schema | `EnsureCreated()` — the migrations are authored for SQL Server and are **not** used here |
| Data | `DemoDataSeeder` adds people + a pipeline; refuses to run if any request already exists |
| Accounts | `rachel` `victor` `amara` `wu` `priya` `quentin` `morgan`, all `Demo!Pass123`; plus `admin` / `ChangeMe!2024` |
| UI | `wwwroot/index.html` — a single-file dev console. **Not** the Angular front end |

Two provider shims live in `WorkflowDbContext.OnModelCreating` and exist only so the same model
loads on SQLite. Don't extend them to carry real behaviour:

- `RowVersion` is stripped of its concurrency-token role on any non-SQL-Server provider.
- `DateTimeOffset` is stored as UTC ticks, because SQLite refuses to `ORDER BY` one.

**Do not run anything that matters on Demo mode.** No ROWVERSION means the assignment concurrency
guard is exercised in code but not enforced by the database.
