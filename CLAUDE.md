# CLAUDE.md — Project Context Map

**Read this file instead of scanning the repo.** It is the authoritative map of what exists,
where it lives, and what the conventions are. Only open the specific files a task touches.
When you add/move/rename anything structural, update this file in the same change.

> Status legend: ✅ built & compiling · 🚧 partially built · ⛔ not started · 🔒 blocked on SQL Server

---

## 1. What this is

Internal operations system enforcing **Request → Review → Approval → Assignment → Execution →
QC → Closure**, plus shift/attendance tracking and real-time updates.

A review can also branch sideways into **Verification** — assigned investigation into whether there
is really a problem — which produces findings and never work. See §6.

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
    dotnet test                     # 397 tests; none require SQL Server

    cd client && npm ci && npm run build    # the Angular client -> src/WorkflowApp.Api/wwwroot
    cd client && npm start                  # dev server on :4200, proxied to the API on :7099

    # migrations — run from src/WorkflowApp.Api
    dotnet ef migrations add <Name> --project ../WorkflowApp.Infrastructure --startup-project . --output-dir Persistence/Migrations
    dotnet ef migrations script --idempotent --project ../WorkflowApp.Infrastructure --startup-project . --output ../../scripts/sql/<n>.sql
    dotnet ef database update --project ../WorkflowApp.Infrastructure --startup-project .   # needs SQL Server

    dotnet run --project src/WorkflowApp.Api --launch-profile Development   # against SQL Server

`Development` is the only launch profile, so a bare `dotnet run --project src/WorkflowApp.Api` now
starts it too. **Everything needs SQL Server** — there is no lighter store to run against, by
decision (§6).

**Runtime on this machine:** `dotnet --list-runtimes` shows 6.0.36 and **8.0.30**, an exact match
for the `net8.0` target, so `dotnet build` / `test` / `run` all work with no roll-forward. The
`<RollForward>Major</RollForward>` settings on the test and Api projects are harmless leftovers
from when only a newer runtime was present — they never engage now.

**`dotnet-ef` is per-machine.** On the secondary machine (DESKTOP-2E2D7JE) it is installed
globally at 8.0.30. If `dotnet tool list --global` is empty on a machine, install it
(`dotnet tool install --global dotnet-ef --version 8.*`) or skip it: in Development the API applies
migrations on startup, and `scripts/sql/` holds the idempotent script for SSMS.

**`WorkflowDbContextFactory` does not read user-secrets** — it builds its own configuration from
`appsettings*.json` + environment variables only. So on the secondary machine every `dotnet ef`
command that *touches the database* (`database update`, `migrations list`) resolves `Server=localhost`
and fails with a Named Pipes error. Prefix it with the real connection string, or apply migrations by
running the API instead (`--launch-profile Development`), which does load user-secrets:

    export ConnectionStrings__Default='Server=(localdb)\MSSQLLocalDB;Database=WorkflowApp_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true'
    dotnet ef database update --project ../WorkflowApp.Infrastructure --startup-project .

`migrations add` and `migrations script` are unaffected — they read the model, never the database.

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
| `Enums/Enums.cs` | `Priority`, `RequestedUrgency`, `RequestStatus` (incl. `UnderVerification`), `RequestType`, `WorkforceState`, `WorkSessionStatus`, `QCResult`, `CommentCategory`, `DependencyType`, `PauseCategory`, `ActivityType`, `VerificationStatus`, `VerificationResult`, `VerificationTargetType` |
| `Workflow/TaskWorkflow.cs` | **The allowed-transition map** — single source of truth. `Find` / `IsAllowed` / `NextStates` |
| `Workflow/WorkforceStateMachine.cs` | **The workforce availability map** — transitions carry their timeline label. `IsOnShift` / `IsAway` / `IsProductive` / `IsSelfServiceTarget` |
| `Workflow/WorkflowExceptions.cs` | `InvalidWorkflowTransitionException`, `TransitionReasonRequiredException` |
| `Entities/Identity/UserRolePermission.cs` | `User`, `Role`, `Permission`, `UserRole`, `RolePermission`, `LoginAttempt`, `RefreshToken` |
| `Entities/Workforce/ShiftAndActivity.cs` | `ShiftSession` (incl. `EndedImproperly`, `EndedByUserId`, `EndNote`), `ActivityEvent` |
| `Entities/Requests/Organization.cs` | `Department`, `Team`, `Client`, `Project`, `Module`, `PauseReason` |
| `Entities/Requests/Request.cs` | `Request` (incl. `BatchId`/`OrdinalInBatch`), `RequestClarification`, `AttachmentKind`, `Attachment` (owner is exactly one of request/task/batch; `Kind` says what it is *for*, `QCReviewId` ties evidence to its attempt) |
| `Entities/Requests/RequestBatch.cs` | `RequestBatch` — several things asked for at once. Holds the shared client/note/files; carries **no status of its own** |
| `Entities/Tasks/WorkTask.cs` | `WorkTask`, `TaskCollaborator` |
| `Entities/Tasks/QuickWork.cs` | `QuickWork`, `QuickWorkStatus` — work that arrived without a request. Not a `WorkTask`, deliberately |
| `Entities/Verifications/Verification.cs` | `Verification`, `VerificationActivity` — **assigned investigation**: "is there really a problem here?". Needs no task, and creating one is not something it can do |
| `Entities/Tasks/WorkSessionAndHistory.cs` | `WorkSession`, `QCReview`, `AssignmentHistory`, `StatusHistory`, `TaskActivity` |
| `Entities/Tasks/CommentsDependenciesAudit.cs` | `TaskComment`, `TaskDependency`, `ScopeChange`, `Notification`, `AuditLog` |

### Application

| File | Status | Contains |
|---|---|---|
| `Common/Permissions.cs` | ✅ | `Permissions.*` key catalog + `DefaultRoles.Map` (role → permission bundles) |
| `Common/TaskTransitionService.cs` | ✅ | Pure transition validation (workflow map + permission + reason + override) |
| `Common/StatusLabels.cs` | ✅ | **The words users see for internal status names.** Mirrored on the client by `core/labels.ts` — change both together |
| `Common/ColumnFilters.cs` | ✅ | The grid filter row server-side: `col[key] → value`, read as text/id/bool/enum/date. Unknown keys ignored, blank values narrow nothing |
| `Common/StatusViews.cs` | ✅ | **Who is shown which statuses.** Groups internal states into per-audience views (requester / worker / coordinator), resolves the audience from permissions, and folds a request's status onto its task |
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
| `Admin/Services/SetupService.cs` | ✅ | **The administrator's reference data**: clients, departments, teams, pause reasons, and roles themselves. Retire rather than delete; names unique case-insensitively; refuses to orphan the last route to `Admin.ManageRoles` |
| `Admin/Dtos/SetupDtos.cs` | ✅ | The editable shapes for all of the above |
| `Workforce/Dtos/WorkforceDtos.cs` | ✅ | Shift/state requests, `WorkforceStatusDto`, `TimelineEntryDto`, `DailyTimelineDto`, `ActiveWorkforceDto` |
| `Workforce/Services/ShiftService.cs` | ✅ | Start/end shift, change availability, status, supervisor force-end |
| `Workforce/Services/DailyTimelineBuilder.cs` | ✅ | **Pure**: events → intervals + totals. Handles carry-over, open entries, clock skew |
| `Workforce/Services/WorkforceQueryService.cs` | ✅ | Who's-working-now, daily timeline, activity list, shift history |
| `Workforce/Services/ShiftMaintenanceService.cs` | ✅ | Closes abandoned shifts at the last sign of life; flags + audits |
| `Common/Services/NumberGenerator.cs` | ✅ | `INumberGenerator` + `NumberSequences` names (`REQ`/`TSK`/`BAT`/`VER`). Retry loop on concurrency conflict |
| `Common/Services/LookupService.cs` | ✅ | Client type-ahead + `ResolveClientAsync`, and the module picker the verification target needs |
| `Common/Interfaces/IFileStorage.cs` | ✅ | Attachment binary storage contract |
| `Common/Options/FileStorageOptions.cs` | ✅ | Section `FileStorage`: `Root`, `MaxFileSizeBytes`, `AllowedExtensions` |
| `Requests/Dtos/RequestDtos.cs` | ✅ | Create/update/triage DTOs, `TriageOutcome`, summary + detail projections |
| `Requests/Services/RequestService.cs` | ✅ | Intake CRUD, listing, review queue |
| `Requests/Services/RequestTriageService.cs` | ✅ | **The request→work gate.** Six outcomes; only Approve creates a task |
| `Requests/Services/RequestBatchService.cs` | ✅ | Batch intake, and the fold: several approved items into one task. Still calls `TaskCreationService` |
| `Requests/Dtos/RequestBatchDtos.cs` | ✅ | Batch create/detail/summary DTOs, `ApproveTogetherDto` |
| `Requests/Services/AttachmentService.cs` | ✅ | Metadata + access control; owner must be exactly one of request/task/batch/verification |
| `Verifications/Services/VerificationService.cs` | ✅ | **Assigned investigation.** Raise, assign, start, report, cancel. Its defining rule: a result *never* creates work — every outcome hands the request back to a reviewer |
| `Verifications/Dtos/VerificationDtos.cs` | ✅ | Create/assign/result/cancel DTOs, `SendForVerificationDto` (carried inside triage), summary + detail + `RequestVerificationDto` |
| `Tasks/Dtos/TaskDtos.cs` | ✅ | Transition/assign/queue DTOs, task summary + detail, workload, sessions |
| `Tasks/Services/TaskCreationService.cs` | ✅ | **The only place a WorkTask is created.** One caller: triage approval |
| `Tasks/Services/TaskWorkflowService.cs` | ✅ | Persistent workflow engine: status, both history streams, idempotency, overrides |
| `Tasks/Services/TaskQueryService.cs` | ✅ | Task reads, queues, workload, assignable users, pause reasons |
| `Tasks/Services/TaskAssignmentService.cs` | ✅ | Assignment (row-version guarded), collaborators, roles, queue order |
| `Tasks/Services/WorkSessionService.cs` | ✅ | The timer: start/pause/block/complete/interrupt; single-active rule |
| `Tasks/Services/QuickWorkService.cs` | ✅ | **The clock for work that never came through the front door.** Pauses the running task in the same commit; promotion raises a *request*, never a task |
| `Tasks/Dtos/QuickWorkDtos.cs` | ✅ | Start/finish/promote DTOs and the projection |
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
| `Reporting/DashboardService.cs` | ✅ | The home screen (needs-attention / recent-activity) plus four audience dashboards |
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
| `Persistence/Configurations/VerificationConfigurations.cs` | ✅ | `Verification` (unique number, checker-queue index, Restrict on every FK) and `VerificationActivity` |
| `Persistence/Interceptors/AuditableEntityInterceptor.cs` | ✅ | **Sole** writer of CreatedAt/UpdatedAt/CreatedByUserId/UpdatedByUserId — never set these by hand |
| `Persistence/Interceptors/IntegrationEventDispatchInterceptor.cs` | ✅ | Derives real-time events from the change tracker; dispatches **after** commit, drops them on rollback |
| `Persistence/Seed/DatabaseSeeder.cs` | ✅ | Idempotent: permissions, roles+grants, pause reasons, bootstrap admin |
| `Persistence/Migrations/` | ✅ | 9 migrations: `InitialCreate` (squashed while still unapplied — **do not squash again**), `OptionalUserEmail`, `RequiredSubtasks`, `PauseCategoryAndAwayState`, `RequestActivityHistory`, `QuickWork`, `RequestBatches`, `AttachmentProof`, `Verifications`, + model snapshot. **All tracked in git — the schema travels with the code; never move a database backup between machines** |
| `Identity/JwtTokenService.cs` | ✅ | Access-token issuance + `AppClaimTypes`; refresh token generation and SHA-256 hashing |
| `Identity/PasswordHasherAdapter.cs` | ✅ | Wraps `PasswordHasher<User>` (PBKDF2-HMAC-SHA256) |
| `Storage/DiskFileStorage.cs` | ✅ | Generated stored names, path-traversal guard, hash-while-writing |
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
| `Controllers/SetupController.cs` | ✅ | `api/setup/*` — the reference data. Gated on `Admin.ManageConfig`; the role endpoints additionally on `Admin.ManageRoles` |
| `Controllers/ShiftsController.cs` | ✅ | Self-service shift/availability — always acts on the token's user |
| `Controllers/WorkforceController.cs` | ✅ | Supervisory views + force-end |
| `Services/StaleShiftSweepService.cs` | ✅ | `BackgroundService` driving `IShiftMaintenanceService`; fails soft |
| `Controllers/RequestsController.cs` | ✅ | Intake, review queue, triage, clarifications + `AttachmentsController` |
| `Controllers/RequestBatchesController.cs` | ✅ | Only the two genuinely new operations: create items together, fold approved items into one task |
| `Controllers/TasksController.cs` | ✅ | Queues, workflow, assignment, timer, QC, closure, comments, dependencies, subtasks, scope, attachments |
| `Hubs/WorkflowHub.cs` | ✅ | The SignalR hub. Notification-only; groups come from the token |
| `Services/SignalRIntegrationEventPublisher.cs` | ✅ | **The one place** that decides who hears about what |
| `Controllers/DashboardsController.cs` | ✅ | The home screen, the four dashboards + `ReportsController` (daily reports, CSV, PDF) |
| `Controllers/QuickWorkController.cs` | ✅ | Quick work — always the caller's own record; gated on `Workforce.TrackShift` |
| `Controllers/VerificationsController.cs` | ✅ | Checks: raise, assign, start, report, cancel, evidence. Split `Verification.Create` / `Verification.Work`; two rules live in the service because they depend on the record |
| `Controllers/LookupsController.cs` | ✅ | `api/lookups/clients` and `/modules` — the type-aheads. Signed in is enough |
| `Services/DailyReportPdf.cs` | ✅ | The daily report as a document (MigraDoc). Header, summary, detail, quick work, notes, page numbers |
| `Services/FileSystemFontResolver.cs` | ✅ | PDFsharp 6 ships no font handling; this finds one on the machine and fails at **startup** if it cannot |
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
| PUT | `/api/users/{id}` | `Admin.ManageUsers` — username, name, email, and optionally a new password |
| PUT | `/api/auth/me` | authenticated — your own name and email only |
| GET | `/api/tasks/filter-options`, `/api/requests/filter-options` | as the list they belong to |
| PUT | `/api/users/{id}/active` | `Admin.ManageUsers` |
| PUT | `/api/users/{id}/roles` | `Admin.ManageRoles` |
| POST | `/api/users/{id}/reset-password` | `Admin.ManageUsers` |
| GET | `/api/roles`, `/api/roles/permissions` | `Admin.ManageRoles` |
| GET | `/health` | anonymous, no DB call |
| GET/POST/PUT | `/api/setup/clients`, `/departments`, `/teams`, `/pause-reasons` (+ `/{id}`, `/{id}/active`) | `Admin.ManageConfig` |
| GET/POST/PUT/DELETE | `/api/setup/roles`, `/{id}`, `/{id}/permissions` | `Admin.ManageConfig` **and** `Admin.ManageRoles` |

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
| POST | `/api/requests/batches` | `Request.Create` |
| GET | `/api/requests/batches/{id}`, `/mine` | authenticated (own, or anyone who reviews/coordinates/reports) |
| GET | `/api/requests/batches/review-queue` | `Task.Review` |
| POST | `/api/requests/batches/{id}/approve-together` | `Task.Approve` |
| POST | `/api/requests/batches/{id}/attachments` | authenticated |
| GET | `/api/requests/review-queue` | `Task.Review` |
| POST | `/api/requests/{id}/start-review`, `/triage` | `Task.Review` (+ `Task.Approve` to approve) |
| POST | `/api/requests/clarifications/{id}/answer` | authenticated (requester only) |
| POST | `/api/requests/{id}/attachments` | authenticated |
| POST | `/api/tasks/{id}/attachments?kind=` | authenticated; `CompletionProof` assignee only, `QCEvidence` needs `Task.QCReview` |
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

### Endpoints (Verification)

| Method | Route | Permission |
|---|---|---|
| GET | `/api/verifications` | authenticated (scoped: yours unless `Verification.ViewAll`) |
| GET | `/api/verifications/my-queue` | `Verification.Work` |
| GET | `/api/verifications/assignable-checkers` | `Verification.Create` |
| GET | `/api/verifications/{id}` | authenticated; **404** rather than 403 when out of scope |
| POST | `/api/verifications` | `Verification.Create` |
| PUT | `/api/verifications/{id}/assignee` | `Verification.Create` (reason required to re-route) |
| POST | `/api/verifications/{id}/claim` | `Verification.Work` — takes an *unclaimed* check for yourself |
| POST | `/api/verifications/{id}/start`, `/result` | `Verification.Work` **and** be the assigned checker |
| POST | `/api/verifications/{id}/cancel` | `Verification.Create` (reason required) |
| POST | `/api/verifications/{id}/attachments` | the assigned checker only |
| GET | `/api/lookups/modules` | authenticated |

`POST /api/requests/{id}/triage` gains the `SendForVerification` outcome, which carries a
`verification` object and returns `verificationId`/`verificationNumber` instead of a task.

### Endpoints (Phases 9-12)

| Method | Route | Permission |
|---|---|---|
| WS | `/hubs/workflow` | authenticated (token via the `access_token` query parameter) |
| GET | `/api/dashboards/home` | authenticated (scoped by the caller's own permissions) |
| GET | `/api/dashboards/requester`, `/worker` | authenticated (own data) |
| GET | `/api/dashboards/coordinator` | `Task.Assign` |
| GET | `/api/dashboards/management` | `Dashboard.Management` |
| GET | `/api/reports/me/daily` | authenticated (own hours) |
| GET | `/api/reports/users/{id}/daily`, `/team/daily`, `/team/daily.csv` | `Reports.View` |
| GET | `/api/reports/me/daily.pdf` | authenticated (own hours) |
| GET | `/api/reports/team/daily.pdf`, `/users/{id}/daily.pdf` | `Reports.View` |
| GET/POST | `/api/quick-work`, `/active` | authenticated (own record) |
| POST | `/api/quick-work` | `Workforce.TrackShift` |
| POST | `/api/quick-work/{id}/finish\|cancel` | authenticated (own record) |
| POST | `/api/quick-work/{id}/promote` | `Request.Create` (raises a request, never a task) |
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
| `src/app/core/labels.ts` | **The words users see, in one place.** Mirrors `StatusLabels` server-side; also roles, actions, categories, dependency and pause types |
| `src/app/core/format.ts` | TimeSpan parsing, `sinceLabel` ("3 days"), status→tone mapping, CSV/blob download |
| `src/app/shared/` | Chips, stats, empty/loading states, the shared task table, confirm + reason dialogs |
| `src/app/shared/search-select.component.ts` | `app-search-select` — **the** dropdown. Type-to-filter, single or multi (chips). Works with `ngModel` and `formControlName`; `enumOptions()` builds the options for enum lists |
| `src/app/shared/list-views.ts` | Columns + primary action per status view. The server says which statuses a view covers; this says what is worth showing once you are in it |
| `src/app/shared/attachments.component.ts` | `app-attachments` (thumbnails, file rows) and the image viewer dialog — zoom, pan, next/previous. Download is secondary |
| `src/app/shared/pdf-viewer.component.ts` | `PdfViewerDialog` + `openPdf(dialog, …)` — the one way a PDF is opened. Fetches with the bearer token, frames the blob, keeps Download as a button |
| `src/app/shared/column-filter.component.ts` | `app-column-filter` (one filter cell — text, date, or a **multi-value** dropdown), `ColumnFilterSpec`, `ColumnFilterState`/`columnFilters()`. Debounces typing, not choices; `asObject()` produces the `col[key]` query bag |
| | Also `app-no-matches` (the strip shown *under* a still-visible table when filters match nothing) and `app-filter-summary` (the "N filters applied · Clear all" bar above the grid) |
| `src/app/shared/file-drop.component.ts` | `app-file-drop` — choose / drag / **paste** (Win+Shift+S → Ctrl+V), with previews before anything is submitted |
| `src/app/shared/attachment-upload.component.ts` | `app-attachment-upload` — the same three ways in, but straight onto a record that already exists. Carries the `kind`, and takes **exactly one** of `taskId`/`verificationId`, mirroring the server's owner rule |
| `src/app/layout/` | Shell, permission-filtered nav, notification bell, shift widget, quick-work widget (live clock) |
| `src/app/layout/nav-preference.ts` | The sidebar-rail preference. Shared, because both the rail's toggle and the Settings page write it |
| `src/app/features/` | One folder per area: dashboard, tasks (+ `panels/`), requests (incl. `batch-detail`), qc, verifications, workforce, reports, admin, me |
| `src/app/features/verifications/` | The checks list — a standard grid: tiles, a generated filter row, sortable headings, and the table kept on screen when nothing matches. Filtering and sorting run **client-side**, which is correct here because the whole set is loaded in one call. Plus the detail where a checker takes it and reports, and the dialogs that raise and assign |
| `src/app/features/me/settings.component.ts` | **The one door out of the profile menu.** Account facts, change password, per-browser preferences, and (for people who run the system) links to the configuration screens |
| `src/app/features/admin/setup.component.ts` | The setup screen — tabs for clients, pause reasons, departments, teams. Every row shows what points at it and offers retire, not delete |
| `src/app/features/admin/roles.component.ts` | The role map, and its editor for anyone holding `Admin.ManageRoles`. Permission grid grouped from the key prefixes, so a new server-side permission appears with no second edit |
| `src/app/shared/quick-view.component.ts` | `app-quick-view` — the read-only drawer. Same endpoint as the full page, no tabs, no actions, desktop only |
| `proxy.conf.json` | Dev-server proxy for `/api`, `/hubs`, `/health` |

### Scripts

| Path | Contains |
|---|---|
| `scripts/sql/001-InitialCreate.idempotent.sql` | The `InitialCreate` migration as a re-runnable script for SSMS |
| `scripts/verify-verification-e2e.sh` | The 36-check HTTP drive of the verification feature against a running API (`bash scripts/verify-verification-e2e.sh`, API on `https://localhost:7099`). Creates four `e2e_*` accounts and leaves its records behind — a dev-box tool, not a test |
| `scripts/sql/reset-dev-data.sql` | Empties a dev database back to `admin` + the seeded catalogue. Keeps Permissions/Roles/RolePermissions/PauseReasons; drops every request, task, quick-work record, batch, session, shift, attachment row, audit entry, org lookup and other account. One transaction, `XACT_ABORT`, child-first. **Add new tables here when you add them** |

## 5. Non-negotiable business rules (enforce in every phase)

1. A request never auto-becomes a task — approval creates it explicitly. A *batch* cannot become
   a task at all; its items can, one at a time or several folded into one.
2. Only one active primary work session per user — and Quick Work respects it rather than
   bypassing it: starting one pauses the running task in the same commit.
3. No status transition outside `TaskWorkflow.Transitions`.
4. Every mutating transition is permission-checked **server-side**; UI hiding is not security.
5. Reason mandatory for: reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only — never overwrite comments, sessions, QC attempts, or status history.
7. DB is source of truth; SignalR only notifies.
8. **Three distinct session concepts** — auth session ≠ shift session ≠ task work session.
9. QCPassed / QCFailedRework / Closed are reachable only through their dedicated service, so each
   always has its record behind it. Overrides are the one exception, and they are audited.
10. A task cannot close while a subtask is open, and cannot start while a dependency is unfinished.
11. **A verification never creates work.** Every result — a confirmed problem included — returns the
    request to `InReview` with the findings attached, and a reviewer approves it explicitly or not
    at all. Nothing may be decided on a request while a check on it is still open.
12. **`Task.Work`, `Verification.Work` and `Workforce.TrackShift` are independent.** None implies
    another, and every combination is a legitimate configuration. Administering the system implies
    none of them.

### DB-level guarantees already declared

- `UX_WorkSession_OneActivePerUser` — filtered unique index, `WHERE [Status] = 0`
- `UX_ShiftSession_OneOpenPerUser` — filtered unique index, `WHERE [ShiftEnd] IS NULL`
- `UX_QuickWork_OneActivePerUser` — filtered unique index, `WHERE [Status] = 0`
- `RowVersion` concurrency token on `User`, `Request`, `WorkTask`, `WorkSession`, `ShiftSession`,
  `QuickWork`, `RequestBatch`, `Verification`

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
- **Form density is a global setting, not per-form CSS.** `subscriptSizing: 'dynamic'` in
  `MAT_FORM_FIELD_DEFAULT_OPTIONS` is the single biggest change: by default every Material field
  reserves a line beneath itself for an error that is usually absent — ~22px each, so a six-field
  dialog carried 130px of nothing. Theme `density: -2` does the rest. The one trap: a container that
  already spaces its children (`.stack`, `.row`, `.grid`) must null the field's own margin, or the
  two add up and the gap doubles.
- **A sortable table heading is a `<button>`, and buttons carry `text-transform: none` from the UA
  stylesheet.** That is not inherited away, so sortable columns rendered "Worked" while plain ones
  rendered "WORKED" in the same header row. `.mat-mdc-header-cell button` puts the inherited value
  back — worth knowing before styling any header.
- **Fonts and icons are self-hosted** (`@fontsource/roboto`, `material-icons`). The CSP is
  `'self'`-only and an internal LAN box may have no internet; a CDN font that silently fails leaves
  an icon set rendered as raw words.
- **`StatusViews.RequestStatusFollowsTask` is the only place that decides whether a request's
  status follows its task.** The rule was re-derived independently in four places — the view table,
  the label, the list filter and the tile counts — and it produced two separate empty-screen bugs
  before being consolidated. (1) `ApplyFilters` folded for *every* audience, so a coordinator's
  approved request was judged against `ReviewerViews`' deliberately-empty task list and vanished:
  the tile counted three and the list showed none. (2) The test was `== Requester`, but
  `AudienceFor` classifies from **task** permissions and the Worker role holds `Request.Create` by
  design — so a worker's own request was read with the reviewer's table and froze on "Approved"
  forever. The rule is now "does this person triage work?" (`!= Coordinator`), asked once. Covered
  by `Approved_view_lists_the_same_requests_the_tile_counts_for_a_reviewer` and
  `A_requester_who_also_works_still_sees_their_request_follow_the_task`.
- **Reference data is retired, never deleted.** A client with requests against it, a pause reason
  in someone's timeline, a role somebody holds — deleting any of them rewrites history that other
  screens still read, turning a report into blanks. So `SetupService` offers deactivate, and every
  row shows what already points at it. The single exception is a role that is neither seeded nor
  held, because a role carries no history of its own.
- **A built-in role cannot be renamed or deleted; its permissions are still editable.**
  `DefaultRoles.Map` is keyed by name and the seeder recreates anything it cannot find, so a rename
  produces a second copy on the next restart and a delete silently comes back. What a role *grants*
  is genuinely configurable.
- **The permission editor refuses to lock itself.** Removing `Admin.ManageRoles` from the only role
  that has it, while someone holds that role, would close the screen for everyone with no way back
  short of SQL. `WouldOrphanAsync` refuses; a role nobody holds is free to edit, because it cannot
  orphan anything.
- **`Admin.ManageConfig` finally means something.** It was in the catalogue from the start, seeded
  with a description, granted to Administrator — and enforced nowhere until `SetupController`.
  Reading lookup data stays open to any signed-in caller through the existing endpoints; only
  changing it is gated.
- **How much of the workflow you see depends on what you do.** `StatusViews` groups the twenty-two
  task states into six for a worker, ten for a coordinator, and ten plain-language ones for a
  requester. The state machine is untouched — this is only about what is *shown*. It lives on the
  server because the filter has to run in the database (counting tiles on the client would only
  ever count the page you can already see) and because two copies of the mapping would drift. The
  audience comes from permissions, never from a role name, so renaming a role changes nothing.
- **A requester's status follows the task, not the request.** A request stops moving once it is
  approved; everything after that happens on the task. So the request's row, tiles and detail all
  report the task's state. Paused reads as "In Progress" and a failed quality check reads as
  "In Progress" — the work is in hand, and a status that flickers with a worker's day only invites
  chasing. Coordinators still see paused, blocked and rework separately, because acting on that
  difference is their job.
- **A filtered-empty grid keeps its filter row.** Every grid rendered its empty state *instead of*
  the table, so a filter matching nothing took the filter row away with it — the control that caused
  the problem vanished and the only way out was a reload. The message lied too: the people grid
  announced "No accounts yet" while eight accounts existed. Now the full empty state is shown only
  when there is nothing *and* no filter is set; otherwise the table stays and `app-no-matches` sits
  under it with a Clear filters button. **Any new grid must keep this shape.**
- **Filter cells have a real minimum width.** With `min-width: 0` the browser met the table's
  `width: 100%` by crushing columns instead of scrolling — "TSK-000003" wrapped onto two lines and
  the priority filter rendered as "An". Columns that do not fit make `.table-scroll` scroll.
- **Filtering lives in the column, not in a card above the grid.** Every grid had a card holding a
  search box and two or three dropdowns, each of which described one column below it — so the
  reader had to map "Client: Any" onto the Client column themselves, and the card grew a control
  every time a column was added. The row is now generated from the grid's own `columns()`, so a
  column gets a filter if a spec names it and an empty cell if not; it cannot fall out of step with
  the header. `mat-form-field` and `app-search-select` are deliberately **not** used here — a
  filter row has to stay the height of a table header, and Material's field brings ~56px of label
  and outline. This is the one place in the app where a bare `<input>`/`<select>` is right.
- **Column filters are applied to the list and never to the tile counts.** A tile says how many
  there are in that status; narrowing it by the column being typed into would send every tile
  towards zero as you type, and the number you were navigating by would move under you. Enforced
  structurally — `ApplyColumnFilters` is called in `ListAsync` only, not inside the shared
  `ApplyFilters` that `StatusCountsAsync` also uses. A comment was not enough: the first attempt
  put it in the shared method and the tiles moved.
- **A column filter holds several values, and they travel separated by a `|`.**
  "Critical and High" is the question people actually ask of a priority column, so a select is
  multi-value; within a column the values are OR'd, across columns they are AND'd. **The separator
  cannot be a comma.** ASP.NET's query value provider treats a comma-separated value as several
  values for one key and the dictionary binder then keeps exactly one — so `Critical,High` arrived
  as `High` and the grid filtered by the wrong thing *without erroring*. A repeated key
  (`col[x]=a&col[x]=b`) fails the same way, keeping the first. A pipe passes through untouched.
  Free text is never split, so a term may contain one.
- **The dropdown stays open while you pick, and the grid does not unmount while it reloads.**
  Picking two values is one action; a panel that closed after the first would make the second a
  fresh trip. That only works because a filter reload no longer swaps the table for a spinner —
  doing so destroyed the filter row, which closed the overlay after the first tick and made
  multi-select impossible in practice. The first load shows the spinner; later loads dim the rows
  (`loaded` / `refreshing`), and deliberately do **not** block pointer events, because the control
  that triggered the reload is the one the user is still typing into.
- **The trigger stays one line however many values are chosen** — `Critical +2`, not a growing list
  of chips. A header cell that grows with the selection pushes the whole grid down.
- **A column's dropdown offers only what the other columns still allow — computed with its own
  filter removed.** That last part is what makes it work: having ticked Critical, the priority list
  must still offer High, or picking a first value would erase the choices needed for a second.
  `ColumnFilters.Without(key)` and `FilterOptionsAsync` on the task and request services; the server
  returns raw tokens and the client hides options its own labelled list no longer needs. A value
  already ticked stays visible even once it becomes unreachable, or narrowing another column would
  strand a filter with no way to untick it.
- **Grid date filters use the business calendar, never UTC midnight.** Filtering
  `[00:00Z, 24:00Z)` matched a task due 30 Aug 00:00+05:00 — 29 Aug 19:00Z — when the user asked for
  the 29th, while the column printed "Aug 30" beside it: the filter and the column disagreed about
  what day it was. `IBusinessCalendar.DayRange` is what the reports already use, and the rule
  "timestamps are UTC; days are business-local" applies here too.
- **Filter columns are a dictionary, not a property per column.** `col[title]=invoice` on the wire,
  `ColumnFilters` server-side, and the owning service decides what each key means. Adding a column
  to a grid must not mean editing a query record, a controller signature and a client interface.
  Unknown keys are **ignored, not rejected** — a stale bookmark or a removed column should show a
  sensible list, and a key nobody handles simply filters nothing.
- **Person columns filter by name, not by a dropdown of people.** The assignable-people endpoint is
  behind `Task.Assign`, which a reviewer need not hold, so a select would 403 for half its users. A
  contains-match on the name already printed in the column works for everyone. `-` in the task
  grid's assignee column means unassigned — the one answer with no name to type, and the one a
  coordinator looks for most.
- **Where the whole list is already loaded, filtering client-side is correct.** The daily report and
  the workload screen return every row unpaged, so narrowing them locally cannot misreport a total.
  The paged grids (requests, tasks, people) filter server-side, because filtering the loaded page
  would show "2 matches" out of thirty — the same class of lie as the tile/list mismatch.
- **"Only mine" became the Requester column.** One control that answers "whose?" beats a switch
  that answers it only for you, and it was the last thing keeping the filter card alive.
- **The grid follows the view, not the screen.** `list-views.ts` names the columns and the one
  primary action per view. A fixed column set is wrong nearly everywhere: worked time on a queue
  nobody has started is a column of dashes. Everything the new columns need was already in the
  history tables — "waiting since" is the `StatusHistory` row that put the task where it is,
  "started" is the first `WorkSession`, "checked by" is the latest `QCReview` — so no schema
  changed.
- **Neither side has to read the other's screen.** `RequestProgressDto` reads the task back onto
  the request (who has it, how far, what QC is doing, why it is waiting, the latest shared note);
  `RequestContextDto` carries the request's own words and screenshots onto the task. Both are
  summaries, not copies: `Request ≠ Task` still holds, and a second staler copy of either would be
  worse than the trip it saves.
- **PDFs are read, not collected.** Every PDF went straight to the downloads folder, which is the
  wrong default for the question people ask of them — "what does today's report say?" — because
  answering it cost a file on disk, an external viewer, and a folder filling with
  `team-daily-*.pdf` nobody deletes. `openPdf` opens the document; Download is a button in its
  toolbar. The CSV export still downloads, deliberately: a spreadsheet is taken away to be worked
  on. Same mechanism as the image viewer — the bytes need the caller's bearer token, which an
  `<iframe src>` cannot carry, so our script fetches them and frames a blob URL. That is why
  **`frame-src blob:` had to be added to the CSP**: without it the directive falls back to
  `default-src 'self'` and the frame renders empty. Unrelated to `frame-ancestors`/`X-Frame-Options`,
  which govern who may frame *us* and stay closed. PDF attachments get the viewer too; spreadsheets
  and archives do not, because they need their own application anyway.
- **Screenshots are looked at, not downloaded.** Thumbnails inline, a viewer with zoom/pan, paste
  and drag-drop on the way in. Attachments are fetched as blobs because an `<img src>` cannot carry
  a bearer token, which is why `img-src` in the CSP allows `blob:` — those URLs are same-origin,
  unguessable, and live only as long as the page.
- **What a file is *for* is a kind, not a second owner.** `AttachmentKind` separates the
  requester's screenshot of a broken invoice (`General`) from the worker's screenshot of the fixed
  one (`CompletionProof`) and from what a checker looked at (`QCEvidence`). In one undifferentiated
  list the only question anybody asks — "show me the evidence this was actually done" — cannot be
  asked at all. Modelling it as another owner column instead would have been wrong twice: the file
  really does belong to the task, and a task can hold all three kinds at once.
- **Who may claim what is decided in the service, not on the controller.** Proof that work is done
  is the responsible person's to give — the check is `PrimaryAssigneeUserId == uploader`, so a
  coordinator holding every permission there is still cannot supply it. Evidence needs
  `Task.QCReview`. A permission attribute alone could not express the first, because the answer
  depends on the task rather than on the caller.
- **QC evidence is staged before the verdict and adopted by it.** The attempt does not exist until
  the verdict is recorded, so there is nothing to point at while the checker is still typing.
  `ClaimQCEvidenceAsync` ties whatever *that* checker left unclaimed on the task to the attempt just
  written — scoped to the uploader, because two checkers can be looking at one task and one of them
  must not have their pictures swept onto the other's verdict. A verdict the server refuses leaves
  the files staged for the retry rather than stranding them.
- **Evidence belongs to a numbered attempt, not to the task.** Attempts are append-only: the
  pictures that justified a failure have to stay with the failure once a later attempt passes. So
  `QCEvidence` is returned inside its `QCReviewDto` and deliberately left out of the task's own
  file lists, where it would lose the one thing that makes it mean anything.
- **The New Request form asks for four things.** Optional detail (business impact, expected result,
  what happens instead, steps to reproduce) is a row of chips, suggested by request type. Closing a
  chip clears the field: a value the requester can no longer see must never be submitted on their
  behalf. Project and Module are deliberately *not* on the form — client alone is enough for
  intake.
- **Every dropdown is `app-search-select`; `mat-select` is not used anywhere.** A plain select is
  fine for four options and unusable for two hundred people, and two controls doing the same job
  means the user has to work out which one they are looking at before they can use it. The text box
  is a *filter*, never a value: it empties on focus (the current value stays on as the placeholder),
  typing narrows the list, and anything unmatched is discarded on blur — so the value can still only
  ever be one of the options. Two Material details are load-bearing: `displayWith` has to map the
  option object back to its label, or the raw object is written into the box behind Angular's back;
  and Material re-focuses the input after a selection, which is why the clear-on-focus is skipped
  once after picking.
- **Angular Material 21 needs no `@angular/animations`.** It uses native CSS animations, and
  `provideAnimationsAsync()` would fail to resolve its lazy import.
- **A batch is a wrapper, not a second workflow.** `RequestBatch` holds only what several requests
  share; every item is a full `Request` with its own number, status and triage decision, so the
  review queue, clarifications, editing and notifications all work on a batch item without knowing
  batches exist. It deliberately carries **no status of its own**: a reviewer can approve three,
  reject one and question the rest, and a status on the wrapper would be either a lie or a summary
  — and a summary is something a screen can compute.
- **Several requests may share one task, and that needed no join table.**
  `Request.GeneratedTaskId` already meant "which task did this become", and nothing stopped several
  requests meaning it about the same task; `WorkTask.RequestId` still points at the item the task
  was raised from. Because the fold rides on the column every read path already uses, a folded-in
  request reports the shared task's progress to its requester with no extra code. The client is
  *copied* onto each item rather than read through the batch, so correcting one item at triage
  cannot drag its siblings with it.
- **Confirmation is for what cannot be taken back, or what starts a clock others read.**
  A dialog on every button is clicked through without being read, which costs the protection on the
  one that needed it — so it is not universal. Two things earn one. *No undo:* triage **Approve**
  (creates the task), **Reject** and **Duplicate**; a QC **pass** or **fail** (a numbered attempt is
  append-only); **approving a scope change**; removing a dependency; closing; deactivating an
  account. *A commitment that starts recording or that other people see:* **start/end shift** and
  **changing availability** (attendance and the timeline are written from the moment you click, and
  the clock does not rewind), **starting work** (opens a session and pauses whatever else was
  running — and is reachable in one click from a queue row via `?start=1`), **submitting a
  request**, and **signing out** (which notably does *not* end your shift, so the dialog says so).
  The re-decidable ones submit straight away: triage clarification/defer/escalate, a QC "need
  information" (which by design leaves the task in QC), and reactivating an account.
- **A confirmation performs the call, it does not just return an answer.** `ConfirmData.submit` and
  `ReasonData.submit` exist so a refusal leaves the dialog open with the server's message beside
  what the user typed. Wiring the dialog to merely return `true` and calling the API afterwards is
  what throws away a reviewer's reason, or a checker's per-criterion answers, on a 409 — so any new
  confirmation on a form must pass `submit`, which means the `ApiService` method needs its
  `context?: HttpContext` parameter.
- **A purpose-built dialog is its own confirmation; it does not open a second one.** `FoldDialog`
  and `AssignDialog` already name what is being acted on, state the consequence and label their own
  button, so they carry the missing sentence inline rather than stacking a modal on a modal. Reach
  for `ConfirmDialog` when the action would otherwise fire from a single click, and note the one
  exception to the submit-inside rule: the New Request form confirms with a plain `true` because it
  is a whole page that survives a refusal untouched, and its submit path goes on to upload the
  attachments afterwards — work that cannot run inside a dialog that has already closed.
- **A dialog wider than 560px must be given a `width` at `open()`.** Material caps the dialog
  *surface* there; a `mat-dialog-content` min-width above it does not widen the dialog, it overflows
  it and clips the right-hand fields behind a sideways scrollbar. That is what was wrong with the
  request edit dialog — the only one in the app over the cap. Sizing from the content alone works
  only below 560px.
- **One dialog holds the whole account; roles stay separate.** Username, name, email and *setting*
  a password are one edit (`Admin.UserUpdated`, plus `Auth.PasswordResetByAdmin` when a password is
  actually set). Granting authority is a different decision, usually by a different person, so
  `Admin.UserRolesChanged` keeps its own dialog and its own audit row.
  **The username is editable** — an earlier version refused on the grounds that history was recorded
  against it, which was wrong: `AuditLog.ActorUserId` and every other back-reference is the numeric
  id, so a rename carries the trail with it. `LoginAttempt.UserNameTried` deliberately keeps the old
  value, being a record of what was actually typed.
  A blank password means "leave it alone"; setting one clears the lockout and ends every live
  session, exactly as the standalone reset did.
- **A password is never displayed, and cannot be.** It is stored as a one-way PBKDF2-HMAC-SHA256
  hash, so no screen, endpoint or query can read one back — only replace it. The edit dialog says so
  where an administrator would otherwise go looking. Making them readable would mean storing them
  reversibly, which turns one database breach into every account.
- **People maintain their own name and email; everything else about them is administered.**
  `PUT /api/auth/me` (`Auth.ProfileUpdated`) reaches display name and email only — not username,
  roles or active state, because a self-service rename would let someone quietly become a different
  person on every screen. Note `AuthService.applyProfile` rather than `setUser` on the client: list
  projections return an **empty** permission array, and putting one through `setUser` would clear
  the session's permissions and blank the nav.
- **Passwords an administrator types are masked, with a deliberate reveal — and the masking is
  what stops the browser keeping them.** The create-user field was `<input name="password">` with
  no `type` and no `autocomplete`, so the browser treated it as ordinary text: every temporary
  password an admin typed went into **form history** and was then offered back as an autofill
  suggestion the next time that dialog opened — for a different user. That, not any API leak, is
  how an administrator could see passwords they had set for other people. `type="password"` fixes
  it at the root (browsers never keep form history for password inputs, and never suggest into
  them) and `autocomplete="new-password"` stops a saved credential being filled in. The reset
  dialog had the same problem twice over, collecting it in a `<textarea>`. Both are masked now with
  one click to reveal (it has to be read out to be handed over), and the reset asks twice, because
  a masked typo locks someone out of an account they have never signed into.
  **Values already in a browser's form history predate the fix and the app cannot clear them** —
  that needs clearing autofill data in the browser. No password or hash has ever left the API:
  `UserDto` does not carry one, and every password field in `AuthDtos` is inbound only.
- **Editing what someone can do is a dialog, not a panel under the table.** The inline roles editor
  opened below a 200-row table — off-screen on any real user list, with nothing tying the
  checkboxes to the row that was clicked.
- **The profile menu offers one door.** It grew an item per preference and had no general answer to
  "where do I change X?". Everything about the account or this browser now sits behind **Settings**;
  "My day" was removed outright because it is a work screen that already appears in the nav, and an
  item in two places is one the reader has to think about twice. `/me/password` redirects rather
  than 404s — it was a menu item long enough to have been bookmarked.
- **The quick-view drawer is read-only and deliberately incomplete.** It answers "is this the one I
  am looking for?" and nothing else — no tabs, no actions, no second copy of the detail page's
  logic. It calls the same endpoint the full page calls. Desktop only: below 1100px there is no
  room for a panel beside a list, and the full page is the better answer anyway.
- **Quick Work is not a task, and not exempt from the rules.** A task carries a lifecycle, an
  assignee, a quality check and a closure checklist, and every one would have to be given a
  meaningless answer for a phone call. But starting one pauses the running task through the same
  close-then-open sequence a task interrupt uses, in one commit, so "one thing at a time" still
  holds. `InterruptedByTaskId` on the work session stays **null** — it means "displaced by *that
  task*", and a quick-work id in it would make every reader of the column wrong;
  `QuickWork.InterruptedTaskId` is the sibling column instead.
- **Promoting quick work raises a request, never a task.** `TaskCreationService` keeps its monopoly,
  so approval is still what creates work. Promotion saves the retyping, not the review. It is gated
  on `Request.Create`, and the **Worker role therefore holds `Request.Create` + `Request.ViewOwn`**:
  a worker who fields a call and finds real work behind it has to be able to put it into the system.
- **Quick work needs an outcome to finish; a mis-click is cancelled, not deleted.** A record of
  forty busy minutes with nothing to show inflates the day and answers nothing. Cancelled rows are
  kept, shown struck through, and excluded from every total.
- **The home screen answers two questions separately.** "What must I do" and "what has happened" are
  different questions, and a list that mixes them makes the reader sort it every visit. Both come
  from the server, scoped by the caller's permissions, and every attention row carries the *reason*
  it is there — so the wording cannot drift between the two halves of the app.
- **The task detail is scoped, not just decorated.** The list was scoped to work someone is part of
  while `GET /api/tasks/{id}` answered for any id — a lock on the door of an unwalled room. It now
  applies the same rule and returns **404, not 403**: "you may not see this" still confirms it
  exists. And a requester is *sent* less, not merely shown less — no sessions, no history, no
  estimate. Hiding a panel client-side without emptying the payload is a leak; emptying it without
  hiding the panel is a lie.
- **Three histories, three audiences, never merged.** `TaskActivity` is the account a person reads;
  `StatusHistory` is the state machine's own record, in its own vocabulary; `AuditLog` is the
  administrator's before-and-after. The technical toggle is offered only to people who run the
  process. All three name people rather than user ids.
- **PDF rendering lives in the API layer.** PDF is a transport format like CSV or JSON: the
  Application layer knows what the numbers mean and should not know how they are drawn. MigraDoc
  (MIT) rather than a hand-built PDF, because page numbers and tables that break across pages are
  the sort of thing that looks easy until the third page.
- **Verification is a first-class aggregate, not a shape of QC review.** `QCReview` answers "does
  this finished work meet its acceptance criteria?"; it belongs to a task's lifecycle, owns the
  transitions into `QCPassed`/`QCFailedRework`, and carries numbered attempts and segregation of
  duties. A verification answers "is there really a problem here?", and in the case it exists for
  there *is no task* — a reviewer handed "the salary form is calculating tax wrongly" cannot tell
  whether it is a defect, a configuration mistake, bad data or a misunderstanding. Making `QCReview`
  polymorphic over both would have given every one of those invariants a null case to mean nothing.
- **A verification never creates work, and that is the whole point.** `IssueConfirmed` returns the
  request to `InReview` with the findings attached and stops. `TaskCreationService` keeps its
  monopoly, so "a request never auto-becomes a task" stays an auditable fact rather than an
  intention — an automatic task on a confirmed issue would have made the check the approval.
- **Every result hands the request back the same way.** Five outcomes with five consequences would
  be five rules to remember and five places for a request to get stuck. "It has been looked at, here
  is what they found, you decide" is one rule, and the reviewer already has all seven triage
  outcomes in front of them.
- **No decision may be taken on a request while a check is open.** The guard sits in `DecideAsync`
  and covers *every* decisive outcome, not only approval — a checker who submits findings against a
  request that was rejected underneath them has done the work for nothing, and the verification is
  left pointing at a decision it played no part in. Asking for a clarification is exempt: that is a
  question, not a decision.
- **A verification's target has real foreign keys where a real row exists, and words where none
  does.** `RequestId` and `ModuleId` are constrained FKs; a form, a screen or a build is described
  in `TargetName`/`TargetReference`, because none of them is an aggregate this database holds. A
  single untyped `SourceId` interpreted through `TargetType` would have been unjoinable,
  unconstrained, and silently orphaned on the first delete.
- **Claiming and assigning are different acts, so they are different endpoints.**
  `PUT /{id}/assignee` is a coordinator handing work out and needs `Verification.Create`;
  `POST /{id}/claim` is a checker picking up something nobody holds and needs `Verification.Work`.
  Without the second, the "needs a checker" notification — which is addressed to exactly the people
  holding `Verification.Work` — led to a page they could do nothing on, and a check raised without
  an assignee was a dead end. Claim is refused once anybody holds it: moving work off a person is a
  decision about two people's workloads, and that goes through assignment, which asks why.
- **Three verification permissions, not four.** `Verification.Create` covers raising, assigning,
  re-routing and cancelling — a check with no checker is inert, so naming one is part of raising it,
  and the reviewer who routes a request is the person who says who should look at it. A separate
  `Verification.Assign` would have meant holding two permissions to perform the single action the
  feature exists for, with no difference in authority behind the split.
- **Who may start, report and attach evidence is decided on the record, not by an attribute.** The
  check is `AssignedToUserId == caller`, so a reviewer holding every permission there is still
  cannot report findings under the checker's name — the same shape of rule, and the same reasoning,
  as `CompletionProof` on a task.
- **`Administrator` is not a worker.** `DefaultRoles.AdministratorGrants` is everything *except*
  `Workforce.TrackShift` and `Task.Work`. Granting them by default put a shift widget in front of
  every administrator, listed the account in who-is-working-now, and offered it for real work —
  none of which follows from administering the system. An administrator who genuinely also does the
  work gets a role that grants it, in the role editor. Note the seeder is **additive**, so an
  existing database keeps the grants it already has: this changes what a *new* one gets, and an
  existing site removes them in the editor. Pinned by `RoleAndShiftSeparationTests`.
- **A requester is told "Being Checked" and nothing else.** `RequestStatus.UnderVerification` folds
  into the requester's existing `checking` view — the same words a task in QC gets — because to the
  person who asked, "somebody is establishing whether this is broken" and "somebody is checking the
  fix" are the same news: it is in hand, and there is nothing for them to do. Reviewers get their
  own `verifying` tile, because "waiting on a checker" and "waiting on the person who asked" are
  different queues with different people to chase.
- **`AttachmentKind.VerificationEvidence` rather than a fourth owner alone.** The file genuinely
  belongs to the verification *and* is evidence for a finding; the owner column says what it hangs
  off, the kind says what it is for. The owner count in `AttachmentService` was already written as a
  sum rather than a chain of comparisons, which is what made adding the fourth owner one line.
- **`.card` is a surface; `.card-pad` is what puts content inside it.** `.card` sets only border,
  radius and shadow — a card without `card-pad` has its content flush to its own edges, which is
  how the first verification detail rendered. The exception is a card whose only child is a table:
  a grid should meet the card's edges, as the request and task lists do.
- **`class="grid"` is a CSS-Grid utility, not a table class.** `styles.scss` defines `.grid` as
  `display: grid`, so putting it on a `<table>` collapses the whole thing into one run-on line —
  which is exactly how the first checks list rendered. Every real grid in this app is an Angular
  Material `mat-table`; the header/row/cell styling in `styles.scss` is written against
  `.mat-mdc-header-cell` and `.mat-mdc-row` and applies to nothing else.
- **There is no demo mode, and nothing added from here on goes into one.** A `Demo` launch profile
  existed until 2026-08-27: SQLite, `EnsureCreated()` instead of migrations, a seeded cast of seven
  sample accounts on one well-known password, and its own branches through `Program.cs`,
  `DependencyInjection`, `WorkflowDbContext` and the security headers. It is **removed** —
  `appsettings.Demo.json`, `DemoDataSeeder`, the `DatabaseProvider` enum, the SQLite package
  reference and every `IsEnvironment("Demo")` check with it. Do not reintroduce it under any name,
  and do not mirror a new feature or requirement into an evaluation, sample or offline mode.
  It was a second implementation of the product that nobody deployed: SQLite has no `ROWVERSION`,
  so every concurrency guard ran as code with nothing behind it, and `EnsureCreated()` builds a
  schema no migration ever produced. Keeping both alive meant writing each feature twice and
  verifying it once, on the copy that could not fail the way production fails — the SQLite
  `DateTimeOffset`-to-ticks converter and the RowVersion strip in `OnModelCreating` were exactly
  that tax. SQL Server is the only store, in every environment. The InMemory provider stays,
  because a test suite is not a second product.
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
- User-facing words come from the wording layer, never from a local PascalCase split:
  `StatusLabels` on the server, `core/labels.ts` on the client. Change both in the same edit.
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

Since then, `docs/04-IMPROVEMENT-PLAN.md` has taken it through three rounds of test feedback.
As of 2026-08-24 **that plan is complete** — every item across all three feedback rounds: the
wording layer, Quick Work, the needs-attention/recent-activity home screen, role-scoped task detail
(enforced server-side), the history split, empty states, the responsive pass, confirmations, PDF
export, multi-item requests, and the quick-view drawer.

Added on top of it, 2026-08-24: **proof of work and quality-check evidence** — `AttachmentKind`
separates the picture describing a problem from the picture proving it was fixed and from what a
checker looked at, with the proof gated on being the person responsible for the work and the
evidence kept with the numbered attempt it justified.

Added 2026-08-26: **Verification** — assigned investigation, distinct from both task QC and Quick
Work. A reviewer who cannot yet tell whether a request describes a real problem sends it to a
checker instead of guessing or approving to find out; an authorised user can also raise one against
a form, a module or a build with no request behind it at all. Every result hands the request back to
a reviewer, and nothing it can do creates a task. In the same change, `Administrator` stopped being
granted `Workforce.TrackShift` and `Task.Work` by default.

**Tests:** 397 passing (`dotnet test`) — 29 domain state machines, 368 application services.
All on EF Core InMemory or pure functions, so the suite runs with no SQL Server.

## 9. SQL Server: done and still outstanding

SQL Server 2019 Developer Edition runs on the `localhost` default instance. `WorkflowApp_Dev` has
been created, migrated and seeded, and every phase has been driven end to end over HTTP.

**`WorkflowApp_Dev` is the only local database.** A second one, `WorkflowApp`, existed until
2026-08-25: it was created accidentally by a `dotnet run` that fell back to base `appsettings.json`
instead of the Development profile, held only `InitialCreate` and the bootstrap admin, and was
seven migrations behind. It has been dropped. That fallback is what to watch for: a run that does
not resolve the Development profile reads base `appsettings.json` and points at `WorkflowApp`
instead. `Development` is now the only profile, so a bare `dotnet run` selects it.

> **Second dev machine (DESKTOP-2E2D7JE) has no default instance.** It exposes
> `localhost\SQLEXPRESS` (SQL Server 2022 Express) and `(localdb)\MSSQLLocalDB` (2025 Express)
> instead, so `Server=localhost` fails there with a Named Pipes error. Do **not** edit the tracked
> `appsettings.Development.json` for it - that would break the primary machine. It is configured
> with **user-secrets** instead, which override per machine and are not in the repo:
>
>     dotnet user-secrets set "ConnectionStrings:Default" 'Server=(localdb)\MSSQLLocalDB;Database=WorkflowApp_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true' --project src/WorkflowApp.Api
>
> `WorkflowApp.Api.csproj` therefore carries a `UserSecretsId`. On the primary machine no secret is
> set, so `appsettings.Development.json` still wins and nothing changes there.
>
> `wwwroot/` is gitignored and was never committed, but `WebApplication.CreateBuilder` throws
> `DirectoryNotFoundException` when it is absent - a fresh clone cannot start the API until the
> directory exists. `mkdir src/WorkflowApp.Api/wwwroot` is enough.

Verified:

- [x] `InitialCreate` applied and recorded in `__EFMigrationsHistory`. Phases 7-12 added no schema;
      `QuickWork`, `RequestBatches` and `AttachmentProof` applied on top, on startup, since
- [x] Proof and evidence driven end to end over HTTP: a checker is refused `CompletionProof`
      (403 `attachment.not_assignee`) and a worker is refused `QCEvidence`
      (403 `attachment.not_checker`); the assignee's proof comes back under `completionProof`,
      and each numbered QC attempt keeps its own screenshots across a fail-then-pass cycle
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

- [x] Refresh rotation verified: a rotated token is rejected as `auth.refresh_token_reused`, and
      reuse revokes the whole family by design (`AuthService` L169-172), so the replacement token
      dies with it. A clean chain of 3 rotations succeeds
- [x] **ROWVERSION race confirmed on real SQL Server.** Two simultaneous assignments carrying the
      same stale `rowVersion`: one 200, one 409 `task.concurrency_conflict`
- [x] Stale-shift sweep closes an artificially old open shift, stamping `EndedImproperly=1` and a
      `Workforce.ShiftAutoClosed` audit row. It ends the shift at the last sign of life, not at
      sweep time - a 20h-old shift closed at its own start timestamp
- [x] `UX_ShiftSession_OneOpenPerUser` rejects a second open shift at the database level, not just
      in the service. (Raw inserts need `sqlcmd -I`; filtered indexes require `QUOTED_IDENTIFIER ON`)
- [x] Phase 2 timeline renders labelled intervals per state. Note `Break -> Lunch` is *not* a legal
      move: away states reach each other only via Available or Working, per `WorkforceStateMachine`
- [x] Placeholder `Jwt:SigningKey` refuses to boot outside Development, as intended
- [x] **Verification driven end to end over HTTP against SQL Server** (36 checks, all passing):
      request → send for checking → the requester reads "Being Checked" while the reviewer reads
      "Being verified" → approve and reject both refused with `request.verification_pending` →
      only the assigned checker may start it or attach evidence (a reviewer gets 403 on both) →
      findings recorded → request back in `InReview` with **no task** → approval is what creates
      the task. Plus an independent check with no request behind it, a checker who cannot be
      assigned one (`verification.checker_cannot_work`), 404-not-403 scoping, and the original
      request→approval→task pipeline still running unchanged
- [x] The seeder's **additive** behaviour confirmed live: `Verification.*` was backfilled onto the
      existing roles on restart, and the Administrator role kept the `Workforce.TrackShift` it
      already had. The new default (`AdministratorGrants`) applies to a fresh database; an existing
      one removes it in the role editor
Still outstanding before this is anything but a dev box:

- [ ] Change the bootstrap admin password away from `ChangeMe!2024`
- [ ] Set a real `Jwt:SigningKey` via `Jwt__SigningKey` env var / user-secrets - startup refuses
      the placeholder outside Development
- [ ] Set a real `Workforce:TimeZoneId` - it defaults to UTC, which will skew daily reports
- [ ] Point `FileStorage:Root` at a real directory for non-Development environments
- [ ] Phase 9: connect a real SignalR client and watch a task update arrive. Only the negotiate
      handshake has been exercised so far, not an actual pushed message
- [ ] Phase 9: SignalR group membership is per-process. Running more than one instance needs a
      Redis backplane, or sticky sessions
- [x] **Cleared 2026-08-25.** `WorkflowApp_Dev` is back to `admin` alone plus the seeded catalogue,
      via `scripts/sql/reset-dev-data.sql`; the orphaned files under
      `src/WorkflowApp.Api/storage-dev` were deleted by hand (the script never touches disk) and
      `NumberSequences` reset, so the next request is `REQ-000001`. Clients/Departments/Teams/
      Projects/Modules went with it - they are **not** seeded, so a client has to be re-added
      before a request can be raised
- [ ] The PDF export resolves a font off the machine (`FileSystemFontResolver`). A stripped Windows
      Server Core or a slim Linux container may have none of Segoe UI / Arial / DejaVu / Liberation,
      and startup will refuse with the list it looked for. Install one, or ship a `.ttf`

Everything above is covered operationally by `docs/03-RUNBOOK.md`.
