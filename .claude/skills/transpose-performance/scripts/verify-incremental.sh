#!/usr/bin/env bash
# Correctness gate for the incremental cache (`tps --incremental`): for each shape of edit, the site an
# incremental build produces must be byte-identical to the one a from-scratch build of the same sources
# produces. An incremental build that is wrong produces plausible output rather than failing, so this
# is the check that has to pass before the cache can be trusted — see TODO.incremental.md.
#
#   TPS=<tps> [REPO=<tesserae checkout>] verify-incremental.sh
#
# The line numbers the edits target are specific to the tesserae corpus at the time of writing; if an
# edit stops applying (the script says so), re-point it at any method body / declaration.
set -uo pipefail

TPS=${TPS:?}
REPO=${REPO:-$(dirname "$0")/../../../../benchmarks/tesserae}   # a sibling checkout works too: REPO=<path>
APP=$REPO/Tesserae.Tests/Tesserae.Tests.csproj
LIB=$REPO/Tesserae/src/Components/Toast.cs
APPSRC=$REPO/Tesserae.Tests/src/App.cs
SITE=$REPO/Tesserae.Tests/bin/Debug/netstandard2.0/tps
CMP=$(dirname "$0")/compare-site.sh
fail=0

clean() { rm -rf "$REPO"/Tesserae/bin "$REPO"/Tesserae/obj "$REPO"/Tesserae.Tests/bin "$REPO"/Tesserae.Tests/obj; }
reset_sources() { (cd "$REPO" && git checkout -- Tesserae/src/Components/Toast.cs Tesserae.Tests/src/App.cs); }

# check <name> <edit-command>
check() {
  local name=$1; shift
  echo "───────── $name"
  reset_sources; clean
  "$TPS" --project "$APP" -c Debug --incremental >/dev/null 2>&1 || { echo "  cold build FAILED"; fail=1; return; }
  eval "$@"
  "$TPS" --project "$APP" -c Debug --incremental > /tmp/v-inc.log 2>&1 || { echo "  incremental FAILED"; tail -20 /tmp/v-inc.log; fail=1; return; }
  grep -E "cache:" /tmp/v-inc.log | sed 's/^/  /'
  rm -rf /tmp/v-site-inc; cp -r "$SITE" /tmp/v-site-inc
  clean
  "$TPS" --project "$APP" -c Debug --no-incremental >/dev/null 2>&1 || { echo "  scratch build FAILED"; fail=1; return; }
  rm -rf /tmp/v-site-full; cp -r "$SITE" /tmp/v-site-full
  if bash "$CMP" /tmp/v-site-inc /tmp/v-site-full | tail -1 | grep -q IDENTICAL; then
    echo "  OK — identical to a from-scratch build"
  else
    echo "  MISMATCH"; bash "$CMP" /tmp/v-site-inc /tmp/v-site-full | head -20; fail=1
  fi
}

check "app: method body edited" \
  "sed -i '130s/You clicked on the icon/You clicked V1/' $APPSRC"

check "library: method body edited" \
  "sed -i '411s/tss-toast-/V2-/' $LIB"

check "library + app: both bodies edited" \
  "sed -i '411s/tss-toast-/V3-/' $LIB; sed -i '130s/You clicked on the icon/You clicked V3/' $APPSRC"

check "library: new public method (declaration change)" \
  "sed -i '408a\\        public Toast AddedByVerify(int n) { return this; }' $LIB"

check "app: statement added to a body" \
  "sed -i '43a\\            var verifyLocal = allSidebarItems.Count;' $APPSRC"

check "library: field initializer changed (declaration surface)" \
  "sed -i '26s/= true;/= false;/' $LIB"

check "library: whole file untouched, only whitespace in a body" \
  "sed -i '411s/^            /                /' $LIB"

reset_sources
echo "───────── result"
[ $fail -eq 0 ] && echo "ALL SCENARIOS IDENTICAL" || echo "FAILURES PRESENT"
exit $fail
