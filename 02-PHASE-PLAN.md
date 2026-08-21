# Phase Plan

Each phase is independently shippable. Complete a phase, verify, then move on.

## Phase 0 — Foundation (DONE in this scaffold)
- [x] Solution + project structure (Api / Application / Domain / Infrastructure)
- [x] Base entity conventions (Id, CreatedAt, UpdatedAt, RowVersion)
- [x] Core enums (statuses, priorities, workforce states, roles)
- [x] Domain entities for identity, workforce, requests, tasks
- [x] DbContext + entity configurations
- [x] Workflow state machine (allowed-transition map)
- [x] Config for dev/staging/prod

## Phase 1 — Identity & Authorization (DONE — code complete, DB steps pending)
- [x] Custom identity tables + ASP.NET Core Identity password hashing (see CLAUDE.md §6 for why)
- [x] Roles + Permissions tables, idempotent seeder for default roles and grants
- [x] JWT access + refresh token issuance/rotation (refresh tokens stored hashed, reuse detected)
- [x] Login / logout / refresh / me endpoints
- [x] Password change (self) + admin reset, account activate/deactivate
- [x] Login attempt logging, failed-count lockout, rate limiting on credential endpoints
- [x] Permission-based authorization policies + [HasPermission] attribute
- [x] Global ProblemDetails exception middleware; auditing SaveChanges interceptor
- [x] InitialCreate migration + idempotent SQL script (scripts/sql/)
- [ ] Apply migrations to a real SQL Server and verify end-to-end  ← needs SQL Server

## Phase 2 — Shift & Workforce States (DONE — code complete, DB steps pending)
- [x] ShiftSession start/end endpoints, plus supervisor force-end with mandatory reason
- [x] Shift tracking scoped to people who execute tasks, via the `Workforce.TrackShift` permission
- [x] WorkforceState machine (`Domain/Workflow/WorkforceStateMachine.cs`) — allowed-transition map
      with the timeline label on each transition
- [x] ActivityEvent log written on login, logout, shift start/end and every state change
- [x] "Who's working now" query + per-user status, timeline, activity and shift history
- [x] Daily timeline generation from events, with business-timezone day boundaries and
      overnight-shift carry-over
- [x] Improper-logout detection: background sweep closes abandoned shifts at the last sign of
      life, flags `EndedImproperly`, and audits it
- [ ] Verify against a real SQL Server  ← needs SQL Server

## Phase 3 — Request Intake & Triage (DONE — code complete, DB steps pending)
- [x] Request create/update/list endpoints; edit locked once triage has acted
- [x] Attachments: disk storage with generated names, path-traversal guard, extension allow-list,
      SHA-256, authorized+audited download
- [x] Review queue (urgency first, then oldest) + all six triage outcomes
- [x] Clarification loop; answering returns the request to review, never straight to approved
- [x] `REQ-nnnnnn` numbering via a concurrency-guarded sequence table

## Phase 4 — Task Creation & Workflow Engine (DONE — code complete, DB steps pending)
- [x] `TaskCreationService` — the single place a task is born, called only by triage approval
- [x] `TaskWorkflowService` — persists transitions, appends StatusHistory + TaskActivity, echoes
      onto the workforce timeline, closes open sessions when leaving InProgress
- [x] Permission-gated (403) and reason-required transitions, distinguished from illegal moves (409)
- [x] Overrides: permission + reason required, flagged in history and written to the audit log
- [x] Idempotency keys — a double-clicked transition applies once
- [x] Requested urgency is advisory; approved priority schedules the work

## Phase 5 — Assignment & Queue (DONE — code complete, DB steps pending)
- [x] Assignment queue + assign endpoint guarded by the row version (second coordinator loses)
- [x] Primary assignee, supporting collaborators, reviewer and QC roles; QC may not be the assignee
- [x] Ordered per-assignee queue; new work joins the end; reorder restricted to your own tasks
- [x] Append-only assignment/reassignment history; reassigning requires a reason
- [x] Workload view (open / running / blocked / outstanding hours / what they are on now)
- [x] `assignable-users` directory scoped to `Task.Assign` — no user-admin rights needed

## Phase 6 — Work Sessions & Timer (DONE — code complete, DB steps pending)
- [x] Start / pause / resume / block / complete; completing lands in QC, never Closed
- [x] Configurable pause reasons, including ones that demand a comment
- [x] Single-active-session enforced three ways: pre-check, atomic switch, and the filtered index
- [x] Emergency interruption — pauses the running task, preserving its time, and starts the urgent
      one in one commit
- [x] Total time is the sum of closed sessions, not start-to-finish elapsed
- [x] Work requires an open shift; only the assignee can run the timer

## Phase 7 — QC & Closure
- [ ] QC review entity, pass/fail/rework with history
- [ ] Acceptance criteria + evaluation
- [ ] Closure requirement rules

## Phase 8 — Comments / Dependencies / Subtasks / Scope / Reopen
- [ ] Categorized comments + visibility rules
- [ ] Dependency graph + blocked-by UI signal
- [ ] Subtasks with own assignee/history
- [ ] Scope-change records
- [ ] Controlled reopen with reason

## Phase 9 — Real-Time (SignalR)
- [ ] Hub + groups
- [ ] Integration events after commit
- [ ] Broadcaster mapping events → groups
- [ ] Reconnect handling + client re-fetch

## Phase 10 — Dashboards & Reports
- [ ] Requester / worker / admin / management dashboards
- [ ] Daily user + team reports (stored procs)
- [ ] Export/print

## Phase 11 — Notifications & Audit
- [ ] In-app notifications
- [ ] Activity timeline
- [ ] Audit log stream

## Phase 12 — Hardening
- [ ] rowversion concurrency everywhere
- [ ] Idempotency keys
- [ ] Security pass (CSRF, XSS, upload validation, rate limiting)
- [ ] Responsive UI polish
- [ ] Deployment scripts + runbook

## Key Business Rules (guardrails for every phase)
1. A request never auto-becomes a task.
2. Only one active primary work session per user.
2b. Shifts are tracked only for people who execute tasks (`Workforce.TrackShift`).
3. No status transition outside the allowed map.
4. Every important transition is permission-checked server-side.
5. Reason mandatory for: reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only; nothing overwrites prior comments/sessions/QC attempts.
7. DB is source of truth; SignalR only notifies.

## Known Edge Cases to Handle
- ~~User closes browser without ending shift~~ → handled in Phase 2: `ShiftMaintenanceService` sweep.
- ~~Double task start / double-click transitions~~ → handled: idempotency keys + single-session rule.
- ~~Two coordinators assign same task~~ → handled: row-version check rejects the loser.
- ~~Interruption must preserve original task's paused session~~ → handled in `WorkSessionService.InterruptAsync`.
- ~~Clarification reply must return request to review~~ → handled in `RequestTriageService`.
- ~~Failed QC must land in rework, not Closed~~ → enforced by the workflow map.
