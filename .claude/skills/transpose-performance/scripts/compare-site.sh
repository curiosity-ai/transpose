#!/usr/bin/env bash
# Byte-compare two emitted site trees — the correctness gate for any compiler optimization.
#
# `$e<hex>` enumerator temporaries are normalised away so a comparison against a compiler older than
# the reproducible-naming fix still works (they used to be derived from a reference hash code and so
# differed on every run). Everything else must match exactly.
#
#   compare-site.sh <dir-A> <dir-B>
set -uo pipefail

A=${1:?usage: compare-site.sh <dir-A> <dir-B>}
B=${2:?}
fail=0

norm() { sed -E 's/\$e[0-9a-f]+/$eN/g' "$1"; }

if ! diff <(cd "$A" && find . -type f | sort) <(cd "$B" && find . -type f | sort); then
  echo "FILE SET DIFFERS"
  fail=1
fi

while read -r f; do
  if [ ! -f "$B/$f" ]; then continue; fi          # already reported by the file-set diff
  norm "$A/$f" > /tmp/.cmp-a 2>/dev/null || cp "$A/$f" /tmp/.cmp-a
  norm "$B/$f" > /tmp/.cmp-b 2>/dev/null || cp "$B/$f" /tmp/.cmp-b
  if ! cmp -s /tmp/.cmp-a /tmp/.cmp-b; then
    echo "DIFFERS: $f"
    diff /tmp/.cmp-a /tmp/.cmp-b | head -12
    fail=1
  fi
done < <(cd "$A" && find . -type f | sort)

rm -f /tmp/.cmp-a /tmp/.cmp-b
[ $fail -eq 0 ] && echo "SITE OUTPUT IDENTICAL"
exit $fail
