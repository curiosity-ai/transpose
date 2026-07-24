#!/usr/bin/env bash
# Interleaved A/B timing of two `tps` binaries on one project.
#
# The benchmark host is a shared VM whose throughput drifts by tens of percent over minutes, so
# measuring A for a while and then B is worthless — the drift swamps the effect. This alternates
# A,B,A,B,… so both see the same conditions, and reports each one's median.
#
#   ab.sh <tps-A> <tps-B> <project.csproj> [rounds] [configuration]
set -uo pipefail

A=${1:?usage: ab.sh <tps-A> <tps-B> <project.csproj> [rounds] [config]}
B=${2:?}
PROJ=${3:?}
ROUNDS=${4:-3}
CFG=${5:-Debug}
PROJDIR=$(dirname "$PROJ")

# Wipe the project's and its project references' outputs: tps skips an up-to-date dependency, so
# without this the second round would measure a different build.
clean() {
  rm -rf "$PROJDIR/bin" "$PROJDIR/obj"
  grep -o 'ProjectReference Include="[^"]*"' "$PROJ" 2>/dev/null | sed 's/.*Include="//; s/"$//' | tr '\\' '/' | while read -r ref; do
    d=$(dirname "$PROJDIR/$ref"); rm -rf "$d/bin" "$d/obj"
  done
}

run() { # <tps> -> prints wall ms
  clean
  local t0 t1
  t0=$(date +%s%N)
  "$1" --project "$PROJ" -c "$CFG" -q >/dev/null 2>&1
  t1=$(date +%s%N)
  echo $(( (t1 - t0) / 1000000 ))
}

declare -a AT BT
for i in $(seq 1 "$ROUNDS"); do
  a=$(run "$A"); AT+=("$a")
  b=$(run "$B"); BT+=("$b")
  printf 'round %d:  A %6d ms   B %6d ms   (B/A %.3f)\n' "$i" "$a" "$b" "$(echo "scale=4; $b/$a" | bc)"
done

median() { printf '%s\n' "$@" | sort -n | awk '{v[NR]=$1} END{print (NR%2)?v[(NR+1)/2]:int((v[NR/2]+v[NR/2+1])/2)}'; }
MA=$(median "${AT[@]}"); MB=$(median "${BT[@]}")
printf '\nmedian:   A %6d ms   B %6d ms   ->  B is %.1f%% %s\n' \
  "$MA" "$MB" "$(echo "scale=4; d=($MB-$MA)*100/$MA; if (d<0) -d else d" | bc)" \
  "$( [ "$MB" -lt "$MA" ] && echo faster || echo slower )"
