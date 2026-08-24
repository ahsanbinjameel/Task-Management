# Deployment Runbook

Target: **IIS in-process, Windows Server, SQL Server 2019+**, .NET 8 Hosting Bundle.

This is the operational companion to `01-ARCHITECTURE.md` (why the system is shaped this way) and
`02-PHASE-PLAN.md` (what is built). Read §1 before the first deployment; §5 is the one to keep open
during an incident.

---

## 1. Prerequisites on the server

| | |
|---|---|
| .NET | **ASP.NET Core 8 Hosting Bundle** (not just the runtime — IIS needs the module) |
| IIS | Application Initialization + WebSocket Protocol features enabled |
| SQL Server | 2019 or later, reachable from the app pool identity |
| TLS | A real certificate bound to the site. The app issues HSTS-relevant headers and redirects to HTTPS |
| Fonts | At least one of **Segoe UI**, Arial, DejaVu Sans or Liberation Sans installed |

**WebSockets must be enabled.** SignalR falls back to long polling without it, which works but costs
a connection per client. `Install-WindowsFeature Web-WebSockets`.

**App pool:** No Managed Code (the app self-hosts Kestrel behind IIS), `LoadUserProfile = true`,
and an identity that can read the file-storage root and connect to SQL Server.

**A font must be installed.** PDF report rendering needs one, and the application **refuses to
start** without it rather than failing on the first report somebody prints — the startup error
names every family and directory it searched. A normal Windows Server has Segoe UI and Arial; a
Server Core install or a slim container may have neither. Install one, or drop a `.ttf` into the
fonts directory. This is a display concern only: nothing else in the application depends on it.

---

## 2. Configuration that must be set before first run

Nothing below has a usable default. The application **refuses to start** outside Development if the
JWT key is still the placeholder; the rest fail quietly, which is worse, so check them.

Set these as environment variables. For IIS in-process the simplest durable place is
`<aspNetCore><environmentVariables>` inside the site's `web.config` — note that a plain
`dotnet publish` **overwrites** `web.config`, so keep the configured copy in the staged publish
folder and let `install-iis.ps1` carry it across. Double underscore is the section separator:

| Variable | Why |
|---|---|
| `ConnectionStrings__Default` | The production database |
| `Jwt__SigningKey` | ≥ 32 bytes of real entropy. Startup rejects the placeholder |
| `Workforce__TimeZoneId` | **Defaults to UTC.** Wrong value silently skews every daily report and overnight shift |
| `FileStorage__Root` | A real directory the app pool identity can write to. Not inside the site folder |
| `Cors__Origins__0` | The Angular client's origin. Credentials are allowed, so wildcards are not |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Security__RequireHttps` | Leave unset (defaults true). Set `false` **only** on an internal host serving plain HTTP or behind a TLS-terminating proxy — otherwise clients are redirected to a port nothing is listening on |

The app pool identity also needs a SQL Server login when the connection string uses
`Trusted_Connection=True`. It does not exist by default:

    CREATE LOGIN [IIS APPPOOL\DefaultAppPool] FROM WINDOWS;
    USE [WorkflowApp];
    CREATE USER [IIS APPPOOL\DefaultAppPool] FOR LOGIN [IIS APPPOOL\DefaultAppPool];
    ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\DefaultAppPool];
    ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\DefaultAppPool];

Generate a signing key:

    [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))

Leave `Database__ApplyMigrationsOnStartup` **false** in production (it is the default there).
Migrations belong in the deployment step, not in application startup — see §3.

---

## 3. Deploying

    # 1. Publish
    dotnet publish src/WorkflowApp.Api -c Release -o ./publish

    # 2. Generate the migration script for this release (run from src/WorkflowApp.Api)
    dotnet ef migrations script --idempotent `
      --project ../WorkflowApp.Infrastructure --startup-project . `
      --output ../../scripts/sql/release.sql

    # 3. Review the script, then apply it. Idempotent: safe to re-run, and safe if some
    #    migrations are already applied.
    #    From sqlcmd you MUST pass -I. Filtered indexes require QUOTED_IDENTIFIER ON and sqlcmd
    #    defaults it OFF, so without -I the script fails partway with error 1934 having already
    #    created some tables. SSMS sets it on by default.
    sqlcmd -S <server> -E -I -d WorkflowApp -i scripts/sql/release.sql -b

    # 4. Install the build. Needs elevation: it stops the app pool so the DLLs are not locked,
    #    copies the files, creates the attachment store, and grants the app pool identity access.
    ./scripts/install-iis.ps1 -Source .\publish -Target C:\inetpub\wwwroot\publish

    # 5. Confirm
    curl https://<host>/health          # liveness  — no database call
    curl https://<host>/health/ready    # readiness — proves the database is reachable

`dotnet-ef` is a global tool and is **not** installed by default:

    dotnet tool install --global dotnet-ef --version 8.*

Apply the schema **before** swapping the binaries. The migrations to date are additive, so the old
build tolerates the new schema for the seconds between the two steps.

---

## 4. First-run checklist

The seeder is idempotent and runs on startup in **every** environment, controlled by
`Database:SeedOnStartup` (default true) and deliberately independent of
`Database:ApplyMigrationsOnStartup`. Applying migrations rewrites the schema and is a production
hazard; seeding only inserts rows the application cannot run without. They used to be one switch,
which meant a production database came up with a valid schema and no roles in it.

The seeder creates the permission catalog, the seven system roles with their grants, the default
pause reasons, and a bootstrap administrator.

- [ ] Log in as `admin` / `ChangeMe!2024`
- [ ] **Change that password immediately** — it is written to the log as a warning on every seed
- [ ] Create real users and assign roles; `Task.Reopen` sits with Reviewer by default
- [ ] Confirm the filtered unique indexes exist (see §6)
- [ ] Confirm `Workforce:TimeZoneId` is right by starting a shift and checking the daily timeline
      lands on the expected date

---

## 5. Operating

### Health

| Endpoint | Meaning | Use for |
|---|---|---|
| `/health` | The process is up | IIS Application Initialization, process monitor |
| `/health/ready` | The database is reachable | Load balancer membership |

They are deliberately separate: a database outage should pull the instance out of rotation, not
trigger a restart loop that fixes nothing.

### Background work

`StaleShiftSweepService` runs every `Workforce:StaleShiftScanMinutes` (default 30) and closes shifts
left open past `Workforce:MaxShiftHours` (default 16). It closes them **at the last sign of life**,
not at sweep time — crediting the user until the sweep noticed would inflate attendance by hours.
Closures are flagged `EndedImproperly` and written to the audit log.

It fails soft: a sweep that throws is logged and retried next interval. If sweeps are failing, shifts
accumulate; the symptom is an inflated "people on shift" count on the coordinator dashboard.

### Real-time

The hub is at `/hubs/workflow`. Clients authenticate with `?access_token=` because browsers cannot
set headers on a WebSocket handshake.

SignalR **only notifies** — the database is the source of truth and payloads are deliberately thin.
So a real-time outage degrades the product to "the user must refresh"; it never causes data loss and
is not a reason to fail a deployment. If the hub is down, check WebSockets are enabled in IIS.

Running more than one instance requires a backplane (Redis) — group membership is per-process. Until
then, run a single instance or use sticky sessions.

### Logs to watch

| Message | Meaning |
|---|---|
| `Task {TaskNumber} force-moved {From} → {To}` | A workflow override. Should be rare; investigate a pattern |
| `Task {TaskNumber} reopened by user` | Closed work reopened |
| `Concurrent assignment rejected for task` | Two coordinators collided. Expected occasionally; a burst means a UI problem |
| `Failed to publish N integration event(s)` | Real-time delivery is broken. Not data loss |
| `Seeded bootstrap administrator 'admin'` | Should appear **once**. Recurring means the database is being recreated |

---

## 6. Verifying the database guarantees

Four constraints carry business rules that the application also enforces. If they are missing, the
application still behaves correctly under normal use and silently loses its last line of defence
under a race — so check them after any manual schema work:

    SELECT i.name, i.filter_definition
    FROM sys.indexes i
    WHERE i.name IN (
      'UX_WorkSession_OneActivePerUser',   -- WHERE [Status] = 0
      'UX_QuickWork_OneActivePerUser',     -- WHERE [Status] = 0
      'UX_ShiftSession_OneOpenPerUser',    -- WHERE [ShiftEnd] IS NULL
      'UX_RefreshToken_TokenHash');

    -- Optimistic concurrency: these must be real ROWVERSION (timestamp) columns
    SELECT OBJECT_NAME(object_id) + '.' + name
    FROM sys.columns
    WHERE name = 'RowVersion' AND system_type_id = TYPE_ID('timestamp');
    -- expect: Users, Requests, Tasks, WorkSessions, ShiftSessions, QuickWork, RequestBatches

---

## 7. Backup and recovery

Back up `WorkflowApp` **and** the `FileStorage:Root` directory. They are separate stores and a
restore that recovers only one leaves attachment rows pointing at files that are not there.

The `AuditLogs` table is append-only by design and there is no application route that deletes from
it. Do not add one; archive old rows to a separate table if it grows.

To restore into a new environment: restore the database, restore the storage directory, set the
configuration in §2, then run §3 step 5 to confirm.

---

## 8. Rollback

1. Stop the site.
2. Restore the previous publish folder.
3. Start the site.

Migrations are **not** rolled back as part of this. Every migration to date is additive, so an older
build runs against a newer schema. If a future migration drops or renames a column, that stops being
true — such a migration needs a two-release plan (add and backfill, deploy, then remove in the
following release), and this section needs updating when it happens.
