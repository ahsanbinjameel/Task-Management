# WorkflowApp — User Guide

> **Status: written against the product as built, 2026-08-28.** Screenshot placeholders are still
> to be filled. Update this guide in the same commit that changes a screen — a guide that describes
> a screen nobody can find is worse than no guide.

## What this app replaces

WhatsApp + Excel, for tracking work at IT Media Zone. Instead of messaging Ahsan and waiting for a
reply, you submit work here and see its progress yourself. WhatsApp stays for conversation;
**WorkflowApp is where the work lives.**

> During the early rollout, Ahsan may still enter some requests for you. That is temporary. The same
> page where you watch progress is where you will submit the next point yourself.

## Finding your way around

The menu on the left shows **your job, not the whole system**. Most people see two or three items.

| If you… | You see |
|---|---|
| Ask for work | Home · Requests |
| Do the work | Home · My tasks · Requests |
| Review and assign (Ahsan) | Home · Requests · Tasks · Quality · Team |
| Check quality | Home · Quality |

Things like the review queue, the assignment queue and the QC queue are **not** separate menu
items — they are tabs inside the page they belong to. Everything about your account, and (if you
run the system) the setup screens, lives behind **Settings** in the menu under your name.

---

## If you *request* work (Requester)

### Submit a request — one client, many points, one send

1. Open **Requests → New request**.
2. Pick the **Client** once, at the top. Leave it blank for internal work.
   *(Optional)* If you happen to know it, say which **Module** and **Form** it is about. If you do
   not, skip it — the reviewer fills that in.
3. Type your first point. One box, plain words. **The first line becomes its title**, so put the
   short version first.
4. Paste a screenshot straight into the point: **Win+Shift+S**, then **Ctrl+V**. You can also drag
   a file in or click to choose one.
5. **+ Add another point** for each extra thing about the *same* client. Each point keeps its own
   screenshots.
6. **Submit**.

> *Tip:* ek hi client ke saare points ek saath likh dein — alag alag din bhejne se timeline kharab
> hoti hai.

You are not asked to name your submission, or to write a title and a description separately. One
point sends one request; several send several, each with its own number, and a reviewer decides on
each one separately.

`![screenshot: new request form](images/new-request.png)`

### See progress — without asking anyone

1. Open **Requests** and click your request.
2. The panel at the top says, in plain words: what is happening, who is responsible, how far along
   it is, the latest update from the person doing it, and why it is waiting if it is.

> "mera point ka kya bana?" ka jawab yahin milega — kisi ko message karne ki zarurat nahin.

`![screenshot: request progress](images/request-progress.png)`

### Confirm a fix — or send it back

When your request reaches **Ready for Confirmation**, we think it is done and it is now your turn.

1. Open the request. **Check the fix on your own system first.**
2. **It's fixed** → the request closes. That is the end of it.
3. **Still not fixed** → say what you are still seeing. It goes straight back to the person who did
   the work, and it will be checked again before you are asked a second time.

Only you can answer this. Nobody else can confirm a fix on your behalf, however senior.

`![screenshot: accept or reject](images/acceptance.png)`

### Found something else while testing?

Open the original request and use **Found something else**.

It becomes a request of its own, carrying the client and the product details so you do not retype
them, linked back to the first one and marked as a later round.

**This is the right thing to do, not a nuisance.** It does *not* change the deadline of the work
already in hand — which is exactly why it is a separate request rather than an addition to the
first one. Finding things late is normal; hiding them inside work that was already planned is what
causes trouble.

> Testing ke doosre round mein kuch aur mila? Alag se raise karein — pehle wale ka time nahin
> badlega.

### Answer a question

If a reviewer needs something from you, the request shows **Needs Your Input** and there is a box
to type your answer. That is a question, not a decision — nothing is being refused.

### What the statuses mean

| You see | It means |
|---|---|
| Submitted | Received, not yet reviewed |
| Under Review | A reviewer is deciding what to do |
| Needs Your Input | Somebody has asked you a question |
| Being Checked | Somebody is establishing whether it is really a problem, or checking the fix |
| Approved · Assigned | It is planned, and it has a person |
| In Progress | Work is underway |
| Waiting | It is held up on something |
| **Ready for Confirmation** | **We think it is done — please check it** |
| Completed | Confirmed done |

---

## If you *do* the work (Worker)

### Find your work

Open **My tasks**. The list is in the order you should pick things up, and you can drag rows to
reorder your own queue.

Each row tells you, without opening anything:

- the task number and what it is,
- **where in the system** it is — client, then module · form · part,
- **what "working" is supposed to look like**, in the requester's own words,
- whether there are screenshots to look at,
- how long has been logged on it.

`![screenshot: my tasks](images/my-tasks.png)`

### Start, pause, finish

1. **Start** is on the row itself. One click.
2. Only one task runs at a time. Starting one pauses whatever else was running — you do not have to
   remember to stop the last one.
3. **Pause** or **Block** if you are interrupted. Both ask why: pause is "I stepped away", block is
   "this cannot move until something else happens".
4. **Complete** when done, with a note on what you did. It goes to quality check next.

`![screenshot: task timer](images/task-timer.png)`

### Your shift

The widget at the top right clocks you in and out and sets whether you are available. Signing out
does **not** end your shift — end it deliberately when you finish for the day.

### Fielded a phone call and found real work behind it?

Raise it as a request (**Requests → New request**). It goes through review like anything else. That
is not extra bureaucracy: it is what stops work existing that nobody agreed to and nobody can see.

---

## If you *review and assign* (Reviewer / Coordinator — Ahsan)

### Triage a request

1. Open **Requests**. **To review** is a tab inside it.
2. Read what was asked, and the screenshots.
3. Set **Module → Form → Which part of it**. This is your job, not the requester's — they gave you
   rough words, you turn them into structured product context. Each level narrows the next.
4. Choose an outcome:
   - **Approve** — sets priority, estimate and acceptance criteria, and **creates the task**. This
     is the only outcome that creates work.
   - **Ask for clarification** — a question back to the requester. Not a decision.
   - **Send for checking** — you cannot yet tell whether there is really a problem. See below.
   - **Reject** / **Mark duplicate** / **Postpone** — with a reason.

`![screenshot: triage](images/triage.png)`

### Follow a request after you have approved it

The tiles on **Requests** do not stop at Approved. Once a request has become a task, its tile
follows the work: **Assigned**, **In progress**, **Blocked**, **Quality check**, **Ready for
closure**, **Completed**. So "where did REQ-000012 get to?" is answered on the screen you approved
it from, without going to Tasks and matching it up by hand.

The finer distinctions a coordinator acts on — paused as against blocked, rework as against fresh
work — stay on **Tasks**, where the actions are. Here, paused and rework both read as In progress:
the question a request answers is how far along it is, not what the worker is doing this minute.

### Send something for a Check

If you cannot tell whether a request describes a real problem — a defect, a configuration mistake,
bad data, or a misunderstanding — send it for a **Check** instead of guessing or approving to find
out.

A checker investigates and reports findings. **A check never creates work.** Whatever it finds, the
request comes back to you and you decide. Nothing can be decided on a request while a check on it
is still open.

### Assign a task

1. Open **Tasks**. **To assign** is a tab inside it.
2. The dialog lists everyone who can do the work with the facts you decide on:
   - a filled dot means they are on the clock,
   - what they are working on **right now**, and for how long,
   - how many are active versus waiting, and how many are due today,
   - **recent related work** — whether they have touched this client or module before.

There is deliberately **no capacity number**. Estimates are guesses and a sum of guesses is not
something you can act on. You decide from the facts.

`![screenshot: assignment](images/assignment.png)`

### A new point arrived after work started?

Do not add it to the running task. Ask the requester to use **Found something else** on their
request — or do it yourself from the request. It becomes a linked later round with its own number,
and the original task's finish line stays honest.

### Closing work

The **Closure** panel on a task lists what still has to be true before it can close, and says which
requirement is not met.

If the work came from a request, the panel also tells you **who has to confirm the fix**. That is
information, not a lock: you can still close it — if a requester has gone quiet, work should not be
stranded. But the normal path is for them to press **It's fixed** themselves, and every time they
do that is a message nobody had to send you.

---

## If you *check quality* (QC)

1. Open **Quality**. It has two tabs: the **QC queue** (finished work waiting to be checked) and
   **Checks** (investigations into whether a problem is real).
2. For a QC review: work through the acceptance criteria one at a time and record **Pass** or
   **Fail**. A fail needs a reason.
3. Attach evidence screenshots. They stay with **that numbered attempt** — the pictures that
   justified a failure stay with the failure even after a later attempt passes.
4. For a Check: take it (**Claim** if nobody holds it), investigate, and record what you found.
   Whatever you find, it goes back to a reviewer. A check never creates work by itself.

`![screenshot: qc](images/qc.png)`

---

## If you run the system (Administrator)

Everything is behind **Settings** in the menu under your name.

- **People** — accounts, and what each person is allowed to do.
- **Roles and permissions** — what each role grants. A role nobody holds can be edited freely; the
  system refuses to remove the last route to managing roles.
- **Setup data** — clients, pause reasons, departments, teams, and the **product catalog**:
  **Modules → Forms → Form parts**.
- **Audit log** — the read-only record of who changed what.

### The product catalog

Set this up before triage will be much use:

1. **Modules** — the parts of your product: Sales, Accounts, Inventory.
2. **Forms** — the screens and documents in each module: Delivery Order, Sales Invoice.
3. **Form parts** — the ways of looking at a form: the Form itself, History, Detail Report, Master
   Report.

**The catalog is not per client.** Your product has these; each client runs a copy of it. That is
what makes it possible to ask "is this Delivery Order bug unique to one client, or are four of them
seeing it?"

Nothing here is ever deleted — it is **retired**. A form with requests filed against it is history
that reports still read, and deleting it would turn those into blanks. Retiring takes it out of the
pickers and leaves the history intact.

---

## Getting help

Something wrong or confusing? Tell Ahsan — but check the request's own progress page first. It
usually answers "what is happening?" on its own, and that is the whole point of it.
