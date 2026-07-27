#!/usr/bin/env bash
# Correctness gate for the incremental cache (`tps --incremental`): for each shape of edit, the site an
# incremental build produces must be byte-identical to the one a from-scratch build of the same sources
# produces. An incremental build that is wrong produces plausible output rather than failing, so this
# is the check that has to pass before the cache can be trusted — see TODO.incremental.md.
#
#   TPS=<tps> [REPO=<tesserae checkout>] verify-incremental.sh
#
# Run by .devops/benchmark-transpose-compiler.yml, where it *fails* the build: unlike a performance
# comparison this is a deterministic question, so there is no noise to tolerate.
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

# check <name> <edit-command> [setup-command]
# The setup command, when given, is applied *before* the cold build — for scenarios where the edit has
# to change something that already existed (e.g. the value of a const the app already consumes).
check() {
  local name=$1 edit=$2 setup=${3:-}
  echo "───────── $name"
  reset_sources; clean
  [ -n "$setup" ] && eval "$setup"
  "$TPS" --project "$APP" -c Debug --incremental >/dev/null 2>&1 || { echo "  cold build FAILED"; fail=1; return; }
  eval "$edit"
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

# The sharpest test of the metadata fingerprint (BuildCache.ReferenceMetadataFingerprint), which is what
# lets a consumer reuse its compilation when a referenced library's *metadata* is unchanged: a const the
# app consumes. Constants live in metadata, so changing one has to invalidate the app — and it does.
# (Worth knowing while reading this: Transpose does not fold a cross-assembly const into the consumer's
# bundle, it emits `tss.Toast.VerifyConstProbe` and lets the runtime read it, so the value itself only
# ever lives in the library's bundle. The comparison holds either way — which is the point of comparing
# bytes rather than reasoning about them.)
check "library: const the app consumes changes value" \
  "sed -i 's/VerifyConstProbe = \"AAA\"/VerifyConstProbe = \"BBB\"/' $LIB" \
  "sed -i '20a\\        public const string VerifyConstProbe = \"AAA\";' $LIB;
   sed -i '21a\\            System.Console.WriteLine(Tesserae.Toast.VerifyConstProbe);' $APPSRC"

reset_sources
echo "───────── result"
[ $fail -eq 0 ] && echo "ALL SCENARIOS IDENTICAL" || echo "FAILURES PRESENT"
exit $fail
