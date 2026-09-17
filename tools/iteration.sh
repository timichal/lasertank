#!/usr/bin/env bash
# One iteration of the autosolver over the whole corpus -- every collection in
# data/levels/, in level order, into one ledger.
#
#   bash tools/iteration.sh              # run it
#   bash tools/iteration.sh status       # the table, free, no solver
#
# **Which iteration this is, and what it is, are not arguments.**  Both are
# `Auto.Iteration` and `Auto.IterationMaxRound` in src/LaserTank.Solver/Auto.cs,
# declared together so that changing the settings without changing the number is
# an edit you have to make on purpose.  Everything else the driver needs it
# already defaults to: --jobs is the core count, --lanes is a quarter of it,
# --out is data/solutions/, and the ledger is data/reports/solutions.jsonl.
# Anything passed here goes through to the solver, which is how you override one
# of them for a single run.
#
# **Resumable, and this is the reason it exists rather than a for-loop in the
# shell history.**  The ledger is the state: a level whose last row says
# *solved*, or says this same iteration already failed it, is skipped.  So an
# interrupted run is restarted by typing the same command, and it does not
# re-run the unsolved remainder -- which at any useful budget is most of the
# corpus and most of the wall clock.  Bumping Auto.Iteration is what re-attempts
# them.
#
# **One process, sequentially, on purpose.**  Two drivers appending to one
# report is the non-atomic-append defect closed item 18 paid for twice: there it
# lost a row and read as a failed probe, here it would lose a level's only
# record of having been tried.  The machine is kept full by --lanes inside the
# one process, not by running thirteen of them.
#
# LT_SOLVE overrides the binary, for the reason campaign.sh has it: a running
# solve holds build/lasertank-solve.exe open, so `dotnet publish -o build`
# cannot replace it.
set -u
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
ledger="$root/data/reports/solutions.jsonl"

if [ "${1:-}" = "status" ]; then
  shift
  exec python "$root/tools/ledger.py" "$ledger" "$@"
fi

exe="${LT_SOLVE:-$root/build/lasertank-solve.exe}"
[ -x "$exe" ] || { echo "no $exe -- run bash src/build.sh" >&2; exit 1; }

# SOLUTIONS.md is regenerated after *every* collection rather than at the end,
# because the end is the one place a pass over 20,914 levels reliably does not
# reach.  It costs a couple of seconds against a collection that costs hours,
# and it means the file in the working tree is never more than one collection
# behind what the ledger knows.  Overwritten in full every time -- it is a view
# of the ledger, so the git diff is what reports a run that came back worse.
render() {
  python "$root/tools/ledger.py" "$ledger" --solutions "$root/SOLUTIONS.md" \
    || echo "could not render SOLUTIONS.md -- the ledger is still the state" >&2
}

start=$(date +%s)
for lvl in "$root"/data/levels/*.lvl; do
  name=$(basename "$lvl" .lvl)
  echo "=== $name ==="
  "$exe" "$lvl" "$@" || {
    # A non-zero exit is Ctrl+C or a preflight failure, and neither is a
    # reason to carry on into the next collection with the same problem.  The
    # render still runs: the levels this collection did solve are banked and
    # the document should say so.
    render
    echo "ABORTED on $name -- rerun the same command, it resumes from the ledger" >&2
    exit 1
  }
  render
done

printf '=== ITERATION DONE in %dh%02dm ===\n' \
  $(( ($(date +%s) - start) / 3600 )) $(( (($(date +%s) - start) % 3600) / 60 ))
echo "    SOLUTIONS.md is the document"
echo "    python tools/ledger.py $ledger --stops   # what to change for the next one"
