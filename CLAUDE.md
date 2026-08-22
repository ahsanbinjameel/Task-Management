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
Deploy + operate: `docs/03-RUNBOOK.md`
(the duplicate copies that used to sit at the repo root have been deleted — `docs/` is the only home.)

## 2. Stack & environment

| | |
|---|---|
| Target framework | `net8.0` (all projects) |
| Installed SDK | .NET 10.0.101 — builds net8.0 fine; **do not** bump TFM without asking |
| DB | SQL Server 2019+, EF Core 8 code-first |
| Auth | Custom identity tables + JWT access/refresh (see §6) |
| Real-time | SignalR (Phase 9, not started) |
| Frontend | **Angular 21 + Angular Material**, in `client/`. Builds into the API's `wwwroot` |
| Host | IIS in-process, Windows Server |

**Local dev DB:** `Server=localhost`, database `WorkflowApp_Dev` (`appsettings.Development.json`),
on the local SQL Server 2019 Developer Edition default instance. Base/prod:
`Server=localhost;Database=WorkflowApp`. Development sets
`Database:ApplyMigrationsOnStartup: true`, so `dotnet run` creates, migrates and seeds it.

### Commands

    dotnet build                    # from repo root
    dotnet test                     # 258 tests; none require SQL Server

    cd client && npm ci && npm run build    # the Angular client -> src/WorkflowApp.Api/wwwroot
    cd client && npm start                  # dev server on :4200, proxied to the API on :7099

    # migrations — run from src/WorkflowApp.Api
    dotnet ef migrations add <Name> --project ../WorkflowApp.Infrastructure --startup-project . --output-dir Persistence/Migrations
    dotnet ef migrations script --idempotent --project ../WorkflowApp.Infrastructure --startup-project . --output ../../scripts/sql/<n>.sql
    dotnet ef database update --project ../WorkflowApp.Infrastructure --startup-project .   # needs SQL Server
    dotnet run --project src/WorkflowApp.Api                                                # needs SQL Server

**Demo mode** (no SQL Server needed) — see §10:

    dotnet run --project src/WorkflowApp.Api --launch-profile Demo   # http://localhost:5099

**Runtime on this machine:** `dotnet --list-runtimes` shows 6.0.36 and **8.0.30**, an exact match
for the `net8.0` target, so `dotnet build` / `test` / `run` all work with no roll-forward. The
`<RollForward>Major</RollForward>` settings on the test and Api projects are harmless leftovers
from when only a newer runtime was present — they never engage now.

**`dotnet-ef` is not installed.** `dotnet tool list --global` is empty. Either install it
(`dotnet tool install --global dotnet-ef --version 8.*`) before running any `ef` command, or skip
it: in Development the API applies migrations on startup, and `scripts/sql/` holds the idempotent
script for SSMS.

## 3. Solution layout & dependency direction

    Api → Application → Domain
    Infrastructure → Application, Domain
    Domain → (nothing)

    client/                      Angular SPA. Talks to the API over HTTP only; no shared build.
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
| `Tasks/Services/TaskStatusJournal.cs` | ✅ | Shared status-change writer: status + StatusHistory + TaskActivity + workforce echo. Used by the timer, QC and closure |
| `Tasks/Services/QCService.cs` | ✅ | **The only way into QCPassed / QCFailedRework.** Numbered attempts, criteria gate, segregation of duties |
| `Tasks/Services/AcceptanceCriteria.cs` | ✅ | **Pure**: parses the criteria text into items, serialises the evaluation |
| `Tasks/Services/ClosureService.cs` | ✅ | **The only way into Closed.** The closure checklist, and the close operation itself |
| `Tasks/Dtos/QCDtos.cs` | ✅ | QC submit/verdict DTOs, criteria projections, closure checklist |
| `Tasks/Services/TaskCommentService.cs` | ✅ | Append-only comments; category-driven visibility, filtered server-side on read |
| `Tasks/Services/TaskDependencyService.cs` | ✅ | The dependency graph, cycle detection, and `BlockersAsync` — the source of the blocked signal |
| `Tasks/Services/ScopeChangeService.cs` | ✅ | Scope changes: recorded on request, applied on approval |
| `Tasks/Dtos/CollaborationDtos.cs` | ✅ | Comment, dependency, subtask, scope-change and reopen DTOs |
| `Common/Events/IntegrationEvents.cs` | ✅ | Event records, `RealtimeGroups` names, the scoped queue, the no-op publisher |
| `Notifications/NotificationService.cs` | ✅ | The bell icon. `Raise` stages; caller commits |
| `Notifications/AuditQueryService.cs` | ✅ | Read-only access to the append-only audit trail |
| `Reporting/DashboardService.cs` | ✅ | Four dashboards: requester, worker, coordinator, management |
| `Reporting/ReportService.cs` | ✅ | Daily user/team attendance + effort, and the CSV |
| `Reporting/DashboardDtos.cs` | ✅ | Dashboard and report projections |
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
| `Persistence/Interceptors/IntegrationEventDispatchInterceptor.cs` | ✅ | Derives real-time events from the change tracker; dispatches **after** commit, drops them on rollback |
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
| `Controllers/TasksController.cs` | ✅ | Queues, workflow, assignment, timer, QC, closure, comments, dependencies, subtasks, scope, attachments |
| `Hubs/WorkflowHub.cs` | ✅ | The SignalR hub. Notification-only; groups come from the token |
| `Services/SignalRIntegrationEventPublisher.cs` | ✅ | **The one place** that decides who hears about what |
| `Controllers/DashboardsController.cs` | ✅ | The four dashboards + `ReportsController` (daily reports, CSV) |
| `Controllers/NotificationsController.cs` | ✅ | The bell icon + `AuditController` (audit stream) |
| `Middleware/SecurityHeadersMiddleware.cs` | ✅ | nosniff, frame-deny, Referrer/Permissions-Policy, CSP |
| `wwwroot/` | ✅ | **Build output** of the Angular client. Gitignored — a fresh clone must run `npm run build` |
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

### Endpoints (Phase 7)

| Method | Route | Permission |
|---|---|---|
| GET | `/api/tasks/qc-queue` | `Task.QCReview` |
| POST | `/api/tasks/{id}/qc/start`, `/qc/review` | `Task.QCReview` |
| GET | `/api/tasks/{id}/qc`, `/acceptance-criteria` | authenticated |
| GET | `/api/tasks/{id}/closure-check` | `Task.Close` |
| POST | `/api/tasks/{id}/close` | `Task.Close` |

### Endpoints (Phase 8)

| Method | Route | Permission |
|---|---|---|
| GET/POST | `/api/tasks/{id}/comments` | authenticated (list is filtered per caller) |
| GET | `/api/tasks/{id}/dependencies` | authenticated |
| POST/DELETE | `/api/tasks/{id}/dependencies`, `/dependencies/{depId}` | `Task.Assign` |
| GET | `/api/tasks/{id}/subtasks` | authenticated |
| POST | `/api/tasks/{id}/subtasks` | `Task.Assign` |
| GET/POST | `/api/tasks/{id}/scope-changes` | authenticated |
| POST | `/api/tasks/scope-changes/{id}/approve` | `Task.Approve` |
| POST | `/api/tasks/{id}/reopen` | `Task.Reopen` |

### Endpoints (Phases 9-12)

| Method | Route | Permission |
|---|---|---|
| WS | `/hubs/workflow` | authenticated (token via the `access_token` query parameter) |
| GET | `/api/dashboards/requester`, `/worker` | authenticated (own data) |
| GET | `/api/dashboards/coordinator` | `Task.Assign` |
| GET | `/api/dashboards/management` | `Dashboard.Management` |
| GET | `/api/reports/me/daily` | authenticated (own hours) |
| GET | `/api/reports/users/{id}/daily`, `/team/daily`, `/team/daily.csv` | `Reports.View` |
| GET/POST | `/api/notifications`, `/unread-count`, `/read`, `/read-all` | authenticated (own inbox) |
| GET | `/api/audit`, `/api/audit/actions` | `Admin.ViewAudit` |
| GET | `/health/ready` | anonymous; checks the database |

### Client (`client/`) — ✅

| Path | Contains |
|---|---|
| `src/app/core/models.ts` | TypeScript mirrors of the API DTOs. Enums are **string unions** — the API serialises names, not ordinals |
| `src/app/core/api.service.ts` | Every HTTP call, grouped by resource. The one place a URL appears |
| `src/app/core/auth.service.ts` | Session signals, token storage, refresh |
| `src/app/core/http.interceptors.ts` | Bearer token + **serialised** refresh-on-401, and ProblemDetails → toast |
| `src/app/core/realtime.service.ts` | SignalR. Exposes event streams; screens re-fetch, never patch |
| `src/app/core/guards.ts` | `authGuard`, `requirePermission(...)` |
| `src/app/core/format.ts` | TimeSpan parsing, status→tone mapping, CSV/blob download |
| `src/app/shared/` | Chips, stats, empty/loading states, the shared task table, confirm + reason dialogs |
| `src/app/layout/` | Shell, permission-filtered nav, notification bell, shift widget |
| `src/app/features/` | One folder per area: dashboard, tasks (+ `panels/`), requests, qc, workforce, reports, admin, me |
| `proxy.conf.json` | Dev-server proxy for `/api`, `/hubs`, `/health` |

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
9. QCPassed / QCFailedRework / Closed are reachable only through their dedicated service, so each
   always has its record behind it. Overrides are the one exception, and they are audited.
10. A task cannot close while a subtask is open, and cannot start while a dependency is unfinished.

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
- **QC and closure own their transitions; the generic endpoint cannot reach them.**
  `POST /api/tasks/{id}/transition` refuses `QCPassed`, `QCFailedRework` and `Closed` with
  `workflow.dedicated_endpoint_required`. Those three states each carry a record that must be
  written in the same commit — a numbered QC attempt, or a satisfied closure checklist — and
  allowing the generic path would make that record optional. An explicit override still gets
  through, because it already demands `Task.Override`, a reason, and an audit row.
- **Acceptance criteria are text on the task, not a child table.** One criterion per line, parsed
  by `AcceptanceCriteria`. A coordinator can rewrite them without a migration; the cost is that
  indexes are only stable while the text is. Verdicts are matched back by index *and* text, so
  editing the criteria after QC passed shows those items as unevaluated rather than inheriting a
  stale pass — and reopens the closure gate.
- **A QC query is not a lifecycle state.** `QCResult.ClarificationRequired` records an attempt and
  leaves the task in `QCReview`. Inventing a transition for it would put the map and the code out
  of step; the map stays the shape of the lifecycle.
- **Closure requirements are a named list, not scattered guards.** `IClosureService.EvaluateAsync`
  returns each requirement with met/unmet and a reason, so a client can grey out the close button
  and say why instead of offering it and taking a 409.
- **Comment visibility is decided by the category, not the caller.** `AddCommentDto.VisibleToRequester`
  is nullable; left null the category decides, and anything not explicitly customer-facing is
  internal. The opposite default would mean one forgotten checkbox leaks an internal note. Filtering
  is applied on read, server-side — the requester calls the same endpoint everyone else does.
- **Only `DependsOn` and `Blocks` impose an order.** They are the only types cycle-checked and the
  only ones that produce a blocked signal; `Related` and `Duplicate` are cross-references.
  `ParentChild` is rejected outright — parentage lives on `WorkTask.ParentTaskId`, and two places to
  record the same fact is one too many.
- **Blocked is enforced, not decorative.** `WorkSessionService.StartAsync` refuses a task waiting on
  unfinished work (`task.blocked_by_dependency`). A graph nothing acts on would not be maintained.
- **Subtasks are real tasks, one level deep.** They get their own number, assignee, timer and
  history because the work has to be schedulable and reportable. Nesting is refused: a tree makes
  "is the parent done?" unanswerable at a glance and forces the closure check to recurse.
- **`TaskCreationService` still owns every birth, and now has exactly two callers** — triage
  approval and subtask creation. The second cannot smuggle an unapproved request into execution,
  because it starts from work that was already approved.
- **A scope change is recorded on request and applied on approval.** Overwriting the estimate in
  place would make a bad estimate and a job that doubled in size look identical in every report.
- **A reopened task needs a fresh QC pass.** The closure gate compares the latest QC attempt against
  the most recent reopen, so a stale pass cannot carry work through a second closure. No schema
  needed — both facts are already in the history.
- **Real-time events are derived from the change tracker, not raised by hand.**
  `IntegrationEventDispatchInterceptor` reads modified entities in `SavingChanges` and publishes in
  `SavedChanges`. No service can forget to notify, and nothing is announced for a save that rolled
  back. Publish failures are logged and swallowed — SignalR only notifies, and a dropped
  notification must never fail the transaction that caused it.
- **Real-time payloads carry an id and a status, never a copy of the record.** A fat payload is a
  second, staler copy that goes wrong the moment a client applies it out of order. Clients re-fetch.
- **Hub group membership comes from the token.** Permission groups are joined in `OnConnectedAsync`
  from the caller's claims, so a client cannot subscribe itself into a feed it has no rights to. The
  hub has no state-changing method at all; commands go through REST, where the checks live.
- **Reports are EF queries, not stored procedures**, against the phase plan's original wording. One
  definition of the schema, covered by the same test suite with no SQL Server needed, and no second
  artefact to keep in step through a migration. Revisit only if a report actually outgrows it.
- **A notification is a pointer, not a copy** — title plus link, resolved on click. Same reason as
  the thin real-time payload. The actor is never notified of their own action.
- **The audit trail is read-only through the API.** `IAuditQueryService` has no write, edit or delete
  method. An administrator who could quietly remove audit rows would make the whole trail worthless.
- **CSRF protection is deliberately absent.** Authentication is a bearer token in a header, never a
  cookie, so a cross-site request cannot carry the caller's credentials. This reasoning stops
  holding the day anything moves to cookie auth.
- **Liveness and readiness are separate probes.** `/health` never touches the database so it still
  answers during an outage; `/health/ready` does, so the load balancer drops the instance instead of
  a process monitor restarting it pointlessly.
- **The client is served from the API's `wwwroot`, not hosted separately.** One artifact, one origin,
  no CORS in production, and the SignalR hub is same-origin. `Program.cs` adds a SPA fallback that
  deliberately excludes `/api`, `/hubs`, `/swagger` and `/health` — an unknown API path must 404, not
  return HTML that a `fetch()` would try to parse as JSON.
- **The UI filters by permission; the API enforces.** The nav, the action bar and every button read
  the permission claims from the JWT. That is usability, not security — hiding a button has never
  stopped anyone calling the endpoint, and every call is checked again server-side.
- **Screens re-fetch on real-time events; they never patch from the payload.** The server sends
  pointers by design, so applying an event as if it were a record would go wrong the moment two
  arrived out of order or one was missed during a reconnect.
- **Fonts and icons are self-hosted** (`@fontsource/roboto`, `material-icons`). The CSP is
  `'self'`-only and an internal LAN box may have no internet; a CDN font that silently fails leaves
  an icon set rendered as raw words.
- **Angular Material 21 needs no `@angular/animations`.** It uses native CSS animations, and
  `provideAnimationsAsync()` would fail to resolve its lazy import.
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
| 7 — QC & Closure | ✅ code complete; verified against SQL Server |
| 8 — Comments / Dependencies / Subtasks / Scope / Reopen | ✅ code complete; verified against SQL Server |
| 9 — Real-Time (SignalR) | ✅ code complete; verified against SQL Server |
| 10 — Dashboards & Reports | ✅ code complete; verified against SQL Server |
| 11 — Notifications & Audit | ✅ code complete; verified against SQL Server |
| 12 — Hardening | ✅ including responsive UI |
| Angular front end | ✅ built and smoke-tested headlessly against the API |

**The server side is complete.** Phases 0–12 are built and have been smoke-tested end to end
against **SQL Server**: request → triage → task → assign → shift → timer (pause/resume) → complete
→ QC fail → rework → QC pass → closure checklist → closed → reopen → fresh QC, plus comments,
dependencies, subtasks, scope changes, dashboards, reports, notifications, the audit stream, the
SignalR hub and the security headers.

The **Angular client** is built and covers the whole pipeline. It has been driven headlessly
(Playwright, Chromium) against the live API: sign-in, all 13 top-level screens, the task detail with
all seven tabs, and the narrow-viewport layout — with no console errors.

What remains is the production configuration in §9.

**Tests:** 258 passing (`dotnet test`) — 29 domain state machines, 229 application services.
All on EF Core InMemory or pure functions, so the suite runs with no SQL Server.

## 9. SQL Server: done and still outstanding

SQL Server 2019 Developer Edition runs on the `localhost` default instance. `WorkflowApp_Dev` has
been created, migrated and seeded, and every phase has been driven end to end over HTTP.

Verified:

- [x] `InitialCreate` applied and recorded in `__EFMigrationsHistory`. Phases 7-12 added no schema
- [x] Seeder populated 22 permissions, the 7 system roles with grants, pause reasons and the
      bootstrap admin. It backfills new grants on restart
- [x] The filtered unique indexes exist with the right predicates:
      `UX_WorkSession_OneActivePerUser WHERE [Status]=0`,
      `UX_ShiftSession_OneOpenPerUser WHERE [ShiftEnd] IS NULL`, `UX_RefreshToken_TokenHash`
- [x] `RowVersion` is a real `timestamp` column on User, Request, WorkTask, WorkSession, ShiftSession
- [x] Phases 3-8 pipeline: request -> triage -> task -> assign -> shift -> timer -> complete ->
      QC fail -> rework -> QC pass -> closure checklist -> closed -> reopen -> stale pass rejected,
      with a contiguous status trail; plus comment visibility, dependency cycles, blocked-start,
      subtasks and scope-change approval
- [x] Phases 9-12: hub rejects anonymous negotiate and accepts an authenticated one (WebSockets
      offered); notifications raised on assignment, QC pass and closure; all four dashboards, the
      daily reports and the CSV; the audit stream and its permission gate; security headers and the
      readiness probe

Still outstanding before this is anything but a dev box:

- [ ] Change the bootstrap admin password away from `ChangeMe!2024`
- [ ] Set a real `Jwt:SigningKey` via `Jwt__SigningKey` env var / user-secrets - startup refuses
      the placeholder outside Development
- [ ] Set a real `Workforce:TimeZoneId` - it defaults to UTC, which will skew daily reports
- [ ] Point `FileStorage:Root` at a real directory for non-Development environments
- [ ] Exercise refresh rotation and confirm replaying an old refresh token is rejected
- [ ] Confirm the ROWVERSION concurrency guard actually fires on *concurrent* assignment - the
      column is real now, but nothing has raced against it
- [ ] Phase 2: step a shift through Break/Lunch/Meeting and check the timeline totals against the
      wall clock
- [ ] Phase 2: confirm the stale-shift sweep closes an artificially old open shift
      (set `Workforce:MaxShiftHours` low to force it)
- [ ] Phase 9: connect a real SignalR client and watch a task update arrive. Only the negotiate
      handshake has been exercised so far, not an actual pushed message
- [ ] Phase 9: SignalR group membership is per-process. Running more than one instance needs a
      Redis backplane, or sticky sessions
- [ ] `WorkflowApp_Dev` holds smoke-test rows (users prefixed `smoke_`/`p8_`/`p9_`, several
      requests and tasks). Drop the database to start clean - it is recreated on the next
      `dotnet run`

Everything above is covered operationally by `docs/03-RUNBOOK.md`.

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
