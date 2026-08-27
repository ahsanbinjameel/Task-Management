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

## Phase 7 — QC & Closure (DONE — verified against SQL Server)
- [x] `QCService` — claim, verdict, history. Numbered attempts, append-only: a pass never erases
      an earlier failure
- [x] Segregation of duties: the assignee cannot QC their own work, and a nominated QC owner
      cannot be displaced mid-review
- [x] Acceptance criteria parsed line-by-line from the task; every one must be evaluated and met
      before QC can pass; the evaluation is stored on the attempt
- [x] Failure requires comments and returns the task to rework, never to closed. A QC query is
      recorded as an attempt and leaves the task under review
- [x] `ClosureService` — a named, inspectable checklist (QC passed, criteria still met, resolution
      written, no running timer, no open subtasks) exposed as its own read endpoint
- [x] QCPassed / QCFailedRework / Closed cannot be reached through the generic transition endpoint,
      so those states always have their record behind them. An explicit override still can

## Phase 8 — Comments / Dependencies / Subtasks / Scope / Reopen (DONE — verified against SQL Server)
- [x] Categorized comments, append-only. Visibility defaults come from the category, so an internal
      note is hidden unless somebody deliberately shares it; filtering happens server-side on read
- [x] Management notes are readable and writable only with `Dashboard.Management`
- [x] Dependency graph. Only `DependsOn`/`Blocks` impose an order, so only those are cycle-checked
      (breadth-first over the ordering edges) and only those produce a blocked signal
- [x] Blocked is enforced, not just displayed: the timer refuses to start on a task waiting on
      unfinished work (`task.blocked_by_dependency`)
- [x] `ParentChild` rejected as a dependency type — parentage lives on `WorkTask.ParentTaskId`
- [x] Subtasks are real tasks: own number, assignee, timer, history. One level deep, and the parent
      cannot close while one is open
- [x] Scope changes are recorded when requested and only move the estimate/deadline when approved,
      so a bad estimate stays distinguishable from scope creep
- [x] Controlled reopen: `Task.Reopen` (held by Reviewer), mandatory reason, audited — and a
      reopened task needs a **fresh** QC pass before it can close again

## Phase 9 — Real-Time (SignalR) (DONE — verified against SQL Server)
- [x] `WorkflowHub` at `/hubs/workflow`, notification-only: no method on it can change state.
      Group membership is derived from the token, never from the client
- [x] Groups: `user:{id}`, `task:{id}`, `perm:{key}`. Names built in one shared place so sender and
      receiver cannot drift
- [x] Integration events are derived from the change tracker in a `SaveChangesInterceptor`, so no
      code path can forget to notify — and dispatched in `SavedChanges`, so nothing is announced for
      a save that rolled back
- [x] Publishing failures are logged and swallowed: a dropped notification must never fail the
      transaction that caused it
- [x] Payloads are thin (id, number, status, kind). Clients re-fetch — the DB is the source of truth
- [x] Reconnect: `OnConnectedAsync` re-runs and re-joins the groups; the client re-fetches what it
      missed. There is no server-side replay buffer, by design

## Phase 10 — Dashboards & Reports (DONE — verified against SQL Server)
- [x] Four dashboards, one per audience: requester, worker, coordinator, management. The personal
      ones are scoped to the caller's own id — no user parameter to tamper with
- [x] Management view: throughput, QC pass rate, average cycle time, hours worked, open by
      status/priority. Closures counted from the status trail, so a reopened-and-reclosed task is
      not double-counted
- [x] Daily user and team reports, sharing `DailyTimelineBuilder` with the workforce screens so a
      report and a timeline can never disagree
- [x] CSV export with proper quoting
- [x] **Deviation:** written as EF queries, not stored procedures — one definition of the schema,
      covered by the same test suite, no second artefact to keep in step through a migration
- [ ] Print styling — belongs to the front end, which does not exist yet

## Phase 11 — Notifications & Audit (DONE — verified against SQL Server)
- [x] In-app notifications: assignment, QC verdict, closure, reopen. A notification is a pointer
      (title + link), not a copy, so it cannot go stale
- [x] The actor is never notified of their own action
- [x] Read/unread, unread count, mark-read scoped to the owner — another user's id marks nothing
- [x] Every notification also goes out over SignalR to its recipient's group
- [x] Audit stream with filters (action, entity, actor, date range), gated on `Admin.ViewAudit`,
      and deliberately read-only: no route edits or deletes an entry
- [x] Activity timeline was delivered in Phases 2 and 4 — the workforce stream
      (`/api/shifts/activity`) and the per-task stream in the task detail

## Phase 12 — Hardening (DONE, except the UI item)
- [x] rowversion concurrency on every mutable aggregate: User, Request, WorkTask, WorkSession,
      ShiftSession — verified as real `timestamp` columns on SQL Server
- [x] Idempotency keys on task transitions
- [x] Security headers: `nosniff`, `DENY` framing, Referrer-Policy, Permissions-Policy, and a CSP
      that relaxes for inline script only where Swagger is actually served
- [x] Global rate limit per user (falling back to IP), on top of the tighter credential-endpoint policy
- [x] Upload validation: extension allow-list, size cap, generated stored names, path-traversal
      guard, SHA-256 — built in Phase 3, re-checked here
- [x] CSRF: **not applicable and deliberately not implemented** — auth is a bearer token in a header,
      never a cookie, so a cross-site request cannot carry the caller's credentials. Revisit only if
      anything moves to cookie auth
- [x] Readiness probe (`/health/ready`) separate from liveness, so a database outage pulls the
      instance from the load balancer instead of triggering a restart loop
- [x] `scripts/deploy.ps1` and `docs/03-RUNBOOK.md`
- [ ] Responsive UI polish — **blocked**: the Angular front end has not been started

## Phase 13 — Verification (DONE — verified against SQL Server)

Assigned investigation, added 2026-08-26. Not a phase in the original plan: it came out of the
observation that a reviewer holding "the salary form calculates tax wrongly" has no honest way
forward, because every option available to them either guesses or commits the organisation to work.

- [x] `Verification` + `VerificationActivity` aggregates, `VER-` number sequence, `RowVersion`
- [x] `VerificationStatus` / `VerificationResult` / `VerificationTargetType`;
      `RequestStatus.UnderVerification`
- [x] `Verification.Create` / `.Work` / `.ViewAll`, seeded onto Reviewer, AssignmentManager, QC and
      Management
- [x] `TriageOutcome.SendForVerification` — the seventh outcome, and the sixth that creates no work
- [x] Every result returns the request to `InReview`; **no result creates a task**
- [x] No decisive triage outcome is allowed while a check is open
- [x] `AttachmentKind.VerificationEvidence` and a fourth attachment owner
- [x] `VerificationChangedEvent` derived from the change tracker like every other event
- [x] Angular: the checks list, the checker's detail screen, the raise dialog, the triage action,
      and the checks panel on the request
- [x] `RequestStatus.UnderVerification` reads as "Being Checked" to a requester and "Being verified"
      to a reviewer
- [x] Driven end to end over HTTP against SQL Server, the way phases 3-12 were — 36 checks

Also in this change: `Administrator` stopped being granted `Workforce.TrackShift` and `Task.Work`
by default. Note the seeder is additive, so an existing database keeps what it has.

## Not started

Nothing outstanding in the plan. The Angular front end, listed here as not started when this
document was written, is built and covers the whole pipeline.

## Key Business Rules (guardrails for every phase)
1. A request never auto-becomes a task.
2. Only one active primary work session per user.
2b. Shifts are tracked only for people who execute tasks (`Workforce.TrackShift`).
3. No status transition outside the allowed map.
4. Every important transition is permission-checked server-side.
5. Reason mandatory for: reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only; nothing overwrites prior comments/sessions/QC attempts.
7. DB is source of truth; SignalR only notifies.
8. A verification never creates work — findings go back to a reviewer, who decides.
9. `Task.Work`, `Verification.Work` and `Workforce.TrackShift` are independent of one another.

## Known Edge Cases to Handle
- ~~User closes browser without ending shift~~ → handled in Phase 2: `ShiftMaintenanceService` sweep.
- ~~Double task start / double-click transitions~~ → handled: idempotency keys + single-session rule.
- ~~Two coordinators assign same task~~ → handled: row-version check rejects the loser.
- ~~Interruption must preserve original task's paused session~~ → handled in `WorkSessionService.InterruptAsync`.
- ~~Clarification reply must return request to review~~ → handled in `RequestTriageService`.
- ~~Failed QC must land in rework, not Closed~~ → enforced by the workflow map.
- ~~Closing work nobody signed off~~ → handled in Phase 7: the closure checklist.
- ~~Criteria edited after QC passed~~ → handled: verdicts only carry over while the text matches,
  so widening the criteria reopens the closure gate.
- ~~Request decided out from under a running check~~ → handled in Phase 13: every decisive triage
  outcome is refused with `request.verification_pending` while a check is open.
- ~~Check called off leaves the request stranded~~ → handled: cancelling returns it to `InReview`.
