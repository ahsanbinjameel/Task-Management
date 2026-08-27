# WorkflowApp

Internal operations system enforcing **Request → Review → Approval → Assignment → Execution → QC →
Closure**, with shift/attendance tracking, dashboards, notifications and real-time updates.

**Angular 21 + Angular Material** front end · ASP.NET Core 8 Web API · EF Core 8 · SQL Server ·
SignalR · IIS. The client builds into the API's `wwwroot`, so a deployment is a single artifact.

---

## Which of these are you trying to do?

| Goal                              | Go to                                                  |
| --------------------------------- | ------------------------------------------------------ |
| Develop against a real SQL Server | [A. Dev server](#a-dev-server--sql-server)             |
| Work on the Angular front end     | [A4. Front-end development](#a4-front-end-development) |
| Deploy to IIS                     | [B. IIS deployment](#b-iis-deployment)                 |
| Something broke                   | [Troubleshooting](#troubleshooting)                    |

> **SQL Server is required.** There is no lighter mode to evaluate against — see
> [Why there is no demo mode](#why-there-is-no-demo-mode).

---

## Prerequisites

|                                   |   Dev    |        IIS         |
| --------------------------------- | :------: | :----------------: |
| .NET 8 SDK                        |    ✅    | build machine only |
| **Node.js 20+ and npm**           |    ✅    | build machine only |
| SQL Server 2019+                  |    ✅    |         ✅         |
| ASP.NET Core 8 **Hosting Bundle** |    —     |    ✅ (server)     |
| `dotnet-ef` global tool           | optional |      optional      |

Check what you have:

```bash
dotnet --list-sdks          # need an 8.x SDK (a newer one also builds net8.0)
dotnet --list-runtimes      # need Microsoft.AspNetCore.App 8.x to *run* the API
sqlcmd -S localhost -E -Q "SELECT @@VERSION"
```

`dotnet-ef` is **not** installed by default. You only need it to generate migration scripts:

```bash
dotnet tool install --global dotnet-ef --version 8.*
```

---

## A. Dev server — SQL Server

### 1. Build and test

```bash
dotnet restore
dotnet build
dotnet test          # 397 tests; none of them need SQL Server
```

### 2. Point at your SQL Server

`src/WorkflowApp.Api/appsettings.Development.json` ships with:

```json
"Default": "Server=localhost;Database=WorkflowApp_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

Change `Server=` if your instance is elsewhere (e.g. `.\SQLEXPRESS`, or `(localdb)\MSSQLLocalDB`).

### 3. Build the client, once

The client is served from `wwwroot`, which is **build output and gitignored**. On a fresh clone this
step is not optional — without it the API serves nothing at the root.

```bash
cd client
npm ci
npm run build          # compiles into ../src/WorkflowApp.Api/wwwroot
cd ..
```

### 4. Run it

```bash
dotnet run --project src/WorkflowApp.Api --launch-profile Development
```

That's the whole step. In **Development only**, `Database:ApplyMigrationsOnStartup` is `true`, so
starting the API **creates the database, applies migrations, and seeds it** automatically.

- App: **<https://localhost:7099>**
- Swagger: **<https://localhost:7099/swagger>**
- Health: `/health`, `/health/ready`

Sign in as `admin` / `ChangeMe!2024`.

### Creating the schema by hand instead

If you would rather not have the app touch the schema:

```bash
# Option 1: EF
cd src/WorkflowApp.Api
dotnet ef database update --project ../WorkflowApp.Infrastructure --startup-project .

# Option 2: the committed SQL script (review it first)
sqlcmd -S localhost -E -I -d WorkflowApp_Dev -i scripts/sql/001-InitialCreate.idempotent.sql -b
```

⚠️ **`sqlcmd` needs `-I`.** The schema has filtered indexes, which require `QUOTED_IDENTIFIER ON`;
`sqlcmd` defaults it **off** and the script will fail partway with error 1934, leaving a
half-created database. SSMS sets it on by default, so this only bites from the command line.

### A4. Front-end development

For UI work, run the Angular dev server instead of rebuilding into `wwwroot` each time. It serves on
:4200 with hot reload and proxies `/api` and `/hubs` to the API on :7099 (`client/proxy.conf.json`):

```bash
# terminal 1
dotnet run --project src/WorkflowApp.Api --launch-profile Development

# terminal 2
cd client && npm start        # http://localhost:4200
```

The client lives in `client/`:

```
src/app/
  core/       models mirroring the API DTOs, ApiService, auth + refresh interceptor, SignalR
  shared/     chips, empty states, the shared task table, confirm/reason dialogs
  layout/     shell, permission-filtered navigation, notification bell, shift widget
  features/   one folder per area: dashboard, tasks, requests, qc, workforce, reports, admin, me
```

Two conventions worth knowing before changing anything:

- **The menu and every action button are filtered by permission**, read from the JWT. That is a
  usability decision, not a security one — the API re-checks everything, so never rely on a hidden
  button.
- **Real-time events are pointers, not records.** A SignalR message says _what changed_; the screen
  re-fetches. Never patch local state from an event payload.

### Adding a migration

```bash
cd src/WorkflowApp.Api
dotnet ef migrations add <Name> --project ../WorkflowApp.Infrastructure --startup-project . \
  --output-dir Persistence/Migrations
```

---

## B. IIS deployment

### 1. Prepare the server, once

```powershell
# ASP.NET Core 8 Hosting Bundle — the runtime alone is not enough, IIS needs the module.
# Download from https://dotnet.microsoft.com/download/dotnet/8.0

Install-WindowsFeature Web-WebSockets       # SignalR falls back to polling without it
```

App pool: **No Managed Code**, `LoadUserProfile = true`.

### 2. Create the database

```powershell
sqlcmd -S localhost -E -I -Q "IF DB_ID('WorkflowApp') IS NULL CREATE DATABASE [WorkflowApp];"
sqlcmd -S localhost -E -I -d WorkflowApp -i scripts\sql\001-InitialCreate.idempotent.sql -b
```

### 3. Give the app pool a SQL login

The connection string uses `Trusted_Connection=True`, so the app connects **as the app pool
identity**. That login does not exist by default — this is the single most common cause of a working
build that cannot read anything:

```sql
CREATE LOGIN [IIS APPPOOL\DefaultAppPool] FROM WINDOWS;
USE [WorkflowApp];
CREATE USER [IIS APPPOOL\DefaultAppPool] FOR LOGIN [IIS APPPOOL\DefaultAppPool];
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\DefaultAppPool];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\DefaultAppPool];
```

Substitute your pool name if it is not `DefaultAppPool`.

### 4. Build the release

```powershell
./scripts/deploy.ps1            # builds the Angular client, runs tests, publishes to .\publish
```

This builds the client first, on purpose: it compiles into `wwwroot`, and `dotnet publish` collects
whatever is there. Skipping it ships yesterday's front end.

### 5. Configure it — the app will not start otherwise

Edit **`publish\web.config`** and set the environment variables inside
`<aspNetCore><environmentVariables>`:

| Variable                     | Notes                                                                                                               |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`     | `Production`                                                                                                        |
| `Jwt__SigningKey`            | **Required.** ≥ 32 bytes of real entropy. Startup _refuses_ to run with the placeholder                             |
| `ConnectionStrings__Default` | Your production database                                                                                            |
| `Workforce__TimeZoneId`      | ⚠️ Defaults to UTC. A wrong value silently skews every daily report and puts overnight shifts on the wrong date     |
| `FileStorage__Root`          | A directory outside the site folder, e.g. `C:\WorkflowApp\storage`                                                  |
| `Cors__Origins__0`           | The client origin. Credentials are allowed, so no wildcards                                                         |
| `Security__RequireHttps`     | Leave unset (defaults `true`). Set `false` **only** on an internal HTTP-only host or behind a TLS-terminating proxy |

Generate a signing key:

```powershell
$b = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
[Convert]::ToBase64String($b)
```

> `dotnet publish` **overwrites `web.config`**. Keep the configured copy in `.\publish` and let
> step 6 carry it across, or you will lose these settings on every deployment.

### 6. Install it — needs an elevated PowerShell

```powershell
./scripts/install-iis.ps1 -Target C:\inetpub\wwwroot\publish
```

It stops the app pool (so the DLLs are not locked), copies the files, creates the attachment store
and log directory, grants the app pool identity access, and restarts the pool.

### 7. Confirm

```powershell
curl http://<host>:<port>/health         # liveness  — no database call
curl http://<host>:<port>/health/ready   # readiness — proves the database is reachable
```

Then sign in as `admin` / `ChangeMe!2024` and **change that password immediately**.

Full operational detail — backups, log messages to watch, rollback — is in
[`docs/03-RUNBOOK.md`](docs/03-RUNBOOK.md).

---

## Why there is no demo mode

There was one: a `Demo` launch profile running on a SQLite file with `EnsureCreated()`, a seeded
cast of sample users and a shared well-known password. It has been **removed, and is not coming
back** — do not reintroduce it, and do not extend anything toward it.

It was a second implementation of the product that nobody shipped. SQLite has no `ROWVERSION`, so
every concurrency guard ran as code and was enforced by nothing; `EnsureCreated()` skips migrations,
so the schema it produced was the one nobody deploys. Both had to be kept working, which meant every
feature after the first was written twice and verified once — and the copy that got verified was the
one that could not fail the way production fails.

**No feature or requirement added from here on is to be reflected in a demo, evaluation or sample
mode of any kind.** Run it against SQL Server, the way it is deployed.

---

## Troubleshooting

### `HTTP Error 500.30 — ASP.NET Core app failed to start`

The app threw during startup. IIS will not tell you why; get the real exception:

```powershell
cd C:\inetpub\wwwroot\publish
$env:ASPNETCORE_ENVIRONMENT="Production"; dotnet WorkflowApp.Api.dll
```

The exception prints immediately. Or enable `stdoutLogEnabled="true"` in `web.config` and read
`.\logs\stdout_*.log`.

**By far the most likely cause:**

```
System.InvalidOperationException: Jwt:SigningKey is unset or still the placeholder.
```

This is deliberate — the app refuses to run outside Development with a known signing key. Set
`Jwt__SigningKey` (step B5).

### The site loads, but every request 500s

The app started but cannot reach the database. Check `/health/ready`, then:

- Does the database exist? `SELECT name FROM sys.databases`
- Does the app pool identity have a login? (step B3)
- Is the connection string right? Remember Production uses `WorkflowApp`, Development uses
  `WorkflowApp_Dev` — they are different databases.

### The browser redirects to `https://` and nothing answers

Production forces an HTTPS redirect. If you are serving plain HTTP on an internal host, set
`Security__RequireHttps` to `false` — or bind a certificate, which is the better answer.

### `error 1934 ... QUOTED_IDENTIFIER` when running the SQL script

Add `-I` to `sqlcmd`. See the warning in section A. The database is now half-created: drop it and
re-run from scratch.

### The page is blank, or you get a bare 404 at the root

`wwwroot` is build output and is gitignored, so a fresh clone has no client in it:

```bash
cd client && npm ci && npm run build
```

### Deep links 404 after deploying

The API serves the SPA with a fallback route. If `/tasks/5` 404s but `/` works, the deployed build
predates that fallback — republish.

### `You must install or update .NET to run this application`

The .NET 8 runtime is missing. On a server, install the **Hosting Bundle**, not just the runtime.

### Logged in, but everything returns 403

Permissions live on the JWT and are refreshed only when a token is issued. After a role change,
sign out and back in.

### Port already in use

```bash
dotnet run --project src/WorkflowApp.Api --no-launch-profile --urls http://localhost:5199
```

---

## What exists today

**The server side is complete** — all twelve phases: identity and permissions, shifts and
attendance, request intake and triage, the task workflow engine, assignment and queues, the work
timer, QC and closure, comments/dependencies/subtasks/scope/reopen, SignalR, dashboards and reports,
notifications and audit, and the hardening pass.

**The Angular client** covers the whole pipeline: role-aware dashboards, request intake and triage,
task queues, the work timer, QC review with the acceptance-criteria checklist, the closure checklist,
comments, dependencies, subtasks, scope changes, shifts and availability, workforce views, reports
with CSV export, notifications, user administration and the audit log.

### Layout

```
client/                       Angular 21 + Material front end (builds into the API's wwwroot)
src/
  WorkflowApp.Domain          entities, enums, workflow state machines. Zero dependencies.
  WorkflowApp.Application     use-case services, DTOs, permission catalog
  WorkflowApp.Infrastructure  EF Core, migrations, JWT, hashing, seeding, file storage
  WorkflowApp.Api             controllers, SignalR hub, middleware, DI, serves the client
tests/                        397 tests, none requiring SQL Server
docs/
  01-ARCHITECTURE.md          why the system is shaped this way
  02-PHASE-PLAN.md            what was built, phase by phase
  03-RUNBOOK.md               deploy and operate
scripts/
  deploy.ps1                  build + test + publish + migration script
  install-iis.ps1             install a staged publish into IIS (elevated)
  sql/                        reviewable idempotent schema scripts
CLAUDE.md                     the project context map — read this before changing code
```

### Rules the code will not let you break

1. A request never auto-becomes a task — approval creates it, explicitly and in one place.
2. One active work session per user, enforced three ways including a database index.
3. No status transition outside the allowed map.
4. Every mutating transition is permission-checked server-side. Hiding a button is not security.
5. A reason is mandatory for: reject, pause, block, QC fail, reopen, override, reassign.
6. History is append-only — nothing overwrites a comment, session, QC attempt or status change.
7. The database is the source of truth; SignalR only notifies.
8. QC verdicts and closures can only be reached through their own endpoints, so each always leaves
   its record behind.
