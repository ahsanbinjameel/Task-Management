#!/usr/bin/env bash
# End-to-end drive of the Verification feature against SQL Server, over HTTP.
set -u
API=https://localhost:7099
C="curl -sk -m 20"

pass=0; fail=0
ok()   { echo "  PASS  $1"; pass=$((pass+1)); }
bad()  { echo "  FAIL  $1"; fail=$((fail+1)); }
check(){ if [ "$2" = "$3" ]; then ok "$1 ($2)"; else bad "$1 - expected $3, got $2"; fi; }

jqv() { python -c "import sys,json;d=json.load(sys.stdin);print(d$1)" 2>/dev/null; }

login() {
  $C -X POST "$API/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"userName\":\"$1\",\"password\":\"$2\"}" | jqv "['accessToken']"
}

echo "== setting up accounts =="
ADMIN=$(login admin 'ChangeMe!2024')
if [ -z "$ADMIN" ]; then echo "cannot sign in as admin; aborting"; exit 1; fi
ok "admin signed in"

# AssignRolesRequest takes role NAMES, not ids.
mkuser() { # name, roleName
  local id
  id=$($C -X POST "$API/api/users" -H "Authorization: Bearer $ADMIN" \
        -H 'Content-Type: application/json' \
        -d "{\"userName\":\"$1\",\"displayName\":\"$1\",\"email\":\"$1@e2e.local\",\"password\":\"E2ePass!2026\"}" \
       | jqv "['id']")
  if [ -z "$id" ]; then
    id=$($C "$API/api/users?pageSize=200" -H "Authorization: Bearer $ADMIN" | python -c "
import sys,json
d=json.load(sys.stdin)
print(next((str(u['id']) for u in d['items'] if u['userName']=='$1'),''))")
  fi
  $C -X PUT "$API/api/users/$id/roles" -H "Authorization: Bearer $ADMIN" \
     -H 'Content-Type: application/json' -d "{\"roles\":[\"$2\"]}" >/dev/null
  echo "$id"
}

RID=$(mkuser e2e_req Requester)
VID=$(mkuser e2e_rev Reviewer)
QID=$(mkuser e2e_qc  QC)
WID=$(mkuser e2e_wrk Worker)
echo "  requester=$RID reviewer=$VID checker=$QID worker=$WID"

REQ=$(login e2e_req 'E2ePass!2026')
REV=$(login e2e_rev 'E2ePass!2026')
CHK=$(login e2e_qc  'E2ePass!2026')
WRK=$(login e2e_wrk 'E2ePass!2026')
[ -n "$REV" ] && ok "reviewer signed in" || bad "reviewer sign-in"
[ -n "$CHK" ] && ok "checker signed in"  || bad "checker sign-in"

echo
echo "== 1. a request is raised and routed for checking =="
R=$($C -X POST "$API/api/requests" -H "Authorization: Bearer $REQ" \
     -H 'Content-Type: application/json' \
     -d '{"title":"Employee Salary form is not calculating tax correctly","description":"Tax column shows zero on the higher band.","type":"Bug","requestedUrgency":"High","clientName":"E2E Client"}')
REQ_ID=$(echo "$R" | jqv "['id']")
REQ_NO=$(echo "$R" | jqv "['requestNumber']")
if [ -n "$REQ_ID" ]; then ok "request raised ($REQ_NO)"; else bad "request raise"; echo "$R" | head -c 400; echo; fi

$C -X POST "$API/api/requests/$REQ_ID/start-review" -H "Authorization: Bearer $REV" >/dev/null

T=$($C -X POST "$API/api/requests/$REQ_ID/triage" -H "Authorization: Bearer $REV" \
     -H 'Content-Type: application/json' \
     -d "{\"outcome\":\"SendForVerification\",\"verification\":{\"targetType\":\"Request\",\"instructions\":\"Reproduce on the higher band; say whether the rate table has a row for it.\",\"assignToUserId\":$QID}}")
VER_ID=$(echo "$T" | jqv "['verificationId']")
VER_NO=$(echo "$T" | jqv "['verificationNumber']")
if [ -n "$VER_ID" ]; then ok "routed for checking ($VER_NO)"; else bad "send for verification"; echo "$T" | head -c 400; echo; fi
check "request status" "$(echo "$T" | jqv "['status']")" "UnderVerification"
check "no task created" "$(echo "$T" | jqv "['createdTaskId']")" "None"

echo
echo "== 2. the requester is told 'Being Checked', not our vocabulary =="
check "requester view label" "$($C "$API/api/requests/$REQ_ID" -H "Authorization: Bearer $REQ" | jqv "['viewLabel']")" "Being Checked"
check "reviewer view label"  "$($C "$API/api/requests/$REQ_ID" -H "Authorization: Bearer $REV" | jqv "['viewLabel']")" "Being verified"

echo
echo "== 3. nothing can be decided while the check is open =="
A=$($C -o /dev/null -w '%{http_code}' -X POST "$API/api/requests/$REQ_ID/triage" \
     -H "Authorization: Bearer $REV" -H 'Content-Type: application/json' \
     -d '{"outcome":"Approve"}')
check "approve refused" "$A" "409"
J=$($C -X POST "$API/api/requests/$REQ_ID/triage" -H "Authorization: Bearer $REV" \
     -H 'Content-Type: application/json' -d '{"outcome":"Reject","reason":"changed our minds"}')
check "reject refused with the same code" "$(echo "$J" | jqv "['code']")" "request.verification_pending"

echo
echo "== 4. only the assigned checker can act =="
check "reviewer cannot start it" \
  "$($C -o /dev/null -w '%{http_code}' -X POST "$API/api/verifications/$VER_ID/start" -H "Authorization: Bearer $REV")" "403"
check "checker can start it" \
  "$($C -o /dev/null -w '%{http_code}' -X POST "$API/api/verifications/$VER_ID/start" -H "Authorization: Bearer $CHK")" "200"

echo
echo "== 5. evidence is the checker's to supply =="
printf 'fake evidence' > /tmp/e2e-evidence.png
check "reviewer refused evidence" \
  "$($C -o /dev/null -w '%{http_code}' -X POST "$API/api/verifications/$VER_ID/attachments" -H "Authorization: Bearer $REV" -F "file=@/tmp/e2e-evidence.png")" "403"
check "checker accepted" \
  "$($C -o /dev/null -w '%{http_code}' -X POST "$API/api/verifications/$VER_ID/attachments" -H "Authorization: Bearer $CHK" -F "file=@/tmp/e2e-evidence.png")" "200"

echo
echo "== 6. findings are required, and confirming a problem creates nothing =="
F=$($C -X POST "$API/api/verifications/$VER_ID/result" -H "Authorization: Bearer $CHK" \
     -H 'Content-Type: application/json' -d '{"result":"IssueConfirmed","findings":"   "}')
# [Required] on the DTO catches whitespace-only before the controller runs, so this arrives as a
# 400 with a field error rather than the service's own stable code. That is the better shape for
# the UI (FormSubmit renders it under the textarea), and the service check still guards the
# contract for any non-HTTP caller. Asserted here as what actually happens.
check "blank findings refused" "$(echo "$F" | jqv "['errors']['Findings'][0]")" "The Findings field is required."

F=$($C -X POST "$API/api/verifications/$VER_ID/result" -H "Authorization: Bearer $CHK" \
     -H 'Content-Type: application/json' \
     -d '{"result":"IssueConfirmed","findings":"Reproduced. The rate table has no row above 40 percent."}')
check "reported" "$(echo "$F" | jqv "['status']")" "Completed"
check "result label is words" "$(echo "$F" | jqv "['resultLabel']")" "Problem confirmed"
check "evidence kept with it" "$(echo "$F" | jqv "['attachments'].__len__()")" "1"

RD=$($C "$API/api/requests/$REQ_ID" -H "Authorization: Bearer $REV")
check "request back in review" "$(echo "$RD" | jqv "['status']")" "InReview"
check "still no task" "$(echo "$RD" | jqv "['generatedTaskId']")" "None"
check "findings on the request" "$(echo "$RD" | jqv "['verifications'][0]['resultLabel']")" "Problem confirmed"

echo
echo "== 7. approval is what finally creates the task =="
T=$($C -X POST "$API/api/requests/$REQ_ID/triage" -H "Authorization: Bearer $REV" \
     -H 'Content-Type: application/json' \
     -d '{"outcome":"Approve","approvedPriority":"High","acceptanceCriteria":"Tax is correct on the higher band"}')
NEW_TASK=$(echo "$T" | jqv "['createdTaskId']")
if [ -n "$NEW_TASK" ] && [ "$NEW_TASK" != "None" ]; then
  ok "task created on approval ($(echo "$T" | jqv "['createdTaskNumber']"))"
else bad "approval"; echo "$T" | head -c 400; echo; fi

echo
echo "== 8. an independent check needs no request at all =="
IV=$($C -X POST "$API/api/verifications" -H "Authorization: Bearer $REV" \
      -H 'Content-Type: application/json' \
      -d "{\"title\":\"Check whether Employee Salary generation form is functioning\",\"targetType\":\"Form\",\"targetName\":\"Employee Salary generation\",\"priority\":\"Normal\",\"assignToUserId\":$QID}")
IV_ID=$(echo "$IV" | jqv "['id']")
if [ -n "$IV_ID" ]; then ok "independent check raised ($(echo "$IV" | jqv "['verificationNumber']"))"; else bad "independent check"; echo "$IV" | head -c 400; echo; fi
check "no request behind it" "$(echo "$IV" | jqv "['requestId']")" "None"

echo
echo "== 9. a check cannot be given to someone who cannot carry one out =="
BADA=$($C -X POST "$API/api/verifications" -H "Authorization: Bearer $REV" \
       -H 'Content-Type: application/json' \
       -d "{\"title\":\"Check something\",\"targetType\":\"Form\",\"targetName\":\"X\",\"priority\":\"Normal\",\"assignToUserId\":$RID}")
check "requester refused as checker" "$(echo "$BADA" | jqv "['code']")" "verification.checker_cannot_work"

echo
echo "== 10. scoping: an unrelated worker gets 404, not 403 =="
check "out of scope reads as absent" \
  "$($C -o /dev/null -w '%{http_code}' "$API/api/verifications/$IV_ID" -H "Authorization: Bearer $WRK")" "404"
check "the checker can read it" \
  "$($C -o /dev/null -w '%{http_code}' "$API/api/verifications/$IV_ID" -H "Authorization: Bearer $CHK")" "200"

echo
echo "== 11. a check nobody holds can be picked up =="
UNCLAIMED=$($C -X POST "$API/api/verifications" -H "Authorization: Bearer $REV"       -H 'Content-Type: application/json'       -d '{"title":"Check the leave balance report","targetType":"Form","targetName":"Leave balance","priority":"Normal"}')
UC_ID=$(echo "$UNCLAIMED" | jqv "['id']")
check "raised with nobody on it" "$(echo "$UNCLAIMED" | jqv "['status']")" "Requested"
check "a checker can take it"   "$($C -o /dev/null -w '%{http_code}' -X POST "$API/api/verifications/$UC_ID/claim" -H "Authorization: Bearer $CHK")" "200"
# And once held, it cannot be taken from under them -- that needs assigning, which asks why.
check "a second checker cannot take it"   "$($C -X POST "$API/api/verifications/$UC_ID/claim" -H "Authorization: Bearer $ADMIN" | jqv "['code']")" "verification.already_claimed"

echo
echo "== 11b. the checker's queue =="
check "the new check is on the desk" "$($C "$API/api/verifications/my-queue" -H "Authorization: Bearer $CHK" | python -c "
import sys,json
q=json.load(sys.stdin)
print('yes' if any(v['id']==$IV_ID for v in q) else 'no')")" "yes"

echo
echo "== 12. the role-map change, and the additive seeder =="
# This database predates the change, so the Administrator role KEEPS the grants it already had --
# the seeder never revokes. That is the documented behaviour (runbook section 4), so it is what is
# asserted here; the new default map is pinned by RoleAndShiftSeparationTests instead.
ME=$($C "$API/api/auth/me" -H "Authorization: Bearer $ADMIN")
haspm() { echo "$ME" | python -c "
import sys,json
d=json.load(sys.stdin)
print('yes' if '$1' in d.get('permissions',[]) else 'no')"; }
# NOT asserted: whether the Administrator role holds Workforce.TrackShift. That is a site
# decision -- the seeder never revokes, so an existing database keeps whatever it had until
# somebody edits the role, and this one was edited on 2026-08-26 to drop it. The new *default*
# is pinned by RoleAndShiftSeparationTests, which reads the map rather than the database.
echo "  NOTE  admin TrackShift is site-configurable; currently: $(haspm Workforce.TrackShift)"
check "Verification.Create was backfilled" "$(haspm Verification.Create)" "yes"
check "Verification.Work was backfilled"   "$(haspm Verification.Work)" "yes"
# The checker's role got Verification.Work but deliberately NOT a shift clock.
MEQ=$($C "$API/api/auth/me" -H "Authorization: Bearer $CHK")
haspq() { echo "$MEQ" | python -c "
import sys,json
d=json.load(sys.stdin)
print('yes' if '$1' in d.get('permissions',[]) else 'no')"; }
check "checker can investigate"            "$(haspq Verification.Work)" "yes"
check "checker is not on the clock"        "$(haspq Workforce.TrackShift)" "no"
check "checker does not execute tasks"     "$(haspq Task.Work)" "no"

echo
echo "== 13. regression: the original pipeline still runs =="
R2=$($C -X POST "$API/api/requests" -H "Authorization: Bearer $REQ" -H 'Content-Type: application/json' \
      -d '{"title":"Regression: labels print the wrong depot","description":"Origin instead of destination.","type":"Bug","requestedUrgency":"Normal","clientName":"E2E Client"}')
R2_ID=$(echo "$R2" | jqv "['id']")
$C -X POST "$API/api/requests/$R2_ID/start-review" -H "Authorization: Bearer $REV" >/dev/null
T2=$($C -X POST "$API/api/requests/$R2_ID/triage" -H "Authorization: Bearer $REV" -H 'Content-Type: application/json' \
      -d '{"outcome":"Approve","approvedPriority":"Normal","acceptanceCriteria":"Destination depot prints"}')
T2_ID=$(echo "$T2" | jqv "['createdTaskId']")
if [ -n "$T2_ID" ] && [ "$T2_ID" != "None" ]; then ok "straight approval still creates a task"; else bad "plain approval regression"; echo "$T2" | head -c 300; echo; fi

echo
echo "-------------------------------------------"
echo "  $pass passed, $fail failed"
[ "$fail" -eq 0 ] || exit 1
