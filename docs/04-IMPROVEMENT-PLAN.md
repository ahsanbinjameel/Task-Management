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
| A1 ✅ | **Wording layer.** `StatusLabels` (server) and `core/labels.ts` (client), kept identical by hand and by comment. Statuses, roles, actions, request types, comment categories, dependency types, pause categories, QC results. | 22, and the label half of 1, 5, 7, 23, 24 |
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
| C6 ✅ | **Quick Work.** New entity + lifecycle: start in seconds, auto-pause the running task, record the interruption, resume, outcome, optional promotion to a Request, on the daily report. | 15 |

## Phase D — Experience

| # | Work | Covers |
|---|---|---|
| D1 ✅ | QC redesign: Pass / Fail / N/A per criterion, three outcomes with required explanations | 14 |
| D2 ✅ | Dashboard split: **Needs Attention** vs **Recent Activity**, both server-derived from the caller's permissions | 5 |
| D3 ✅ | Status tiles with counts on Tasks and Requests; depth cut by the home attention list and URL-backed task tabs | 7 |
| D4 ✅ | Notification matrix per role (RC4) with unread count, mark read, deep links | 9 |
| D5 ✅ | Role-scoped task detail — the client hides the panels, the server empties them, and the detail endpoint is scoped like the list | 10, 11 |
| D6 ✅ | Three records, three audiences: the readable stream, the status trail, the audit log | 16 |
| D7 ✅ | Empty states that explain and offer the next action | 23 |
| D8 ✅ | Responsive pass: card-list rows wrap, no page scrolls sideways at 390px | 2 |
| D9 ✅ | Confirmation dialogs on destructive/irreversible actions | closing note |

## Phase E — Reporting

| # | Work | Covers |
|---|---|---|
| E1 ✅ | Purpose-built PDF export (header, summary, work detail, quick work, interruptions, notes, page numbers) | 18 |
| E2 ✅ | Reports separate owned work / supported work / quick work, and count interruptions | 21 |

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


### 2026-08-24 — the rest of the plan: A1, C6, D2–D9, E1, E2, F

292 tests green (+18); both builds clean; driven end to end against SQL Server and headlessly
through the UI. Everything left in Phases A–E is done.

#### A1 — the wording layer, finished

`core/labels.ts` is the client half and mirrors `StatusLabels` on the server; both files carry a
comment saying so, because two copies of a name is exactly how "Cannot continue" becomes "Blocked"
on one screen. It covers statuses, request statuses, roles, actions, request types, urgency,
workforce states, comment categories, dependency types, pause categories, QC results and session
statuses. `humanizeEnum` survives as the *fallback* for a value with no translation, not as the
translation.

Three copies of the same PascalCase splitter were in the codebase; there is now one. The server's
labels moved to the agreed plain wording (`ReadyForAssignment` → "Waiting to be given out",
`CompletedReadyForQC` → "Waiting for quality check"), so API messages and templates say the same
thing.

> Urgency deliberately reads with the **same four words as priority**. Urgency is what the requester
> asked for and priority is what was agreed — they have to be comparable at a glance, and a value
> reading "Needed immediately" in one column and "Critical" in the next is a difference that is not
> there.

#### D2 — the dashboard says what to do, then what happened

`GET /api/dashboards/home` returns two lists. **Needs your attention** is a to-do list: every row
carries the *reason* it is there ("Came back from the quality check and needs fixing", "Needs
someone to do it", "Waiting 3 days"), written by the server so the wording cannot drift.
**Recent activity** is news, past tense, nothing to act on.

The rows a caller gets come from their permissions, not a role name, so someone who both works and
coordinates sees both kinds. A task that qualifies twice — overdue *and* needing an unblock —
appears once, under the stronger reason.

> **Found by running it, not by reading it:** a worker's own **Paused** task reached none of the
> rules, so someone with three paused tasks was told nothing was waiting on them. Paused is the
> commonest state for a worker's own work — it is where everything Quick Work interrupts lands.

#### D3 — task tabs live in the URL

`?tab=qc` opens the quality check directly, Back walks the tabs, and a link in a notification can
point at the part of the task it is about. It also removed a live bug: `tabIndex.set(2)` after
starting a QC review was only ever correct by coincidence, because the tab list is conditional.

#### D5 — the task detail is scoped where it counts

Two halves, and only one of them existed before:

- **The endpoint was open.** The task *list* was scoped to work someone is part of, but
  `GET /api/tasks/{id}` took an id and answered — anyone signed in could walk a URL through every
  task in the system. It now applies the same three clauses as the list, and answers **404, not
  403**: "you may not see this" still confirms the task exists.
- **The payload is scoped by audience.** A requester following their own work through to the task
  gets what it is and how far along it is — not the estimate, the sitting-by-sitting timings, the
  reassignment trail or what a checker wrote about a colleague's work. The client hides those
  panels *and* the server empties them, because one without the other is either a lie or a leak.

#### D6 — three records, three audiences

`TaskActivity` is the account a person reads. `StatusHistory` is the state machine's own record.
`AuditLog` is the administrator's before-and-after, and stays on its own screen behind its own
permission. The History tab used to be one list built by re-deriving sentences from the technical
rows; now the readable stream is shown as written and the technical trail is one toggle away,
offered only to people who run the process.

All three streams now carry **names** rather than user ids, resolved in one query rather than one
per row.

> **Found by running it:** the readable stream said *"Assigned to user 36."* — a user id, in the one
> stream written specifically to be read. It now says *"Moved from Wu Chen to Hunzala Waseem"*.
> The old rows keep their old wording, because the log is append-only and that is the point.

#### C6 — Quick Work

The phone call, the person at your desk, the five minutes that became forty. A new entity, not a
`WorkTask`: a task carries a lifecycle, an assignee, a quality check and a closure checklist, and
every one of those would have to be given a meaningless answer here.

Three rules keep it from being a back door:

| | |
|---|---|
| One thing at a time still holds | Starting it pauses the running task through the same close-then-open sequence the task interrupt uses, in one commit. `UX_QuickWork_OneActivePerUser` backs it at the database level — **verified by raw insert**, which SQL Server refused |
| Promotion produces a **request**, never a task | `TaskCreationService` keeps its monopoly. Verified live: promoting created a `Submitted` request and the task count did not move |
| An outcome is required to finish | A record of forty busy minutes and nothing else inflates the day and answers nothing. A mis-click is *cancelled* — kept as history, struck through on screen, and excluded from every total |

The interrupted session keeps its recorded time and is flagged `EndedByInterruption`; the task goes
to **Paused, not Blocked** (nothing is wrong with it, it simply waited); the worker stays
**Working**, because they are. `InterruptedByTaskId` is deliberately left null — it means
"displaced by *that task*", and a quick-work id in it would make every reader of the column wrong.
`QuickWork.InterruptedTaskId` is the sibling column the C3 pass predicted would be needed.

> **A permission I got wrong, found by running it.** Promotion is gated on `Request.Create`, which
> is right — it creates a request. But the Worker role did not hold it, so the feature was dead for
> the only role it was built for: promoting returned 403. The fix is the role, not the gate. Worker
> now has `Request.Create` and `Request.ViewOwn`, backfilled by the seeder on restart. A worker who
> fields a call and finds real work behind it has to be able to put it into the system, and can now
> follow what they raised without being able to browse anyone else's.

#### E2 — the day adds up

`DailyUserReportDto` gained `QuickWork`, `QuickWorkTime` and `Interruptions`. Its own line, not
folded into owned or support work, because it is neither — and because a day reading as six hours
of work in an eight-hour shift with no explanation is the complaint that produced the feature.
Interruptions are counted from the work sessions rather than from quick work alone, so the figure
covers being displaced by another task as well as by a phone call. The CSV gained three columns;
the per-item detail stays on the report, because a spreadsheet row cannot carry a variable number
of phone calls.

#### E1 — a PDF worth handing to someone

`PDFsharp-MigraDoc`, MIT-licensed, so there is no revenue condition to keep track of. Header,
summary strip, work detail, work they helped with, quick work, notes, and **"Page 2 of 7"** on
every page — a printed page with no number is a page nobody can put back. Table headers repeat when
a table breaks across a page. The renderer lives in the API layer: PDF is a transport format like
CSV or JSON, and the Application layer should not know how numbers are drawn.

`GET /api/reports/{me,team,users/{id}}/daily.pdf`.

> PDFsharp 6's cross-platform build ships **no font handling at all** and throws at render time, not
> at startup. `FileSystemFontResolver` reads TrueType files off the machine, trying Segoe UI, Arial,
> DejaVu and Liberation in order, and `EnsureAvailable()` fails at startup with the list it looked
> for. Every unknown family falls back to the resolved default rather than returning null — MigraDoc
> asks for "Courier New" for its own internal error font, and a null there turns a missing italic
> into an unhandled exception.

#### D7, D8, D9

- **D7.** Every empty state says why it is empty; the ones with a real next step offer it as a
  button. Left off where nothing the reader can do would change the answer — "nobody is on shift"
  is a fact about other people, not an invitation.
- **D8.** The tables were already wrapped in `.table-scroll` and the dialogs already sized
  themselves with `min(px, vw)`. What was left was the *card-list* rows: flex rows carrying a name,
  a chip, a link and sometimes a button, with no `flex-wrap` and in one case a hard `min-width:
  190px` that pushed the page sideways on a phone.
- **D9.** Closing a task now asks (reopening already did, and reopening is the harder of the two:
  a separate permission, a written reason, and a fresh quality check). So do deactivating an
  account — and the toggle is put back if the answer is no — removing a dependency link, and
  ending your own shift.

#### Verified

Live against SQL Server, 52 API checks and 25 UI checks, all passing:

- [x] `QuickWork` migration applied; `UX_QuickWork_OneActivePerUser` present with `([Status]=(0))`,
      `RowVersion` a real `timestamp`
- [x] Raw insert of a second active row **refused by the database**; a finished row alongside an
      active one still allowed
- [x] Timer running → quick work → task Paused, session kept with `EndedByInterruption`, 50 minutes
      intact → finish with outcome → task back to `InProgress` on a **fresh** session
- [x] Second quick work refused; blank outcome refused; somebody else's record refused 403
- [x] Promotion created a request and **no** task; a second promotion refused
- [x] Unrelated worker reading a task by id gets **404**; requester gets the task with no sessions,
      no history, no estimate, no worked total — and the coordinator gets it whole
- [x] All three history streams name people; a new assignment reads "Moved from X to Y"
- [x] Daily report, CSV columns, and three PDFs (person, team, empty day) — page numbering checked
      across a 5-page document by extracting the text
- [x] UI: both dashboard lists, quick-work dialog, URL-backed tabs with Back, the history toggle
      shown to a coordinator and hidden from a worker, empty states, and **no horizontal scroll at
      390px** on four screens — with no console errors anywhere

**Still open at the time of writing:** Round 3's multi-item requests (29–37) and the quick-view drawer (18). Both landed the same day — see the entry at the end.

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
| 29–37 | Multi-item requests (batch) | ✅ |
| 18 | Desktop quick-view drawer | ✅ (read-only by design — see below) |

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

## 2026-08-24 — the last two: multi-item requests and the quick-view drawer

307 tests green (+15); both builds clean; 28 API checks and 26 UI checks against SQL Server, all
passing. **Every item in this plan is now done.**

### Multi-item requests (29–37)

`RequestBatch` holds what several requests share — a title, a note, a client and the files. Each
item is a full `Request` with its own number, its own status and its own triage decision.

**The batch carries no status of its own,** and that is the design rather than an omission. A batch
is a wrapper, not a unit of work: a reviewer can approve three items, reject one and ask a question
about the rest, and any status on the wrapper would have to be either a lie or a summary — and a
summary is something a screen can compute. So everything that already worked keeps working on a
batch item without knowing batches exist: the review queue, clarifications, editing before
approval, notifications, the requester's progress view.

**Folding needed no new schema.** `Request.GeneratedTaskId` already answered "which task did my
request become", and nothing stopped several requests answering with the same task;
`WorkTask.RequestId` answers the other direction with the item the task was raised from. A join
table would have been a second place to keep one fact — and because the fold rides on the column
every existing read path already uses, the *second* item reports the shared task's progress to its
requester with no extra code at all.

| Decision | |
|---|---|
| The client is **copied** onto each item, not read through the batch | An item corrected at triage must not drag its siblings with it — which is exactly what happens when eight month-end problems turn out to belong to two clients |
| The **highest** urgency across folded items wins | Taking the first, or averaging, would let a critical item be quietly downgraded by being submitted next to a trivial one |
| **One audit row per item**, not one for the fold | An administrator asking "who approved REQ-000031" must find an answer against that request, not a batch operation they then have to unpick |
| **One notification** for the batch, not one per item | Eight bells for one submission is how people learn to ignore the bell |
| Every item's words go into the folded task's **description** | A worker handed three folded requests will otherwise finish the first and call it done |
| Folding is limited to **one batch** | The items arrived together and were judged together; combining unrelated requests would make the task's provenance unreadable, and no screen would show it |

Nothing about the workflow changed. A batch cannot become a task; its items become tasks, and only
through triage approval — `TaskCreationService` keeps its monopoly, and folding calls it once.

The form asks for one thing until told otherwise: "Ask for something else too" turns the single
request into the first item of a list. The optional detail fields belong to that first item and are
folded into its description under their own headings on submit, so nothing typed is discarded and
nothing is sent that the requester cannot see.

> **A duplicate column, caught by reading the migration.** EF could not tell that
> `Attachment.BatchId` backed `RequestBatch.Attachments` and silently added a second
> `RequestBatchId` beside it — two columns holding one fact, in a codebase whose comments are
> largely about avoiding exactly that. Naming the foreign key explicitly fixed it; the migration
> was removed and regenerated rather than patched.

### Quick-view drawer (18)

The brief asked for it and warned in the same breath against duplicating the detail page. That
warning is the design: the drawer is **read-only and deliberately incomplete**. It answers "is this
the one I am looking for?" and nothing else — no tabs, no comments, no timer, no quality check, no
actions. Each of those would be a second implementation of a screen that already exists, and would
drift from it within a month.

It holds no logic of its own either. It calls the same endpoint the full page calls and renders a
handful of fields; everything beyond that is one click away on the page itself. Desktop only —
below 1100px there is no room for a panel beside a list, and the full page is the better answer.

> **The trigger was in the wrong cell.** It first went into the table's `action` column, which only
> exists for views that define a primary action — so it vanished on every view that does not. It
> now has a column of its own, appended by the table rather than named in `list-views`, which also
> keeps it out of the per-view column lists it has no business being in.

### Verified

- [x] Three items, three request numbers, one batch number from its own `BAT-` sequence
- [x] Rejecting one item leaves its siblings untouched; the review queue counts what still waits
      and the batch drops out once every item is decided
- [x] Two items folded → **one** task; both approved, both pointing at it, each naming the other
- [x] `batch → item → task` reads from both ends; the task shows the batch and the folded request
- [x] Highest urgency won (Critical over Normal); both requests are in the task description
- [x] The folded-in item still reports the shared task's progress to its requester
- [x] Refusals: already-decided item, item from another batch, unanswered question, missing
      `Task.Approve`, unrelated worker reading the batch (404), a batch of blanks
- [x] UI: the form grows from one request to three, the reviewer's checkboxes and fold dialog, the
      drawer opening without navigating and carrying no tabs, hidden at 390px, no console errors

> Two checks in the *earlier* pass's UI harness failed on this run. Both were the harness's own
> doing: its assignment-naming check reassigns a task as a side effect, so the worker it later
> asserted about no longer had one. The paused-task rule was re-verified directly against the API
> instead. A verification script that mutates the data it later reads is a bad harness, not a
> regression — worth remembering next time one is written.


---

## 2026-08-24 — proof of work and quality-check evidence

The pipeline could carry a screenshot **in** and nothing back **out**. A requester attached the
picture of the broken invoice; the worker who fixed it, the checker who verified it and the
coordinator who closed it all had one undifferentiated list of "files on this work" to put anything
in — so the question a closure decision actually turns on, *show me the evidence this was done*,
could not be asked of the data at all.

### The shape of it

`AttachmentKind` says what a file is **for**, as distinct from what it hangs off:

| Kind | Who supplies it | Where it is read |
|---|---|---|
| `General` | anyone who may see the record | the request's context, the task's own file list |
| `CompletionProof` | **only** the task's primary assignee | Overview, above the work sessions — the first thing a checker opens |
| `QCEvidence` | anyone holding `Task.QCReview` | inside the numbered attempt it justified, never loose on the task |

### Decisions

| Decision | |
|---|---|
| A **kind**, not a fourth owner column | The file really does belong to the task, and one task holds all three kinds at once. Another owner column would have said the wrong thing twice |
| Authorised in the **service**, not by an attribute on the controller | "Is this the person responsible for this work" depends on the task, not on the caller. A coordinator holding every permission there is still cannot supply the proof |
| Evidence is **staged, then adopted** | The attempt does not exist while the checker is still typing, so evidence uploads unclaimed and the verdict ties it in. Scoped to the uploader: two checkers on one task must not have their pictures swept onto each other's verdict |
| A **refused verdict leaves it staged** | Otherwise a validation failure costs the checker their screenshots and a second trip through the snipping tool |
| Evidence lives with the **attempt**, not the task | Attempts are append-only. The pictures that justified a failure have to stay with the failure once a later attempt passes — so QC evidence is returned inside its `QCReviewDto` and deliberately left out of both loose lists |
| The completion nudge is **said, not enforced** | Work whose result is not a screenshot is ordinary. Refusing to accept it without a file would only teach people to attach anything |

`app-attachment-upload` is the client half: the same choose / drag / **paste** the request form's
drop zone offers, but posting straight onto a record that already exists rather than staging for a
form that has not been submitted. Nothing to remember to press afterwards.

### Verified

- [x] 11 new application tests (`AttachmentProofTests`); suite at 318
- [x] `AttachmentProof` migration applied on startup against SQL Server
- [x] Live over HTTP: checker refused `CompletionProof` (403 `attachment.not_assignee`), worker
      refused `QCEvidence` (403 `attachment.not_checker`), assignee's proof accepted
- [x] A fail-then-pass cycle: attempt 1 keeps `attempt-1.png`, attempt 2 keeps `attempt-2.png`,
      neither leaks into the task's own file lists, and the request's screenshots stay on the request
- [x] Evidence downloads back byte-for-byte through the authorised endpoint
- [x] `ng build --configuration production` clean


---

# 2026-08-26 — Verification, and the administrator who was a worker

Two changes, related only in that both come from the same mistake: assuming that because two things
often go together, one implies the other.

## Part 1 — Verification

### The gap

A reviewer opens *"Employee Salary form is not calculating tax correctly."* They cannot tell from
that whether it is a software defect, a configuration mistake, a data problem, a permission problem,
a misunderstanding, or expected behaviour somebody dislikes. Triage offered six outcomes and not one
of them said *"find out"*:

| What they could do | What it costs |
|---|---|
| **Approve** it into a task | Commits the organisation to building something before anyone has established there is anything to build. And it is irreversible — an unwanted task has to be cancelled on its own page |
| **Ask for clarification** | Bounces it back to a requester who has already told you everything they know. The information needed is in the system, not in their head |
| **Reject** it | Guessing, with the requester bearing the cost of a wrong guess |
| Fake it as a task, complete it, send it to QC | Four states, an assignee, acceptance criteria and a closure checklist, every one given a meaningless answer, to record twenty minutes of looking |

The fourth is what people actually did, which is how you end up unable to distinguish *work that was
done* from *looking that was done* in any report.

### The shape of it

`Verification` — assigned investigation. A reference number, a target, a checker, instructions, a
result, findings, evidence, and its own history stream.

```
Request → Review → Send for checking → Verification → findings → Review → (approve or not)
                                                                              ↓
                                                                      TaskCreationService
```

Three concepts that look alike and are kept apart:

| | Answers | Needs a task? | Who starts it |
|---|---|---|---|
| `QCReview` | "Does this finished work meet its acceptance criteria?" | yes, a completed one | the process, after Complete |
| `Verification` | "Is there really a problem here?" | **no** — usually there is none | a reviewer, deliberately assigning it |
| `QuickWork` | "What was I doing for those forty minutes?" | no | the person doing it, for themselves |

### Decisions

| Decision | |
|---|---|
| A **new aggregate**, not a polymorphic `QCReview` | QC owns the transitions into `QCPassed`/`QCFailedRework`, numbered attempts, criteria evaluation and segregation of duties. Every one of those would have needed a null case meaning "not applicable" — and the null case would have been the common one |
| **A result never creates work** | `IssueConfirmed` returns the request to `InReview` with the findings attached and stops. This is the load-bearing decision: an automatic task on a confirmed issue would have made the check *be* the approval, and `TaskCreationService`'s monopoly is what makes "a request never auto-becomes a task" auditable rather than aspirational |
| **Every** result hands the request back the same way | Five outcomes with five consequences would be five rules to remember and five places for a request to get stuck. The reviewer already has all seven triage outcomes in front of them |
| No decision while a check is open | Applied to every decisive outcome, not only approval. A checker who submits findings against a request rejected underneath them has done the work for nothing. Asking for a clarification is exempt — a question is not a decision |
| Real FKs where a real row exists | `RequestId` and `ModuleId` are constrained; a form, screen or build is described in `TargetName`/`TargetReference`. One untyped `SourceId` read through `TargetType` would be unjoinable, unconstrained, and silently orphaned on the first delete |
| **Three** permissions, not four | `Verification.Create` covers raise/assign/re-route/cancel. A check with no checker is inert, so naming one is part of raising it — a separate `Verification.Assign` would mean holding two permissions to do the one thing the feature exists for |
| Start / report / attach decided **on the record** | `AssignedToUserId == caller`. A reviewer holding every permission there is cannot file findings under the checker's name. Same shape, same reasoning, as `CompletionProof` |
| A requester is told **"Being Checked"** | `UnderVerification` folds into their existing `checking` view — the same words a task in QC gets. To the person who asked, "establishing whether this is broken" and "checking the fix" are the same news. Reviewers get their own `verifying` tile, because those are two different queues with two different people to chase |

## Part 2 — Administrator was quietly a worker

`DefaultRoles.Map[Administrator]` was `Permissions.All`, which included `Workforce.TrackShift` and
`Task.Work`. So every administrator got a shift widget in the toolbar, appeared in
who-is-working-now, and turned up in the assignable list for real work — none of which follows from
administering the system.

`DefaultRoles.AdministratorGrants` is now everything except those two. `Administrator = Worker` is a
configuration decision: an administrator who also does the work gets the Worker role too.

Note the seeder is **additive** by design — a restart must never silently revoke a permission a site
chose to add — so this changes what a *new* database grants. An existing one keeps what it has until
someone removes it in the role editor. Said in the runbook's first-run checklist.

`RoleAndShiftSeparationTests` pins the three independent permissions, including the one nobody was
testing: signing in does not start a shift.

## Verified

- [x] 37 new application tests (`VerificationTests`, `RoleAndShiftSeparationTests`); suite at
      **393** (29 domain + 364 application), all green
- [x] `dotnet build` clean across the solution; `npm run build` clean on the Angular client
- [x] `Verifications` migration generated and applied to `WorkflowApp_Dev`
- [x] `UserAdminServiceTests.List_roles_reports_the_seeded_permission_grants` updated — it asserted
      the old assumption, exempting Administrator from the "nobody else is shift-tracked" rule
- [x] `scripts/sql/reset-dev-data.sql` extended, child-first, between the tasks and the requests
- [x] **Verification driven end to end over HTTP against SQL Server** (36 checks, all passing):
      request → send for checking → the requester reads "Being Checked" while the reviewer reads
      "Being verified" → approve and reject both refused with `request.verification_pending` →
      only the assigned checker may start it or attach evidence (a reviewer gets 403 on both) →
      findings recorded → request back in `InReview` with **no task** → approval is what creates
      the task. Plus an independent check with no request behind it, a checker who cannot be
      assigned one (`verification.checker_cannot_work`), 404-not-403 scoping, and the original
      request→approval→task pipeline still running unchanged
- [x] One thing the HTTP run corrected: whitespace-only findings are caught by `[Required]` on the
      DTO **before** the controller runs, so they come back as a 400 field error rather than the
      service's `verification.findings_required`. That is the better shape — the client renders it
      under the textarea — and the service check still guards the contract for non-HTTP callers

### Follow-up, same day — the two things the first cut got wrong

Both found by actually using the screen rather than by reading the code.

**A check with no checker was a dead end.** `PUT /{id}/assignee` and `assignableCheckers` were
built and then never called from anywhere: nothing in the UI could give a check to somebody. A
verification raised without an assignee sat at "Waiting for a checker · Nobody yet" with no action
on the page but *Call it off* — and the "needs a checker" notification, addressed to exactly the
people holding `Verification.Work`, led them to that same dead page.

Fixed with two paths, because they are two different acts:

| | Who | Permission | When |
|---|---|---|---|
| `POST /{id}/claim` | a checker taking it | `Verification.Work` | only while nobody holds it |
| `PUT /{id}/assignee` | a coordinator giving it out | `Verification.Create` | any time, and asks why if somebody already has it |

Both are reachable from the list (a **Take it** / **Assign** column) and the detail. The detail also
now says out loud that an unassigned check is not moving, rather than leaving it to be inferred from
"Nobody yet" in a field.

**The list rendered as one run-on line of text.** `<table class="grid">` — but `.grid` in
`styles.scss` is a CSS-Grid utility (`display: grid`), so it flattened the table. Every other grid
in the app is a `mat-table`, and the table styling in `styles.scss` is written against
`.mat-mdc-header-cell` / `.mat-mdc-row`, which a plain `<table>` never matches. Rewritten as a
`mat-table`.

Three smaller things went with them: the evidence panel printed its empty message twice (its own
paragraph plus `app-attachments`' built-in one), status chips had no tone so everything was grey,
and *Call it off* sent a canned reason instead of asking for one — the server requires a reason, so
every call-off was recorded as "Called off from the check screen."

- [x] 4 more tests (claim, claim-refused-when-held, idempotent claim, claim-without-permission);
      suite at **397**
- [x] `npm run build` clean
