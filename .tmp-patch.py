import io

p = 'CLAUDE.md'
s = io.open(p, encoding='utf-8').read()

# --- §4 file index ---------------------------------------------------------------------------------
old = "| `Entities/Requests/Organization.cs` | `Department`, `Team`, `Client`, `Project`, `Module`, `PauseReason` |"
new = ("| `Entities/Requests/Organization.cs` | `Department`, `Team`, `Client`, `Project`, `PauseReason`, "
       "and the **product catalog**: `Module` → `Form` → `FormSurface`. The catalog is "
       "client-independent by design (§6); `Module.ProjectId` is vestigial and must not be used for it |")
assert old in s, "Organization.cs row missing"
s = s.replace(old, new, 1)

old = "| `Common/StatusViews.cs` |"
new = ("| `Common/ProductLocation.cs` | ✅ | The one place that joins module/form/surface into "
       "\"Sales · Delivery Order · Detail Report\". Never stored — it is a rendering of three ids |\n"
       "| `Common/StatusViews.cs` |")
assert old in s, "StatusViews row missing"
s = s.replace(old, new, 1)

# --- migrations row ---------------------------------------------------------------------------------
old = "`AttachmentProof`, `Verifications`, + model snapshot."
new = "`AttachmentProof`, `Verifications`, `ProductCatalog`, + model snapshot."
assert old in s, "migrations row missing"
s = s.replace(old, new, 1)
s = s.replace("| `Persistence/Migrations/` | ✅ | 9 migrations:", "| `Persistence/Migrations/` | ✅ | 10 migrations:", 1)

# --- endpoints ---------------------------------------------------------------------------------------
old = "| GET/POST/PUT/DELETE | `/api/setup/roles`, `/{id}`, `/{id}/permissions` | `Admin.ManageConfig` **and** `Admin.ManageRoles` |"
new = (old + "\n"
       "| GET/POST/PUT | `/api/setup/modules`, `/forms`, `/form-surfaces` (+ `/{id}`, `/{id}/active`) | `Admin.ManageConfig` |")
assert old in s, "setup roles endpoint row missing"
s = s.replace(old, new, 1)

old = "| GET | `/api/lookups/modules` | authenticated |"
new = ("| GET | `/api/lookups/modules`, `/forms?moduleId=`, `/form-surfaces?formId=` | authenticated |")
assert old in s, "lookups modules row missing"
s = s.replace(old, new, 1)

old = "| GET | `/api/tasks/assignment-queue`, `/assignable-users` | `Task.Assign` |"
new = (old + "\n"
       "| GET | `/api/tasks/{id}/assignment-candidates` | `Task.Assign` |")
assert old in s, "assignment-queue row missing"
s = s.replace(old, new, 1)

old = "| POST | `/api/tasks/{id}/close` | `Task.Close` |"
new = (old + "\n"
       "| POST | `/api/tasks/{id}/accept`, `/reject` | authenticated — **the requester of the originating "
       "request only**, enforced on the record, not by a permission |")
assert old in s, "close endpoint row missing"
s = s.replace(old, new, 1)

# --- invariants ---------------------------------------------------------------------------------------
old = """12. **`Task.Work`, `Verification.Work` and `Workforce.TrackShift` are independent.**"""
new = """12. **`Task.Work`, `Verification.Work` and `Workforce.TrackShift` are independent.**"""
assert old in s
tail = """   another, and every combination is a legitimate configuration. Administering the system implies
   none of them."""
assert tail in s
s = s.replace(tail, tail + """
13. **Once execution starts, a task's committed scope does not silently grow.** New points that
   arrive after work has begun become *new linked requests*, never an invisible extension of the
   running task (PRODUCT-CORE §6).
14. **Requester acceptance is a per-work-type closure *policy*, not a universal invariant.**
   Work with a requester behind it is confirmed by them; work with none closes on the quality check.
   It is reported beside the closure checklist rather than as one of its requirements, so a
   coordinator whose requester has gone quiet is told rather than blocked (PRODUCT-CORE §7).
15. **The ERP context is two orthogonal axes, not one tree.**
   `Request = Client? × ProductLocation(Module → Form → Surface)`. The catalog is
   client-independent and nothing in it may reference a client; `ClientId` is nullable and there is
   **no "Internal" client** (PRODUCT-CORE §5).""", 1)

io.open(p, 'w', encoding='utf-8').write(s)
print("CLAUDE.md index + invariants updated")
