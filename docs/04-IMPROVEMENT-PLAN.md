# 04 — Improvement & Stabilisation Plan

Response to the first-round test feedback (26 items). Sequenced by dependency, not by the order the
issues were reported: several reported symptoms share one root cause, and fixing the cause once is
cheaper and safer than patching each screen.

> **Status legend:** ✅ done · 🚧 in progress · ⛔ not started

---

## Root causes found during review

The review turned up five defects that each explain several reported symptoms. These are stated
first because they change what the rest of the plan has to do.

### RC1 — Dialogs are data collectors, so validation loses the user's work

Every dialog in the app closes *before* the server is called:

```ts
// users.component.ts:81, assign-dialog.ts:96, stop-work-dialog.ts:95, dialogs.ts:69
confirm(): void { this.ref.close({ userName: ..., password: ... }); }
```

The caller then makes the API request. When the server rejects it, the modal is already gone, the
input is discarded, and `errorInterceptor` raises a global toast — which is exactly the reported
"modal closes, error appears outside, everything is lost".

This is the app-wide dialog convention, so **item 1 is one architectural fix, not a per-form patch**:
invert the pattern so the dialog owns the submit, stays open on failure, and renders field errors
inline.

### RC2 — QC criteria checklist can never be satisfied

```ts
readonly verdicts = signal<Verdict[]>([]);           // array of mutable objects
[(ngModel)]="v.met"                                   // mutates an object inside the signal
readonly unmet = computed(() => this.verdicts().filter(v => !v.met)...)
```

`ngModel` mutates an object *inside* the array. The signal reference never changes, so `unmet()`
never recomputes and stays frozen at its initial "nothing ticked" value. That is why the warning
reads `Unmet: 1, 2, 3, 4` no matter how many boxes are ticked, and why `valid()` keeps the submit
button disabled. The backend rule in `QCService.Evaluate` is correct; the client never lets a valid
payload leave.

### RC3 — Real-time is wired correctly on the server but only half-consumed on the client

The server side is sound: `IntegrationEventDispatchInterceptor` collects events off EF's
ChangeTracker on every save, so task / request / workforce-state / notification changes all publish
automatically, and `SignalRIntegrationEventPublisher` is correctly registered over the no-op.

Only 8 components subscribe. **The dashboard subscribes to nothing**, which is precisely why counts
and shift status go stale. Task list, request list and request detail are also unsubscribed.

Separately, only `task-detail` has an `ngOnDestroy`. Every other subscription to the root-scoped
`Subject`s leaks, so navigating away and back multiplies the reloads fired per event.

### RC4 — Notifications are raised from only three services

`INotificationService.RaiseFor` is called from `TaskAssignmentService`, `QCService` and
`ClosureService` only. Nothing raises on request submitted, approved, rejected, clarification
requested, or requester replied — so the bell is empty through most of the workflow.

### RC5 — "My day" has no permission gate

`shell.component.ts:119` declares the *My day* nav item with no `permissions` array, so every
Requester and Manager sees worker-only tooling. `Workforce.TrackShift` already exists and is already
independent of role name — it is exactly the "Uses Work Tracking" capability item 4 asks for, so
this is a gating fix, not a new permission model.

---

## Phase A — Foundations

Everything downstream depends on these. Done once, centrally.

| # | Work | Covers |
|---|---|---|
| A1 🚧 | **Wording layer.** *`StatusLabels` (server) is the first half — status names now translate in one place.*  One source-of-truth map of status/role/action labels + supporting text, so terminology is changed in one file rather than scattered across 47 components. | 22, and the label half of 1, 5, 7, 23, 24 |
| A2 ✅ | **Form/dialog inversion.**  Shared `FormDialogBase`: dialog owns submit, stays open on error, maps `ProblemDetails` → inline field errors, focuses first invalid field, never resets input. | 1 |
| A3 ✅ | **Human error messages.**  Map stable error codes (`closure.not_ready_to_close`, `qc.criteria_unmet`, …) to plain sentences that say what to do next. Feeds A2 and the toast path. | 24, 17 |
| A4 ✅ | **Real-time hygiene.**  `takeUntilDestroyed` on every subscription; subscribe the screens that are missing (dashboard first); de-duplicate bursts. | 8 |

## Phase B — Blocking defects

Small, high-value, unblock testing of everything else.

| # | Work | Covers |
|---|---|---|
| B1 ✅ | QC checklist signal fix (RC2) — make verdicts immutable updates so `computed` tracks them | 14 (blocking half) |
| B2 ✅ | Email optional: `CreateUserRequest` DTO, client validation, and every read path that assumes an address | 6 |
| B3 ✅ | Gate *My day* / shift widget / work timer on `Workforce.TrackShift` | 4 |
| B4 ✅ | Blank page / `undefined` during review — trace response shape, null-guard, error boundary | 17 |

## Phase C — Data model & business rules

Server-side changes; several need a migration.

| # | Work | Covers |
|---|---|---|
| C1 ✅ | **Support Person.** `TaskCollaborator` exists — guarantee it never counts as ownership in queue, workload, counts or overdue stats. Rename in UI to Support Person. | 20, 21 |
| C2 ✅ | **Subtasks.** `IsRequired` flag; block parent completion while required subtasks are open, with a clear message. Show subtasks on the parent page. | 13 |
| C3 ✅ | **Pause reason.** Category (existing lookup) + free-text details, both persisted, both reportable. | 12 |
| C4 ✅ | **Client on task.** Surface Client/Project/Module at creation and through detail, assignment, reports, filters. | 19 |
| C5 ✅ | **Request edit before approval.** Editable fields pre-approval, change history, reviewer notification, blocked after approval. | 3 |
| C6 | **Quick Work.** New lightweight entity + lifecycle: start in seconds, auto-pause current task, record interruption, resume, outcome, optional promotion to a real Request/Task, appears in daily report. | 15 |

## Phase D — Experience

| # | Work | Covers |
|---|---|---|
| D1 ✅ | QC redesign: Pass / Fail / N/A per criterion, three outcomes with required explanations | 14 |
| D2 | Dashboard split: **Needs Attention** vs **Recent Activity** | 5 |
| D3 🚧 | Status tiles with counts on Tasks and Requests ✅; navigation depth review still open | 7 |
| D4 ✅ | Notification matrix per role (RC4) with unread count, mark read, deep links | 9 |
| D5 | Role-scoped task detail — sections, fields and actions per role, enforced server-side too | 10, 11 |
| D6 | Split user-facing activity history from technical audit | 16 |
| D7 | Empty states that explain and offer the next action | 23 |
| D8 | Responsive pass: overlap, wrapping, tables, modal scroll, mobile layouts | 2 |
| D9 | Confirmation dialogs on destructive/irreversible actions | closing note |

## Phase E — Reporting

| # | Work | Covers |
|---|---|---|
| E1 | Purpose-built PDF export (header, summary, work detail, quick work, interruptions, notes, page numbers) | 18 |
| E2 🚧 | Reports separate owned work / supported work *(quick work still to come)* | 21 |

## Phase F — Scenario testing

End-to-end scenarios A–G from the brief, run as flows rather than per-screen checks.

---

## Invariants that must survive all of the above

Carried from `CLAUDE.md` §5 plus the brief's item 25:

1. A request never auto-becomes a task — approval creates it, in one place.
2. One active primary work session per user (Quick Work must respect this, not bypass it).
3. No status transition outside `TaskWorkflow.Transitions`.
4. Every mutating transition is permission-checked server-side; hiding a button is not security.
5. Reason mandatory for reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only.
7. Database is the source of truth; SignalR only notifies.
8. Support People never own a task and never appear in ownership statistics.
9. Completion ≠ Closed; QC precedes closure where required.
10. Required subtasks block parent completion.


---

## Progress log

### 2026-08-22 — first execution pass

**Done and verified against SQL Server (258 tests green, live API exercised):**

- **B1 + D1 — quality check.** Root cause RC2 fixed by replacing verdicts immutably instead of
  mutating objects inside a signal. Verdicts are now three-way (Pass / Fail / N/A) end to end:
  `AcceptanceCriterionVerdictDto.Met` became `bool?`, and `QCService.Evaluate` treats *no entry* as
  unanswered, `null` as not applicable, and only `false` as blocking. Verified live: all-items-ticked
  passes (the reported bug), N/A does not block a pass, an explicit Fail is refused, an unanswered
  item is refused, and the same task records as *Needs fixing* into `QCFailedRework`.
  Panel rewritten with plain wording and per-item Pass/Fail/N/A controls.
- **B2 — email optional.** `User.Email` is nullable through entity, DTOs, services and JWT (the
  email claim is now omitted rather than null). Migration `OptionalUserEmail` replaces the unique
  index with a **filtered** one — without the filter SQL Server would have allowed exactly one
  user without an address. Verified live: two users created with no email, and sign-in by username
  alone works.
- **B3 — worker-only tooling.** *My day* is gated on `Workforce.TrackShift`, the capability that
  already existed, so it follows the permission rather than a role name.

**Started:**

- **A2** — `core/form-errors.ts` parses both ASP.NET validation problems and our business-rule
  codes into per-field messages, and `HANDLED_LOCALLY` stops the global interceptor double-reporting
  what a form already shows inline. Create User is converted: it owns its submit, sets
  `disableClose`, keeps every value on failure, renders messages against the field, and focuses the
  first invalid one. **Every other dialog still uses the old close-then-POST pattern.**
- **A4** — the dashboard now merges task/request/workforce events, debounces 250 ms so one approval
  refreshes once rather than three times, and tears down with `takeUntilDestroyed`. **The other
  unsubscribed screens, and the missing teardown on the existing 8 subscribers, remain.**

**Not started:** A1, A3, B4, all of C, D2–D9, E, F.


### 2026-08-22 — second execution pass (Priority 1 foundations)

Everything below was verified against SQL Server with the API running, not merely compiled.
258 tests green; client and server both build.

**A2 — forms and modals, application-wide.** Two shared pieces did the work: `core/form-submit.ts`
(`FormSubmit` — busy/failure state, per-field errors, focus-first-invalid, and the rule that a
failed submit changes *nothing* but the message) and the `HANDLED_LOCALLY` HttpContext token, which
stops `errorInterceptor` double-reporting what a form already shows inline. An `HttpContext`
parameter was threaded through the 25 mutating `ApiService` methods forms use.

Every dialog now owns its submit and closes only on success or explicit cancel:

| Dialog | Was | Now |
|---|---|---|
| Create user | closed, then caller POSTed | submits, stays open on failure, inline field errors |
| Assign / reassign | returned a payload | performs the assignment; a row-version conflict keeps the form |
| Pause / cannot continue | returned a payload | performs it; keeps the reason on failure |
| Reason (5 call sites) | returned text | optional `submit` callback performs the action |
| Confirm | returned true | optional `submit`, with inline failure |

**A4 — real-time, application-wide.** `core/realtime-sync.ts` (`syncOn`) encodes the three rules:
re-fetch rather than patch, debounce 250 ms to coalesce bursts and duplicate deliveries, and tear
down with `takeUntilDestroyed`. Rolled out to **14 screens** — dashboard, task list, task detail,
my queue, assignment queue, QC queue, request list, request detail, review queue, workload,
who's-working, my day, notification bell, shift widget. Before this, the dashboard subscribed to
nothing and only `task-detail` had teardown.

*Verified with two simultaneous live hub connections* (worker + coordinator) while a third person
made changes over REST: the coordinator saw a new task without refreshing, the worker received both
`notification` and `taskChanged` on assignment, and group membership was restored after a
disconnect/reconnect.

> **Known and absorbed:** `Clients.Groups(...)` delivers one copy per matching group, so a
> coordinator who is in both the `Task.Assign` and `Workforce.ViewAll` groups receives the same
> event twice. The 250 ms debounce collapses it into one re-fetch. Left as is — de-duplicating
> server-side would mean resolving group membership per connection on every publish.

**D4 — notifications.** `RaiseForPermissionAsync` addresses a *capability* rather than a role name,
so notifications stay correct when someone rearranges who does what. Wired to the events that need
someone's attention, each verified end to end with five separate accounts:

| Event | Who is told | Verified |
|---|---|---|
| Request submitted | everyone who can review | ✅ |
| More information needed | the requester | ✅ |
| Requester replied | the reviewer who asked | ✅ |
| Request approved | requester, plus coordinators (work to place) | ✅ |
| Request rejected / postponed | the requester | ✅ |
| Task assigned | the responsible person | ✅ |
| Ready for quality check | everyone who can check | ✅ |

Bell operations verified: unread count, mark one read, mark all read, unread-only filter, and
deep links carrying `Request 8` / `Task 9`.

**A3 — error messages.** The reported "invalid transition" text came from *two* places, not one:
`ExceptionHandlingMiddleware` and — the one users actually hit —
`TaskTransitionService.Validate`, which built `"Transition CompletedReadyForQC → Closed is not
allowed."`. Both now use `StatusLabels`, plus the four message sites in `WorkSessionService`.

Live result: *"This cannot be moved from "Waiting for quality check" to "Closed". Refresh the page
to see the current options — someone may have changed it already."*

Client-side, `messageForStatus()` gives every HTTP status a plain sentence (no bare codes), and
`looksTechnical()` blocks stack traces, constraint names (`IX_`/`FK_`/`UX_`), `System.*`,
`Exception`, and literal `undefined`/`null` from ever being rendered as if written for a person.

**Permissions.** *My day* was gated in the menu last pass, but the `/me/day` **route** had no guard,
so it was still reachable by typing the URL. Now `requirePermission(Perm.workforceTrackShift)`.
Shift start/end were already correctly gated server-side (`Workforce.TrackShift` on start only, so
someone whose permission is revoked mid-shift can still clock out).

**Still open in Priority 1:** the wording layer (A1) is only half done — `StatusLabels` covers
status names server-side, but the client still has its own `humanizeEnum`, and role/action labels
are not yet centralised. That is the natural first step of the Priority 4 language pass.

**Next:** Priority 2 — subtask rules, Support Person semantics, Client on task, request editing
before approval, the pause model, and task-detail visibility by role.


### 2026-08-23 — third execution pass (Priority 2, C1 and C2)

268 tests green (+10 new); client and server build; both workstreams verified against SQL Server
with real users, permissions and a live socket.

#### C1 — Support Person

The data model was already sound: every ownership query used `PrimaryAssigneeUserId`, and nothing
unioned collaborators into a queue or workload. What was missing was **visibility and the
invariant** — the DTO exposed bare ids, so no screen could show a support person at all.

| Change | |
|---|---|
| `SupportPersonDto` | ids replaced with name + when added + who added them |
| Cannot be both | adding the responsible person as support was already refused; **assigning a task to an existing support person now ends the support relationship**, so nobody is counted twice |
| Reports | `DailyUserReportDto.Breakdown` split into `OwnedWork` / `SupportWork`, plus `SupportingOn` which names who *is* responsible so a line cannot be misread |
| Real time | adding or removing a support person now announces |
| UI | "Responsible person" / "Support people", each with a one-line explanation; My Day gained a "Tasks I helped with" section |

Verified live (14 checks): helper's queue stays empty, helper absent from workload, not returned by
`?assigneeUserId=`, report shows the task under `supportingOn` naming `c1c_owner` as responsible,
requester refused with 403, owner refused as support with 400, and taking the task over moves it
into the new owner's queue while clearing the support row.

> **Two defects the unit tests could not have caught**, both found only by running against SQL
> Server — worth remembering when judging whether a workstream is really done:
>
> 1. **`OrderBy` after `Select` into a DTO does not translate.** The InMemory provider happily
>    ordered by a member of the constructed `SupportPersonDto`; SQL Server threw. Both projections
>    now order before projecting and join through the navigation property.
> 2. **A `TaskCollaborator` row is not a `WorkTask` row**, so the change-tracker interceptor raised
>    no event and support-person changes were invisible until refresh. The service now enqueues a
>    `TaskChangedEvent` explicitly, still drained by the same save so a rollback cannot announce
>    something that did not happen.

#### C2 — Subtasks

`WorkTask.IsRequired` (migration `RequiredSubtasks`, default `true` so existing subtasks keep
blocking exactly as before, and the column carries a database default rather than relying on the
application).

- **Completion is gated in `WorkSessionService`, not the UI.** The endpoint is reachable directly,
  and a page left open while a subtask reopens would otherwise let a parent through.
- Closure's existing subtask requirement narrowed to *required* subtasks only.
- The parent now carries `SubtaskSummaryDto[]` — number, title, status, responsible person,
  progress, required flag — ordered required-first, so the whole structure reads on one page
  without a second request that could disagree with the first.

Live result, in the words a non-technical reader gets:

> *This cannot be finished yet because 2 smaller tasks still have to be done first
> (TSK-000015, TSK-000016).*

The optional subtask was correctly excluded from both the count and the message; once the required
ones reached a terminal state the parent completed to `CompletedReadyForQC`.

**Next in Priority 2:** C3 pause model (category + free text, shaped so Quick Work can reuse it),
C4 Client on task, C5 request editing before approval, then task-detail visibility by role.


### 2026-08-23 — C3: pause splits into two independent decisions

274 tests green (+6); both builds clean; verified against SQL Server.

**The conflation.** Pausing decided one thing and applied it to two unrelated subjects. Which
status the *task* took came from *which endpoint was called* (`/pause` vs `/block`), not from what
had actually happened — and `ReleaseWorkingStateAsync` always moved the *worker* to `Available`,
whatever the reason. So stopping for lunch produced a task that read as stalled and a worker
recorded as available while they were away from their desk. `PauseReason.IsBlocker` already
existed with the right intent and drove nothing.

**The split.** A reason now answers two genuinely independent questions:

| | Question | Field |
|---|---|---|
| The work | Can the **task** still move on? | `IsBlocker` |
| The person | Where did the **worker** go, if anywhere? | `AwayState` |

| Category | Task | Worker |
|---|---|---|
| Break / Lunch / Meeting | Paused — still claimed, still theirs | Break / Lunch / Meeting |
| Waiting for client / someone / cannot continue | **Blocked** — genuinely cannot proceed | stays Available, free to pick up other work |
| Other work became urgent | Paused | stays free |
| End of shift | Paused | stays free — only the end-shift operation may set `ShiftEnded` |

`ApplyWorkerStateAsync` records an `ActivityEvent` for the move, so the timeline and daily report
show the break for what it is. Without that the time would silently read as productive. The
workforce state machine still governs the transition: an illegal move releases the worker to
Available rather than forcing a state the machine forbids.

Migration `PauseCategoryAndAwayState` adds the two columns **and backfills the rows the seeder
installed earlier** — it only runs on an empty table, so without the backfill every existing reason
would have landed on category 0 with no away-state and the system would have stopped recording
that anyone was at lunch.

**Verified live:** starting a task then choosing Lunch leaves the task `Paused` and the worker
`Lunch`; choosing "waiting for client" leaves the task `Blocked` and the worker `Available`; break
time lands in the daily report.

#### Shaped for Quick Work

The interrupt path already had the right structure and now has the right semantics:

- the interrupted session is **preserved and paused**, never discarded, keeping its recorded time;
- `EndedByInterruption` and `InterruptedByTaskId` record what displaced it;
- the interrupted task goes to **Paused, not Blocked** — nothing is wrong with it, it simply waited;
- the worker stays **Working**, because they are working, just on something else;
- the close and the open commit together, so the one-active-session rule is never briefly violated.

Quick Work can therefore interrupt a task, run its own timed activity, and hand back, without
touching task status semantics. The one piece it will still need is somewhere to point when the
interrupting thing is *not* a task — `InterruptedByTaskId` is task-shaped, so Quick Work will want
a sibling column rather than borrowing that one.

> A verification check of mine was wrong, not the code: I expected resuming a `Blocked` task to be
> refused. `TaskWorkflow` line 55 allows `Blocked → InProgress` deliberately — starting again is how
> a worker says the blocker cleared. Expectation corrected; behaviour left alone.

**Next:** C4 Client on task, C5 request editing before approval, then task-detail visibility by role.


### 2026-08-23 — C4: Client, end to end

274 tests green; both builds clean; verified against SQL Server.

**What was actually wrong.** `WorkTask` already had `ClientId` / `ProjectId` / `ModuleId`, and
`TaskCreationService` already copied them from the request — the inheritance was correct all along.
The chain was simply **dormant**: there was no lookup API, so no picker could exist; the request
form never offered a client, so `Request.ClientId` was always null; and every DTO exposed bare ids,
so even a populated client could not have been displayed. Nothing was broken, nothing worked.

| Added | |
|---|---|
| `ILookupService` + `/api/lookups/{clients,projects,modules}` | projects narrow by client, modules by project |
| Client picker on the request form | three dependent pickers; each clears what no longer applies |
| Names on the DTOs | `ClientName` / `ProjectName` / `ModuleName` on task detail and request detail; `ClientName` on list rows |
| `?clientId=` filter | on the task list, with the row showing which client it is |
| Client at triage | reviewer can set or correct it when approving |

**Lookups are deliberately not permission-gated** beyond being signed in. Anyone who can raise a
request has to be able to say who it is for, and the list carries only a name and a code. Gating it
behind an administration permission is precisely how a field ends up permanently null.

#### Consistency between request and task

The instruction was to avoid duplicating client information that can be inherited safely. So a
reviewer's correction at triage is written to the **request**, and the task inherits from it one
line later — rather than writing to both, which is how the same piece of work ends up filed under
two different clients. Verified live: correcting the client at approval left the request and the
task reporting the same client, not merely both non-null.

The client name resolution is null-safe throughout: a client retired after a task was raised must
not make that task unreadable, and an internal task with no client costs no query at all.

> **A failure in my own verification, not the code.** The first run showed the reviewer's
> correction being lost. The code was right; I had rebuilt only the Application project after the
> change and then started the API with `--no-build`, so it was running a stale copy. A full build
> and restart turned every check green. Worth remembering: `--no-build` after a partial build tests
> yesterday's binary.

**Next:** C5 request editing before approval, then task-detail visibility by role.


### 2026-08-23 — items 17, 19 (reshaped) and 20

274 tests green; both builds clean; verified against SQL Server.

#### Item 17 — the blank page on review

A type lie, not a rendering fault. `POST /requests/{id}/triage` returns **`TriageResult`**
(`status`, `createdTaskId`, `createdTaskNumber`), but the client declared it as `RequestDetailDto`
and assigned the response straight into the `request` signal. Every field the template read came
back undefined, so the page rendered blank — and the redirect never fired either, because it looked
for `generatedTaskId` on an object whose field is `createdTaskId`.

Fixed by naming the real shape (`TriageResultDto`), reading `createdTaskId`, and **re-fetching the
request** instead of assigning the response over it — the same re-fetch-never-patch rule the
real-time layer follows.

#### Item 19 — Client as a typed name

Reworked from pickers to a single free-text field with type-ahead, per the later instruction. There
is deliberately **no client register to maintain**:

- a name typed once is matched against the ones already in use, and created the first time it is seen;
- matching ignores case and surrounding space, so `"  falcon traders "` lands on the existing
  `Falcon Traders` rather than quietly forking it — verified live, same id both times;
- blank means internal work;
- `Internal` and `Head Office` are seeded as starting points.

The Project and Module pickers added in the earlier C4 pass were **removed** — the instruction was
not to add further client-related fields.

> The starter-name seeder originally skipped the whole step if any client already existed, so a
> database that had ever seen one client would never receive `Internal`. It now adds only what is
> missing, the same way the permission seeder does.

#### Item 20 — mat-hint removed

All 13 occurrences across 7 files are gone, and the now-dead `hint` property was removed from the
shared reason dialog. Where a hint carried something genuinely needed it moved into the label,
which costs no extra line: *"New password (at least 10 characters)"*, *"Reason (required unless
approving)"*, *"Acceptance criteria — one per line"*. Several of those hints were mine, added
earlier in this work.


### 2026-08-23 — item 3: attachments and editing before approval

274 tests green; both builds clean; verified against SQL Server.

**Attachments.** Files can now be picked while raising a request. They upload *after* the request
exists, because the endpoint is `POST /requests/{id}/attachments` and there is nothing to attach to
until then — so a failed upload reports itself without making a successful submission look failed.
Adding attachments later already worked; nothing needed doing there.

**Editing.** The server rule already existed and already refused edits once triage had acted. What
was missing was everything around it: no UI, no history, no notification.

- **New `RequestActivity` table** (migration `RequestActivityHistory`), mirroring `TaskActivity`.
  Requests had no history stream of their own, so an edit after submission left no trace anyone
  could see. Deliberately separate from `AuditLog`, which keeps the technical before/after for
  administrators — this is the readable one, and it gives item 16 somewhere to land.
- **The history says what changed, not that something did.** Fields are compared before writing,
  producing lines like *"Requester updated the title, description, what was expected, urgency and
  client."* A reviewer who already read the request needs to know which parts to re-read.
- **A no-op edit changes nothing.** If nothing actually differs, no history is written and nobody
  is notified — resubmitting an unchanged form should not wake a reviewer.
- **Reviewers are told**, because a decision made against text that quietly changed underneath is
  worse than no decision.
- The dialog uses the shared `FormSubmit`, so a rejected edit keeps every value.

Verified live: the edit is accepted and names the changed fields; the reviewer's unread count rises
and the notification deep-links to the request; a no-op leaves history and notifications untouched;
a different user is refused 403; and after approval it is refused 409 with
*"This request is now "Approved", so it can no longer be changed. Add a comment instead, or ask a
reviewer."*


### 2026-08-23 — second feedback round: items 1, 2, 8, 10

274 tests green; both builds clean; verified against SQL Server.

**Item 2 — people see their own work.** Requests were already scoped server-side. Tasks were not:
anyone signed in could list everything. `TaskQuery.VisibleToUserId` now narrows the list to work
someone is part of — theirs to do, theirs to help with, or from a request they raised — and the
controller applies it to anyone without a coordinating, reviewing, checking or reporting
permission. The menu and the routes were gated to match: a worker no longer sees Requests, a
requester no longer sees Tasks, and typing the URL is refused as well as hidden.

Verified with three tasks and two workers: the worker sees both of theirs and not the unrelated
one; the support person sees only what they help with; and supporting still keeps the task out of
their queue and out of workload.

**Item 1 — status tiles, client column, client filter.** A shared `StatusTilesComponent` above both
lists. The counts come from the server under **the same filters as the list minus status** — the
usual bug here is counting the whole table, so a tile promises 12 and the list then shows 3.
Verified: tiles summed to 31 against a list total of 31, and clicking "Waiting to be given to
someone" (11) returned exactly 11.

The tiles replaced the old status dropdown rather than sitting beside it — two controls for one
filter is the kind of duplication that makes a screen feel heavier than it is. Client is now a
column on both tables and a filter on both pages.

**Item 8 — newest first.** The Tasks list previously ordered by priority then oldest id. It is a
browsing view, with tiles and filters to narrow it, so it now orders newest-first. The working
queues — my queue, assignment, QC — keep their deliberate ordering, where priority and queue
position are the entire point.

**Item 10 — no seeded client names.** The starter names are gone; only names actually saved are
offered. `Head Office` survived deletion in the dev database because a task still references it,
which is the correct behaviour.


### 2026-08-23 — sortable headers, queue order, nav, support people, wording

274 tests green; both builds clean.

**Sortable column headings (new request).** Server-side `SortBy` / `SortDescending` on both lists,
with a shared `SortHeaderComponent`. Three details that matter:

- **Sorting happens in the database.** Ordering the twenty-five rows already fetched would reorder
  the *page*, not the list — indistinguishable from correct until the data spans two pages.
  Verified across a page boundary: page 1 ended TSK-000005, page 2 began TSK-000006.
- **Nulls last, both directions.** A task with no due date is not "the most urgent thing you have".
- **Third click clears it**, so the natural order is always one click away rather than needing a
  reset control.

> Enums sort by their **business order**, not alphabetically: priority runs Critical → High →
> Normal → Low, and status runs ReadyForAssignment → … → Closed → Cancelled. My first verification
> asserted alphabetical order and reported a failure; the code was right and the assertion was
> wrong. Alphabetical priority would read "Critical, High, Low, Normal", which is nonsense.

**Item 5 — the queue is a queue.** The top task (or anything already running) is presented as the
one to pick up; the rest are dimmed with a "Later" marker. Dimmed rather than disabled on purpose:
a worker with a paused task further down still has to be able to reach it, and the
one-active-session rule is enforced by the server regardless. The aim is to make the next task
obvious, not to lock the others away.

**Item 6 — support people at assignment.** The assign dialog now takes support people alongside the
responsible person, with one search box narrowing both lists, and the person about to own the task
excluded from the support list. They are added *after* the assignment succeeds — a rejected
assignment must not leave helpers attached to work nobody owns.

**Item 7 — breadcrumbs** showing the chain the work actually travelled (`Requests → REQ-000012`,
`Tasks → REQ-000012 → TSK-000031`), not the URL segments. Browser back retraces where *you* went;
this shows where the *work* went, so someone arriving from a notification can still walk up to the
request.

**Item 9 — collapsible sidebar**, remembered across visits, with tooltips on the icon rail and the
toggle hidden on narrow screens where the sidebar is already a drawer. The storage read is wrapped
in try/catch because some privacy modes throw rather than return nothing — a menu preference is not
worth a blank page.

**Naming.** Consistent user-facing wording: Responsible person, Support person, Smaller task,
Cannot continue, Waiting to be given out. Internal names untouched — they are the schema.

> A blanket string replace in that pass corrupted an import identifier
> (`AssignDialogComponent` → `Choose personDialogComponent`). Caught by the build, fixed
> immediately, and a reminder that renames need to be scoped to template text rather than run
> across a whole file.

---

# Round 3 — Requests, Tasks & New Request UX (49 items)

Feedback round asking for one thing in many places: **the screens expose the workflow, and the
workflow is not the user's problem.** A requester should not have to know a Task exists; a worker
should not have to read a Request to do the work; and neither should be shown twenty-two internal
statuses when six words would do.

Nothing about the workflow itself changes. `Request ≠ Task` stays, the state machine keeps every
state it has, and every rule is still enforced server-side. What changes is who is told what.

> **Status legend:** ✅ done · 🚧 in progress · ⛔ not started

## The shape of it

| # | Group | Status |
|---|---|---|
| 3–5, 47 | Audience-scoped statuses, plain wording | ✅ |
| 6 | Status cards become navigation (URL-backed) | ✅ |
| 7–14 | Contextual grids per view | ✅ |
| 15–17 | Requests grid, clickable rows, quick actions | ✅ |
| 1, 19–21 | Requester sees progress on the Request | ✅ |
| 2, 22–23 | Worker sees the request context on the Task; tabs by role | ✅ |
| 24–28 | New Request form: short, optional detail folded away | ✅ |
| 38–44 | Attachments: thumbnails, viewer, paste, drag-drop | ✅ |
| 45 | Client picker | ✅ (project/module dropped — see below) |
| 46 | Responsive | ✅ |
| 29–37 | Multi-item requests (batch) | ⛔ |
| 18 | Desktop quick-view drawer | ⛔ (explicitly optional in the brief) |

## Decisions this round

**Views live on the server, labels come with them.** `StatusViews` maps internal statuses into the
few groups each audience needs, and the API returns the tiles — key, label and count — rather than
the client hard-coding a second copy of the mapping. It has to be server-side anyway: the filter
runs in the database, and counting tiles on the client would only ever count the page you can
already see. The audience is derived from the caller's *permissions*, not their role name, so
renaming a role cannot change what anyone is shown.

**A requester's status follows the task, not the request.** A request stops moving the moment it is
approved — everything after that happens on the task it generated. So `RequestViewOf` answers with
the task's state whenever there is a task. Without this, "Approved" would sit on the screen for a
fortnight while the work was actually being done, which is exactly the confusion that made people
click into the task.

Two foldings are deliberate: **Paused reads as In Progress** (a worker going to lunch is not news,
and a status that flickers with someone's day invites "why has it stopped?"), and **failed quality
check reads as In Progress** (the work went back to the same person and is moving; "Needs Fixing"
would invite the requester to chase something already in hand). Coordinators still see both, in
full, because acting on the difference is their job.

**The grid follows the view.** A fixed column set is wrong nearly everywhere: worked time on a
queue nobody has started is a column of dashes, and the one thing a coordinator wants to know about
the unassigned pile — how long it has sat there — was not on the old grid at all. `list-views.ts`
names the columns and the primary action per view; the table renders what it is given. Everything
those columns need was already in the history tables, so no schema changed: "waiting since" is the
`StatusHistory` row that put the task where it is, "started" is the first `WorkSession`, "checked
by" is the latest `QCReview`.

**Rows are clickable, and each view has one action.** The action button stops the click so a row
never does two things at once. Anything rarer than the primary action stays on the detail screen
rather than crowding every row; permissions are still enforced server-side, as always.

**The requester is never sent to the task.** `RequestProgressDto` reads the task back onto the
request — who has it, how far along, what the quality check is doing, why it is waiting, and the
latest note anyone deliberately shared with them. Deliberately a summary, not a copy: a second,
staler task screen would be worse than the trip it saves. The "Generated task" link is now shown
only to people who act on tasks.

**The task carries the request's own words.** `RequestContextDto` puts the original description,
expected/actual result, steps and — most importantly — the screenshots on the task screen. The two
records stay separate; the *reading* does not have to be.

**Tabs by role.** A worker gets Overview, Updates, Smaller tasks, History. Dependencies and Scope
are coordination work, and "what is holding this up" now appears in the Overview for everyone, so
nothing is hidden that anyone needed. Quality check earns its tab when it is part of the task's
life or the reader is a checker — a permanently empty tab is one everyone learns to skip.

**The New Request form asks for four things.** Title, description, and three optional pickers.
Business impact, expected result, what happens instead and steps to reproduce are chips you click
to open, suggested by type — a bug gets all four, a support question gets none. Closing a chip
clears the field, because a value the requester can no longer see must not be submitted on their
behalf. Making people complete a bug-analysis form before they can report anything is how you stop
them reporting things; the workflow can already ask for more later.

**Project and Module are not on the request form.** Asked for in the brief, then dropped on the
customer's instruction mid-implementation: the client alone is enough for intake. The columns still
exist on the entity and the task screen shows them where they are set.

**Screenshots are shown, not downloaded.** Thumbnails inline, a viewer with zoom/pan/next/previous,
paste (Win+Shift+S → Ctrl+V) and drag-and-drop, and previews before submission so the wrong
screenshot is caught before it is sent. Attachments are fetched as blobs because an `<img src>`
cannot carry a bearer token — which is why `img-src` in the CSP now also allows `blob:`. Those URLs
are same-origin, unguessable and last only as long as the page.

## Verified

Driven headlessly (Playwright/Chromium) against the live API:

- [x] Coordinator, worker and requester each get their own tiles from the same data
- [x] Tiles filter, highlight, update the URL (`/tasks?view=working`), and Back walks them
- [x] Columns and the row action change per view, and no view shows a column of dashes
- [x] A requester's request row shows the responsible person and the live status of the work
- [x] Request detail leads with progress; Questions & Replies is one line when empty
- [x] Worker's task shows "What was asked for" and exactly four tabs
- [x] New request: one visible textarea, four detail chips, type-driven suggestions
- [x] Paste a screenshot → thumbnail → submit → viewable on the request without downloading
- [x] 390px wide: no horizontal overflow on any of the changed screens
- [x] 274 server tests still pass; no console errors on any screen exercised

## Still to do

**Multi-item requests (29–37).** The only part needing a schema change: a batch groups items, each
item is independently reviewable, and a reviewer may fold several approved items into one task
while traceability (`batch → item → task`) survives. Sketch: `Request` gains a nullable `BatchId`
and an ordinal, a `RequestBatch` holds the shared client/note/attachments, triage acts per item,
and `TaskCreationService` keeps its monopoly on creating work — it simply accepts more than one
request as the origin. Not started.

**Quick-view drawer (18).** The brief marks it optional and warns against duplicating detail-page
logic. Deferred until the rest has been used in anger.

