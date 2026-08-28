# PRODUCT-CORE.md — What WorkflowApp *is*, and what to do next

**Read `CLAUDE.md` first for where code lives. Read this for what the product is and what to build next.**

`CLAUDE.md` maps the *codebase*. This file governs the *product* — the spine we commit to, what
gets exposed to users, what gets parked, and the exact order of work. When the two disagree:

- About **where code lives / how something is implemented** → `CLAUDE.md` wins.
- About **what a user sees or which feature is exposed** → this file wins.

A **feature freeze is in effect.** No new workflow, entity, status, screen or field enters the
*visible* product unless it directly unblocks the pilot defined in §12. Backend code already written
is not deleted — it is hidden from the user journey (§9). This is a *product* recovery, not a code
rewrite. The architecture is sound; the surface area is not.

> **The one rule that overrides all cleverness:** a workflow concept enters the visible product only
> *after* a real operational problem proves it deserves to exist — never because a scenario is
> imaginable. Claude can generate plausible features indefinitely; that is exactly why the brake now
> has to be product discipline, not implementation cost.

---

## 1. The single purpose of this software

> **Remove Ahsan as the human synchronization layer between requesters, workers and QC.**

Every screen, field and status is judged against that sentence. If a feature does not measurably
reduce Ahsan's coordination burden, it is not core — however elegant the code behind it is.

Today Ahsan personally is: the request intake system, the triage engine, the task writer, the
assignment manager, the progress-relay between everyone, the sheet-maintainer, and sometimes QC.
WhatsApp is doing intake + attachments + notifications + progress updates. Excel is doing the
database + queue + status tracker. **That** is the problem the software removes — not "build a
comprehensive project-management platform."

---

## 2. The three burdens (the lens for every decision)

Every activity Ahsan does is one of three kinds. Software treats each differently:

| Burden | Example today | What software does | Can it be removed? |
|---|---|---|---|
| **Translation** | WhatsApp + screenshot → a structured task | Make capture fast enough that little rewriting is needed | Mostly |
| **Judgment** | Is this valid? Which form? Who does it? Priority? | Give context + evidence so the decision is *faster* | **No — this is legitimate work** |
| **Relay** | Requester → Ahsan → worker → Ahsan → requester | Make progress directly visible; let the requester close their own loop | Yes, for the *routine* case |

**Do not try to automate judgment away.** The target for relay is not zero — exceptions, blockers
and management questions will always need a human. The measurable target is:

> **Routine progress questions require zero human relay.**

This lens explains the build order in §9: relay needs the *fewest* people to change habits, so it
ships first.

---

## 3. The frozen spine

This is the whole product. Everything else supports this or is parked.

```
                              INTAKE
                    ┌──────── REQUEST ────────┐
                    │   single, or several     │
                    │   points for ONE client  │   ← batch is intake convenience, not a workflow
                    └───────────┬──────────────┘
                                ▼
                              TRIAGE                "What actually needs doing?"
                   ┌────────────┼────────────┐
                   ▼            ▼             ▼
              Clarification   Check        Reject
                   │            │
                   └────► back to Triage ◄─┘
                                │
                             Approve                ← the ONLY thing that creates a task
                                ▼
                         COMMITMENT: TASK            ← scope is now stable (see §6)
                                ▼
                            ASSIGNMENT               "Who owns this?"
                                ▼
                            EXECUTION                Start / Pause / Finish
                                ▼
                               QC
                        Fail ◄──────► Pass
                          │             │
                    back to Execution   ▼
                                 READY FOR CONFIRMATION   ← client work only (see §7)
                                        ▼
                                   ACCEPTANCE            requester: "Fixed" / "Still not fixed"
                                   ┌────┴────┐
                                Closed     Rework
```

Orthogonal to the spine, never its centre:

```
ERP CONTEXT          Client? ─────────────┐
                                          ├── on the Request / Task
                     Module → Form → Surface┘

SUPPORTING           Users & permissions · Attachments · Shift · Notifications · Audit · Reports
```

Batch = intake convenience. Check (Verification) = a triage branch. Shift, notifications, audit,
reports = infrastructure/derived output. **None of them is the conceptual centre.**

---

## 4. Core invariants — add these to `CLAUDE.md §5`

Keep every existing invariant in `CLAUDE.md §5` (a request never auto-becomes a task; approval is
the explicit gate; batches carry no status of their own; a verification never creates work; QC/
closure own their transitions; history is append-only; DB is source of truth). Add two:

13. **Once execution starts, a task's committed scope does not silently grow.** New points that
    arrive after work has begun become *new linked requests* (§6), never an invisible extension of
    the running task.
14. **Requester acceptance is a per-work-type *closure policy*, not a universal database
    invariant** (§7). Client-facing work closes on requester acceptance; internal work may close on
    QC pass. Do not hard-code acceptance into the state machine for every task.

---

## 5. ERP context is two orthogonal axes — not a tree

This is the single most important modelling correction. Do **not** model Client → Module → Form →
Surface as one hierarchy. Forms do **not** hang off clients.

Your *product* has modules and forms. Each *client* runs an instance of that product. So:

```
CLIENT AXIS (nullable)          PRODUCT-CATALOG AXIS (client-independent)

Impression Sourcing             Sales
ABC Ltd                          └─ Delivery Order
XYZ Pvt Ltd                          ├─ Form
Internal / (null)                    ├─ History
                                     ├─ Detail Report
                                     └─ Master Report
```

A request ties one point on each axis together:

```
Request  =  Client?  ×  ProductLocation(Module → Form → Surface)
```

Why this matters (and why a per-client tree destroys it):

- **Cross-client insight becomes possible.** "Show every *Delivery Order · Detail Report* issue
  across all clients." / "Which forms generate the most support?" / "Is this Invoice-Posting bug
  unique to ABC, or are four clients reporting it?" A per-client tree makes those queries ugly or
  impossible because each client's forms are independent copies.
- **The nullable-client requirement stops being a special case.** Internal work is just the Client
  axis left empty against a real product node:
  ```
  Client: (null)   Module: Accounts   Form: Accounts Posting   Surface: Form
  ```
  **Do not invent a fake "Internal Client"** to satisfy a foreign key. Client is nullable, full stop
  (the stated ~5% internal-work tolerance; ~95% will have a client).

### Instructions for Claude Code

1. **Investigate first.** Read `Entities/Requests/Organization.cs` and the migrations. Determine
   whether `Module` currently carries a `ClientId` FK or is otherwise tied to a client, and whether
   `Project` couples them. Report findings before changing schema.
2. **Model the catalog client-independently.** `Module` (exists) → add `Form` (belongs to Module) →
   add `FormSurface` / `FormType` (belongs to Form, e.g. *Form*, *History*, *Detail Report*,
   *Master Report*). These are product-catalog reference entities administered under Setup, **retired
   never deleted** (follow the existing `SetupService` pattern and `CLAUDE.md`'s retire-not-delete
   rule). None of them references `Client`.
3. **On `Request`/`WorkTask`:** `ClientId` stays nullable; add nullable `ModuleId`, `FormId`,
   `FormSurfaceId` (whichever grain the requester/reviewer supplies). Reuse the existing
   `LookupsController`/`LookupService` type-ahead pattern already used for the verification target.
4. **Keep the catalog modest.** Module / Form / Surface is enough for now. **Do not** model fields,
   controls, report-columns, builds, versions or schema dependencies this week just because they are
   theoretically useful — that is exactly the trap §0's rule forbids.
5. Update `CLAUDE.md` §4 file index and §5/§6 in the same change.

---

## 6. Scope discipline — the "Faisal" rule

The pasted conflict (Delivery Order *Detail* report points on day 1, then *Master* report points on
day 2, blowing the timeline) is the acid test. The software answer is **not** to punish requesters
for finding things late, and **not** to silently absorb the new points. It is to make later rounds
*cheap and traceable* while keeping the finish line honest.

There are two distinct cases — do not conflate them:

**Case A — genuine change to already-committed work.** Worker started "add Customer Balance column",
then the requester says "actually add Aging, Credit Limit and Last Payment too." *That* is a real
scope change. Keep the concept (`ScopeChangeService` already exists) but **park the bureaucratic
approval ceremony** (§9). Record it; don't build a multi-step approval dialog around it yet.

**Case B — a new point discovered in a later round (the Faisal case).** This is **not** a scope
change to the first task. It is a *new request* arriving in a later round against related ERP
context:

```
REQ-00141 — Delivery Order · Detail Report total     (Round 1)  ← already in progress
REQ-00159 — Delivery Order · Master Report issue      (Round 2)  ← new, linked, its own number
```

They share `Client · Module · Form` and can show they came from related testing, **but REQ-00159
does not move REQ-00141's finish line.** The invariant (§4.13) makes the cost of finding-later
*visible* instead of hidden. That protects the team's timeline without blaming anyone.

### Instructions for Claude Code

- Add a lightweight "raise a related/follow-up request" affordance from an existing request or task
  that pre-fills the shared `Client · Module · Form` and marks it as a later round (an ordinal or a
  simple `RelatedToRequestId` link — reuse `Related` dependency semantics if convenient; **do not**
  build a new heavyweight linkage aggregate).
- Under no circumstances append later points into a task that is already `InProgress`. Enforce §4.13
  where scope would otherwise mutate.
- Leave `ScopeChangeService` in place but keep its ceremony out of the default journey.

---

## 7. Closure policy — internal correctness ≠ client acceptance

`QC Pass → Closed` (auto) is fine for a generic tool but wrong for an ERP shop. "Coded and passed
internal QC" is genuinely not "done" for the requester — done is *it reached their instance and they
confirmed it's fixed*. Distinguish three things conceptually:

```
Internal correctness    ≠    Client delivery    ≠    Client acceptance
```

But **do not build five new states.** Resist the creep. Implement the *minimum* that removes the
relay, as a **policy**, not a universal invariant:

- **Client-facing work:** after QC pass the requester sees **Ready for Confirmation** with two
  buttons — **"It's fixed"** (→ Closed) and **"Still not fixed"** (→ rework, carrying their comment
  and optional screenshot). This kills the last relay hop: today it's `Faisal → "haan hogya" →
  Ahsan reads → Ahsan updates sheet`. The requester now closes their own loop.
- **Internal work (no requester to accept):** QC pass is sufficient for closure. Collapse the
  closure-checklist ceremony to a single click (or auto-close). Do **not** force a requester-
  acceptance step where there is no requester.

### Instructions for Claude Code

1. **Prefer reusing existing transitions over inventing states.** Read `Workflow/TaskWorkflow.cs`,
   `Common/StatusViews.cs`, `Tasks/Services/ClosureService.cs` and `TaskWorkflowService.cs` before
   any change. Report the minimal viable change.
2. Represent "Ready for Confirmation" to the requester through the **`StatusViews` audience mapping
   + wording layer** wherever possible, rather than adding raw machine states. If one new state is
   genuinely unavoidable for the acceptance gate, add exactly one — not a delivery/deploy/accept
   chain.
3. Give the requester two scoped actions on *their own* request's task: **Accept** (routes through
   `ClosureService` → Closed) and **Reject** (routes through the existing reopen/rework path with a
   mandatory comment). Gate them so a requester can only act on their own request. Reuse
   confirmation/reason dialogs per `CLAUDE.md`'s confirmation rules.
4. Make acceptance a **closure policy** keyed off "does this work have a requester/client?" — not a
   hard invariant on every task (§4.14). Internal work bypasses it.
5. Requester-facing statuses collapse to a tiny, plain-language set (see §8/§11 wording): e.g.
   `Submitted · Being reviewed · Being checked · In progress · Ready for confirmation · Completed`.
   Coordinators keep the richer view; the 22-state machine is untouched underneath.

---

## 8. The Request page — one client, many points, fast (Ahsan's explicit requirement)

This is the highest-value *intake* screen and must feel effortless. The requirement:

> **One client per request/session. Then add multiple points rapidly. Submit once.**

This is exactly the existing batch (`RequestBatch` + `RequestBatchService`, with
`BatchId`/`OrdinalInBatch` already on `Request`). We are not building a new concept — we are giving
the batch a **fast, non-overwhelming UI**.

### Target UX (spec)

```
┌────────────────────────────────────────────────────────────┐
│  New Request                                                 │
│                                                              │
│  Client   [ Impression Sourcing ▾ ]        ← pick ONCE, shared │
│  Module   [ Sales ▾ ]  (optional)          ← optional shared  │
│  Form     [ Delivery Order ▾ ]  (optional) ← optional shared  │
│  ──────────────────────────────────────────────────────────  │
│  Point 1                                                     │
│  [ Detail report total isn't correct           ]  [📎 paste] │
│                                                              │
│  Point 2                                                     │
│  [ Master report column XYZ not showing         ]  [📎 paste] │
│                                                              │
│  + Add point                                                 │
│                                                              │
│                                          [ Submit all ]      │
└────────────────────────────────────────────────────────────┘
```

Rules:

- **Client is chosen once** and copied onto each item (the client is *copied* per item per
  `CLAUDE.md`'s existing rule, so correcting one item at triage never drags its siblings).
- **Module/Form are optional shared defaults** the requester *may* set; each point can override or
  leave blank. Surface/FormType and finer product location are primarily a **triage** concern
  (§10) — do not force four mandatory dropdowns on the requester and destroy the intake speed.
- A point needs only **text** (+ optional screenshot/paste). Everything else is optional.
- **Paste-to-attach** must work per point (Win+Shift+S → Ctrl+V), reusing `app-file-drop` /
  `file-drop.component.ts`.
- **One submit** creates one batch of N independent requests. A single point is just N=1 — the same
  form, no separate "single vs batch" mode for the user to choose between.
- Target: a submittable request in **~15–20 seconds**.

### Instructions for Claude Code

- Read `Requests/Services/RequestBatchService.cs`, `RequestBatchesController.cs`,
  `Requests/Dtos/RequestBatchDtos.cs`, and the current `features/requests` intake components +
  `shared/search-select.component.ts` + `shared/file-drop.component.ts`.
- Build the repeater UI above as the **single** New Request entry point. Collapse any separate
  batch-vs-single distinction from the user's view.
- Keep the optional-detail chips already described in `CLAUDE.md` (business impact, expected result,
  etc.) but keep them out of the way — a point must be submittable with text alone.
- Do not add Project/Surface to intake. Client + optional Module/Form is the ceiling for the
  requester.

---

## 9. Build order — sequenced by *whose habits must change*, not by feature

The previous plan put requester self-intake first; that is the **hardest** adoption (five people
changing behaviour) for a loop that needs it least. Reverse it. The relay loop needs the *fewest*
people to change habits (Ahsan + two workers already update state today), so it proves value first.
During migration **Ahsan keeps entering requests on requesters' behalf** — that is a transition
strategy, not the end state. Once requesters *see* a page that answers "kya bana?" without pinging
Ahsan, self-service sells itself: "the same page you already use to see progress is where you submit
the next point."

| # | Experience | Ships value by… | Success condition |
|---|---|---|---|
| **1** | Worker task execution (My Tasks) | Making the running work obvious | A worker can understand, start, pause and finish a task **without asking Ahsan how** |
| **2** | Request progress visibility (+ requester acceptance) | Killing relay | A requester answers "kya bana?" **without contacting Ahsan**, and can Accept / reject a fix themselves |
| **3** | Assignment | Speeding judgment | Ahsan picks a worker from **real workload/context**, not memory |
| **4** | Triage (+ ERP catalog at triage) | Speeding translation→commitment | Ahsan turns messy WhatsApp input into committed, structured work **quickly** |
| **5** | Request intake (fast multi-point, §8) | Eventually offloading translation | A requester can submit **faster than messaging Ahsan** |

**New Request is deliberately last to be *adopted*** even though it's built early enough to dogfood.
Ship 1 and 2 before anything else — they capture most of the value while only three people change
habits.

---

## 10. KEEP / HIDE / BUILD / REFINE — the capability map

**Hide ≠ delete.** Keep routes, permissions and backend intact; remove from the default nav and the
normal user journey (§11).

| Capability | Decision | Note |
|---|---|---|
| Requests (intake) | **REFINE** | §8 — the fast one-client multi-point form is the single entry point |
| Batch | **REFINE** | It *is* the multi-point form; stop exposing it as a separate mode |
| Attachments / paste screenshots | **KEEP (core)** | Per point, per task, per QC evidence |
| Triage | **REFINE** | Add Module/Form/Surface here; present outcomes simply |
| Clarification | **KEEP (core)** | A question, not a decision |
| Check / Verification | **KEEP (secondary)** | Rename to **"Check"** in the UI; surface mainly as a triage branch |
| Assignment | **REFINE (invest)** | §12 — facts, not fake capacity |
| Worker queue / My Tasks | **REFINE (invest)** | §12 |
| Start / Pause / Complete | **KEEP (core)** | The timer |
| QC pass / fail | **KEEP (core)** | |
| **Requester acceptance** | **BUILD (minimal)** | §7 — Fixed / Still-not-fixed |
| **Client × Module/Form/Surface** | **BUILD / CHANGE** | §5 — orthogonal axes |
| Client (nullable) | **KEEP** | ~95% set, 5% internal; no fake internal client |
| Shift / workforce | **KEEP (secondary)** | Feeds Assignment; small widget for workers only |
| Workload | **KEEP / improve** | Feeds the Assignment screen |
| Notifications | **KEEP (quiet)** | Pointer, not a copy |
| Audit | **KEEP (quiet)** | Read-only trail |
| Home / Dashboard | **KEEP (minimal)** | "What must I do" + "what happened", scoped by permission |
| Reports | **KEEP (basic)** | |
| PDF export | **KEEP, not a priority** | Not an adoption lever |
| Quick Work | **HIDE / park** | |
| Dependencies | **HIDE / park** | |
| Subtasks | **HIDE / park** | |
| Scope-change *ceremony* | **HIDE / park** | Keep the *discipline* via §6 linked-round requests |
| Complex reopening | **HIDE** | Keep a simple rework path |
| Closure checklist | **SIMPLIFY** | Policy-based (§7); one click / auto for internal |

---

## 11. Navigation per role — the "don't overwhelm" rule

The sidebar must represent **the user's job, not the database architecture.** Routes and permissions
stay; the *nav* shrinks to almost nothing per role. This is the fastest, cheapest win for "the
experience shouldn't feel overwhelming" — do it first (§13).

```
Requester        Worker              Reviewer/Coordinator (Ahsan)    QC
─────────        ──────              ────────────────────────────    ──────
Home             Home                Home                            Home
Requests         My Tasks            Requests   (Review is a view    Quality  (QC queue
  + New            (+ shift widget)  Tasks       inside Requests)      + Checks)
                                     Team        (Assignment is a
                                                 view of Tasks)
```

- **Admin/Setup** lives behind **Settings** (the one door out of the profile menu), not in the main
  rail.
- "Review Queue", "Assignment Queue", "QC Queue", "Workload", "Who's Working" are **views inside**
  Requests/Tasks/Team/Quality — not top-level destinations.
- Ahsan wears reviewer + coordinator + sometimes-QC hats, so his rail is the fullest — but it is
  still four job-shaped items, not thirteen database-shaped ones.

### Instructions for Claude Code

- Edit the permission-filtered nav in `src/app/layout/` so each role sees only its job-shaped items
  above. Do not remove routes or guards — a user who deep-links to a parked route still lands
  (permitted) or 404s (not), unchanged.
- Fold Review into Requests and Assignment into Tasks as **tabs/views**, not separate nav entries.
- Apply the wording layer (`core/labels.ts` ↔ `StatusLabels.cs`, change both together) so
  requester-facing screens use the plain-language status set (§7).

---

## 12. The four screens to make *obvious* — and nothing else

Do not move on from one until it is boring and self-evident. Use **real** company scenarios to test
each (§13), not invented data.

### A. My Tasks (worker) — *build first*
- Read `Tasks/Services/TaskQueryService.cs` (`my-queue`), `WorkSessionService.cs`, `features/tasks`.
- Worker sees at most: **To Do · In Progress · Paused/Blocked · QC · Done** (via `StatusViews`).
- A task row must answer, without asking Ahsan: *what is this, in what ERP context, what's the
  expected result, what's attached.* One-click **Start** from the queue (`?start=1` already exists).
- Success: a worker starts and finishes without a "how do I…" interruption.

### B. Request progress + acceptance (requester) — *build second*
- Read `RequestProgressDto` (reads the task back onto the request). Requester detail shows: current
  plain-language status, who's responsible, when submitted/started, latest shared note — and, at the
  end, the **Fixed / Still-not-fixed** buttons (§7).
- Success: zero "kya bana?" messages needed; requester closes their own loop.

### C. Assignment (coordinator) — *build third; invest depth here*
- Read `TaskAssignmentService.cs`, `TaskQueryService.cs` workload/assignable-users. Show **facts,
  not fictional capacity**:
  ```
  ● Hanzala Waseem   Working now · current TSK-00124 (1h 12m) · 1 active · 3 waiting
                     Recent related: Delivery Order report · Sales invoice print
  ● Uzair            2 active · 5 waiting · 1 due today
  ○ Umer             Not on shift · 2 waiting
  ```
- Do **not** compute a made-up "capacity" number. The assigner decides from facts.

### D. Triage (reviewer) + ERP catalog — *build fourth*
- Read `Requests/Services/RequestTriageService.cs`. Keep the six/seven outcomes but present them
  plainly (Approve / Clarify / Check / Reject / …). **Approve is the only task-creating action.**
- This is where **Module → Form → Surface** get set (§5): the requester gave rough input; the
  reviewer refines it into structured product context.
- Success: a messy WhatsApp point becomes committed, well-scoped work in a few clicks.

---

## 13. Do this, in this order (the concrete recovery checklist)

Each step ends by updating `CLAUDE.md` (its own rule) and running `dotnet test` (currently 397
passing — keep it green; add tests through `TestHarness`, never require SQL Server).

1. **Declare the freeze in the repo.** Commit this file. Add a one-line pointer to it at the top of
   `CLAUDE.md`. No new workflow/entity/status/screen until the pilot runs.
2. **Shrink the nav (§11) and hide parked capabilities (§10).** Cheapest possible win against
   "overwhelming." No backend deletion. Verify every role's rail matches §11.
3. **Refine My Tasks (§12A).** Worker execution obvious end-to-end.
4. **Request progress visibility + requester acceptance (§12B, §7).** Kill the relay loop; add
   Fixed/Still-not-fixed; make closure a policy (§4.14).
5. **Assignment screen (§12C).** Facts-based.
6. **Triage + ERP catalog (§12D, §5).** Investigate current `Module` coupling first; add
   `Form`/`FormSurface` client-independently; wire them at triage.
7. **Fast multi-point intake (§8).** One-client, many-points, one submit, paste-to-attach.
8. **Scope discipline (§6).** Linked follow-up requests as later rounds; enforce §4.13.
9. **User documentation (§14).**
10. **Dogfood, then measure (§15).** Run the last 20 real WhatsApp requests through the app before
    adding a single new concept.

---

## 14. User documentation deliverable

Create `docs/USER-GUIDE.md` (a starter skeleton is provided alongside this file). It must be:

- **Task-oriented, per role** — "How do I submit a request?", "How do I see progress?", "How do I
  start a task?", "How do I check a fix?" — not a feature tour.
- **Written for the actual team**, bilingual-friendly: keep sentences short; where a requester-facing
  action is described, include the plain Roman-Urdu phrasing the team already uses (e.g. the
  progress page answers "mera point ka kya bana?"). English structure, Roman-Urdu example phrases
  where they aid adoption.
- **Screenshot placeholders** (`![...](...)`) at each step, filled once §12 screens settle.
- **Honest about the migration state:** section noting that, during the pilot, Ahsan may still enter
  requests on a requester's behalf (§9).

Because §12 screens are actively changing, **write the structure now and fill specifics as each
screen lands** — do not document a moving target in detail and let it go stale. Update the guide in
the same commit that changes a screen.

---

## 15. The only metrics that matter

Feature count, test count, screen count and status count tell you **nothing** about success. Before
the pilot, capture a one-week baseline by counting from WhatsApp; then track weekly:

```
Progress-chasing messages / week      →  measures RELAY
Requests Ahsan manually rewrites / wk  →  measures TRANSLATION
Worker "how do I…" interruptions / wk   →  measures task clarity / JUDGMENT support
```

A good pilot looks like e.g. `30 → 5` relay, `20 → 0` rewrites, `15 → 6` interruptions. If those
fall, the product works even with half its features hidden. If they don't move while you have 397
tests, 13 screens, SignalR, PDF reports and 22 statuses, the product failed despite sound
engineering. **Do not treat any percentage in this file as fact — measure your own numbers.**

---

## 16. What NOT to do (pin this)

- Do **not** restart or rewrite. The architecture (modular monolith, layered, permission-based auth,
  state-machine-as-shape, DB-as-truth, append-only history) is sound. Recover the *product*.
- Do **not** delete parked backend code — hide it (§10).
- Do **not** model Module/Form per-client (§5).
- Do **not** invent a fake "Internal Client" (§5).
- Do **not** silently grow a task's scope after execution starts (§4.13, §6).
- Do **not** build a delivery/deploy/accept chain of new states (§7) — one minimal acceptance gate,
  as a policy.
- Do **not** force four mandatory ERP dropdowns onto the requester (§8) — client + optional
  module/form is the ceiling for intake.
- Do **not** add a feature because a scenario is imaginable (§0). Wait for a real, observed,
  preferably repeated operational problem.
