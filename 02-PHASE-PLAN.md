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

## Phase 1 — Identity & Authorization
- [ ] ASP.NET Core Identity setup, ApplicationUser
- [ ] Roles + Permissions tables, seed default roles
- [ ] JWT access + refresh token issuance/rotation
- [ ] Login / logout / refresh endpoints
- [ ] Password change / reset, account activate/deactivate
- [ ] Login attempt logging, lockout
- [ ] Permission-based authorization policies + attribute

## Phase 2 — Shift & Workforce States
- [ ] ShiftSession entity + start/end endpoints
- [ ] WorkforceState machine + transitions (Available/Working/Break/Lunch/Meeting/Away)
- [ ] ActivityEvent log (login, shift start, break, etc.)
- [ ] "Who's working now" query + admin view
- [ ] Daily timeline generation from events

## Phase 3 — Request Intake & Triage
- [ ] Request form fields + create endpoint
- [ ] Attachments (disk storage + metadata + authorized download)
- [ ] Review queue + triage outcomes (approve/reject/clarify/duplicate/defer/escalate)
- [ ] Clarification loop with full comment history

## Phase 4 — Task Creation & Workflow Engine
- [ ] Request→Task conversion on approval
- [ ] Wire TaskWorkflow into a transition service
- [ ] Permission-gated + reason-required transitions
- [ ] Requested urgency vs approved priority

## Phase 5 — Assignment & Queue
- [ ] Assignment queue + assign endpoint (concurrency-safe)
- [ ] Primary / supporting / reviewer / QC roles per task
- [ ] Ordered worker queue + reorder
- [ ] Assignment/reassignment history
- [ ] Workload view

## Phase 6 — Work Sessions & Timer
- [ ] WorkSession start/pause/resume/block
- [ ] Pause reasons (configurable)
- [ ] Single-active-session enforcement
- [ ] Emergency interruption flow
- [ ] Total duration from sessions

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
3. No status transition outside the allowed map.
4. Every important transition is permission-checked server-side.
5. Reason mandatory for: reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only; nothing overwrites prior comments/sessions/QC attempts.
7. DB is source of truth; SignalR only notifies.

## Known Edge Cases to Handle
- User closes browser without ending shift → "did not log out properly" flag + auto-detect.
- Double task start / double-click transitions → idempotency + single-session index.
- Two coordinators assign same task → optimistic concurrency rejects the loser.
- Interruption must preserve original task's paused session, not discard it.
- Clarification reply must return request to review, not skip ahead.
- Failed QC must land in rework (InProgress), not Closed.
