# WorkflowApp — User Guide

> **Status: skeleton.** Fill each step's specifics and screenshots as the §12 screens in
> `PRODUCT-CORE.md` settle. Update this guide in the same commit that changes a screen. Keep
> sentences short; keep it task-oriented. Roman-Urdu example phrases are intentional — they help the
> team recognise their own workflow.

## What this app replaces

WhatsApp + Excel, for tracking work at IT Media Zone. Instead of messaging Ahsan and waiting for a
reply, you submit work here and see its progress yourself. WhatsApp stays for conversation;
**WorkflowApp is where the work lives.**

> During the early rollout, Ahsan may still enter some requests for you. That is temporary. The same
> page where you watch progress is where you'll submit the next point yourself.

---

## If you *request* work (Requester)

### Submit a request (one client, one or many points)
1. Open **Requests → New**.
2. Pick the **Client** once (e.g. *Impression Sourcing*). If it's internal work, leave client blank.
3. *(Optional)* pick **Module** and **Form** if you know them (e.g. *Sales · Delivery Order*).
4. Type your first point. Paste a screenshot if you have one (Win+Shift+S, then Ctrl+V).
5. Click **+ Add point** for each extra issue on the *same* client. Submit them all at once.
6. Click **Submit all**.
   > *Tip:* ek hi form ke saare points ek saath likh dein — alag alag din bhejne se timeline
   > kharab hoti hai.
   
   `![screenshot: new request form](images/new-request.png)`

### See progress — without asking anyone
1. Open **Requests** and click your request.
2. The page shows the current status in plain words, who's responsible, and the latest update.
   > "mera point ka kya bana?" ka jawab yahin milega — kisi ko message karne ki zarurat nahin.
   
   `![screenshot: request progress](images/request-progress.png)`

### Confirm a fix (or send it back)
1. When a request shows **Ready for confirmation**, open it and check the fix on your side.
2. Click **It's fixed** to close it, or **Still not fixed** and add a short note / screenshot to send
   it back for rework.
   
   `![screenshot: accept or reject](images/acceptance.png)`

### What the statuses mean
| You see | It means |
|---|---|
| Submitted | Received, not yet reviewed |
| Being reviewed | A reviewer is deciding what to do |
| Being checked | Someone is establishing whether it's really a problem / checking the fix |
| In progress | Work is underway |
| Ready for confirmation | We believe it's resolved — please check |
| Completed | Confirmed done |

---

## If you *do* the work (Worker)

### Find your work
1. Open **My Tasks**. Columns tell you what to do next.
2. Each task shows the ERP context (Client · Module · Form · Surface), the expected result, and any
   attached screenshots — everything you need to start without asking.
   
   `![screenshot: my tasks](images/my-tasks.png)`

### Start, pause, finish
1. Click **Start** on a task (one click from the queue). Only one task runs at a time.
2. **Pause** or **Block** with a reason if you're interrupted.
3. **Complete** when done — it goes to QC next.
   
   `![screenshot: task timer](images/task-timer.png)`

### Your shift
- Use the shift widget to clock in/out and set availability. `![screenshot: shift widget](images/shift.png)`

---

## If you *review and assign* (Reviewer / Coordinator — Ahsan)

### Triage a request
1. Open **Requests** (Review is a view inside it). Open a submitted request.
2. Refine the ERP context: **Module → Form → Surface**.
3. Choose an outcome: **Approve** (creates the task), **Clarify**, **Check**, or **Reject**.
   Only **Approve** creates work.
   
   `![screenshot: triage](images/triage.png)`

### Send something for a Check
- If you can't yet tell whether a request is a real problem, send it for a **Check** instead of
  guessing. The checker reports findings and hands it back to you — a check never creates work by
  itself.

### Assign a task
1. In **Tasks**, open assignment for a task.
2. Pick a worker using their real load — who's working now, active vs waiting counts, recent related
   work — not from memory.
   
   `![screenshot: assignment](images/assignment.png)`

### A new point arrived after work started?
- Don't add it to the running task. Raise a **related follow-up request** (it pre-fills the shared
  Client · Module · Form as a later round). The original task's finish line stays honest.

---

## If you *check quality* (QC)

1. Open **Quality**. It holds work awaiting QC and standalone **Checks**.
2. Review against the acceptance criteria. Record **Pass** or **Fail** (fail needs a reason). Attach
   evidence screenshots — they stay with that numbered attempt.
   
   `![screenshot: qc](images/qc.png)`

---

## Settings & account
- Everything about your account and this browser lives behind **Settings** (profile menu).
- Change your own name, email and password there.

---

## Getting help
- Something wrong or confusing? Use the app's feedback path / tell Ahsan — but check the request's
  own progress page first; it usually answers "what's happening?" on its own.
