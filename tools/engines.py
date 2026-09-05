#!/usr/bin/env python
"""Run the C oracle and the C# core on the same input and compare their traces.

This is the plumbing every two-engine tool needs, factored out of the shell
loops that kept getting rewritten:

    tools/sweep.py    every level in a .lvl, one fixed keystream (empty by default)
    tools/fuzz.py     random keystreams, and shrink whatever diverges

Both engines take the same command line and emit the same trace format on
purpose (`oracle/driver.c` trace_tick vs `src/LaserTank.Cli/TraceWriter.cs`), so
"compare" here is textual, exactly as in tools/difftrace.py -- which this module
imports rather than reimplements, so a verdict from a sweep and a verdict from
`difftrace.py` can never drift apart.

What a caller gets back is a `Div` or None.  `Div.sig` is the *signature*: the
name of the first field that moved, with its values stripped ("T.dir", "PF",
"S.moves", "length", "NOTPORTED").  That is what the fuzzer dedupes findings on
and what its shrinker holds fixed while it reduces a keystream.

Trace size note: `--field`/`--bmf` add 512 hex bytes per tick each, so the
random-keystream loop runs without them -- the default trace already carries
`H=fnv1a(PF),fnv1a(PF2)`, so a playfield divergence still shows up, just as a
hash rather than a cell.  Minimal repros are then re-run *with* both flags, and
that is the artifact a bug report quotes.
"""
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
from collections import namedtuple

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import difftrace                                        # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent.parent
ORACLE = ROOT / "oracle" / "build" / "oracle.exe"
CORE = ROOT / "build" / "lasertank-core.exe"
DIFFTRACE = ROOT / "tools" / "difftrace.py"

LEVEL_REC = 576                     # bytes per level record in a .lvl

# Exit codes both engines use (oracle/driver.c main, LaserTank.Cli/Program.cs).
RC_WIN, RC_NOTWIN, RC_USAGE, RC_NAME, RC_NOTPORTED = 0, 1, 2, 3, 4

Case = namedtuple("Case", "levels level keys")
Run = namedtuple("Run", "rc stdout stderr trace")

# kind: tick | length | result | exit | engine
# sig:  signature used for dedup and for holding a shrink on one bug
Div = namedtuple("Div", "kind sig tick line cosmetic detail")


def count_levels(path):
    """Number of levels in a .lvl file."""
    return os.path.getsize(path) // LEVEL_REC


def find_bash():
    """A POSIX bash that can run oracle/build.sh and src/build.sh.

    On Windows a bare `bash` resolves to `System32\\bash.exe`, which is WSL's
    launcher: it lives in a different filesystem, has no MinGW gcc and no dotnet,
    and fails with an execvpe error rather than anything readable.  Git for
    Windows' bash is the one both build scripts are written for.  $LT_BASH
    overrides, for a machine where neither guess is right.
    """
    env = os.environ.get("LT_BASH")
    if env:
        return env
    found = shutil.which("bash")
    if found and "system32" not in found.lower():
        return found
    for p in (r"C:\Program Files\Git\bin\bash.exe",
              r"C:\Program Files\Git\usr\bin\bash.exe",
              r"C:\Program Files (x86)\Git\bin\bash.exe"):
        if os.path.exists(p):
            return p
    return found or "bash"


def build(script):
    """Run one of the build scripts.  -> (ok, combined output)."""
    p = subprocess.run([find_bash(), script], capture_output=True, text=True,
                       cwd=str(ROOT))
    return p.returncode == 0, (p.stdout + p.stderr).strip()


def require_engines(oracle=ORACLE, core=CORE):
    """Fail loudly and with the build command rather than mysteriously."""
    for exe, how in ((oracle, "bash oracle/build.sh"), (core, "bash src/build.sh")):
        if not pathlib.Path(exe).exists():
            raise SystemExit("engine not built: %s\nrun: %s" % (exe, how))


# --- running ----------------------------------------------------------------


def command(exe, case, trace, field=False, bmf=False, max_ticks=None):
    """The exact argv, so a repro can quote something that actually runs."""
    cmd = [str(exe), "--levels", str(case.levels)]
    if case.level:
        cmd += ["--level", str(case.level)]
    cmd += ["--keys", case.keys, "--trace", str(trace)]
    if field:
        cmd.append("--field")
    if bmf:
        cmd.append("--bmf")
    if max_ticks is not None:
        cmd += ["--max-ticks", str(max_ticks)]
    cmd.append("--quiet")
    return cmd


def run_one(exe, case, trace, field=False, bmf=False, max_ticks=None):
    trace = pathlib.Path(trace)
    if trace.exists():
        trace.unlink()          # a stale trace would read as a live one
    p = subprocess.run(command(exe, case, trace, field, bmf, max_ticks),
                       capture_output=True, text=True, cwd=str(ROOT))
    return Run(p.returncode, p.stdout, p.stderr, trace)


def run_pair(case, ta, tb, field=False, bmf=False, max_ticks=None,
             oracle=ORACLE, core=CORE):
    """Both engines, same input.  A is always the oracle, B always the port."""
    a = run_one(oracle, case, ta, field, bmf, max_ticks)
    b = run_one(core, case, tb, field, bmf, max_ticks)
    return a, b


# --- comparison -------------------------------------------------------------


def read_trace(path):
    """-> (header lines, tick lines, footer lines).  Missing file -> None."""
    path = pathlib.Path(path)
    if not path.exists():
        return None
    head, foot, ticks = [], [], []
    with path.open("r", encoding="latin-1") as fh:
        for raw in fh:
            raw = raw.strip()
            if not raw:
                continue
            if raw.startswith("#"):
                (foot if ticks else head).append(raw)
            else:
                ticks.append(raw)
    return head, ticks, foot


def _sig(name):
    """'T.dir 1 -> 4' -> 'T.dir';  'PF (3 cells)' -> 'PF'."""
    return name.split()[0]


def _result(foot):
    for line in foot:
        if "result=" in line:
            return line.lstrip("# ").strip()
    return None


def compare(a, b, max_cells=12):
    """Two Runs -> the first divergence, or None if the engines agree.

    Order matters: a differing tick line localises better than a length
    mismatch, which localises better than a differing footer, which localises
    better than a bare exit code -- so they are checked in that order and the
    first one that fires is what gets reported.
    """
    ta, tb = read_trace(a.trace), read_trace(b.trace)
    if ta is None or tb is None:
        which = "oracle" if ta is None else "core"
        return Div("engine", "no-trace", None, None, False,
                   "%s wrote no trace (rc=%d)\n%s"
                   % (which, (a if ta is None else b).rc,
                      (a if ta is None else b).stderr.strip()))
    (_, la, fa), (_, lb, fb) = ta, tb
    if not la or not lb:
        return Div("engine", "no-ticks", None, None, False,
                   "no tick lines from %s" % ("oracle" if not la else "core"))

    for i in range(min(len(la), len(lb))):
        if la[i] == lb[i]:
            continue
        fields = difftrace.compare_tick(la[i], lb[i], max_cells)
        names = [nm for _, nms, _ in fields for nm in nms]
        keys = [k for k, _, _ in fields]
        detail = ["  A " + difftrace.elide(la[i]), "  B " + difftrace.elide(lb[i])]
        detail += ["    " + ln for _, _, ds in fields for ln in ds]
        return Div("tick", _sig(names[0]),
                   difftrace.split(la[i]).get("t"), i + 1,
                   set(keys) <= set(difftrace.COSMETIC),
                   "first field: %s   [%s]\nalso: %s\n%s"
                   % (names[0], difftrace.WHAT.get(keys[0], "?"),
                      ", ".join(names[1:]) or "-", "\n".join(detail)))

    if len(la) != len(lb):
        # A partly-ported engine stops here: the footer says NOTPORTED and
        # stderr names the function.  That is a finding, not an obstacle.
        stop = _result(fb) or ""
        sig = "NOTPORTED" if "NOTPORTED" in stop else "length"
        return Div("length", sig, None, min(len(la), len(lb)), False,
                   "identical for %d ticks, then %s stops; %s has %d more\n"
                   "  A %s\n  B %s\n%s"
                   % (min(len(la), len(lb)),
                      "B (core)" if len(lb) < len(la) else "A (oracle)",
                      "A" if len(la) > len(lb) else "B",
                      abs(len(la) - len(lb)),
                      _result(fa), _result(fb), b.stderr.strip()))

    ra, rb = _result(fa), _result(fb)
    if ra != rb:
        return Div("result", "result", None, len(la), False,
                   "every tick identical, footers differ\n  A %s\n  B %s" % (ra, rb))

    if a.rc != b.rc:
        return Div("exit", "exit-code", None, len(la), False,
                   "traces identical, exit codes differ: oracle %d, core %d\n%s"
                   % (a.rc, b.rc, b.stderr.strip()))
    return None


_FOOT_RE = re.compile(r"result=(?P<result>\w+)\s+ticks=(?P<ticks>\d+)\s+"
                      r"moves=(?P<moves>\d+)\s+shots=(?P<shots>\d+)\s+"
                      r"keys_used=(?P<used>\d+)/(?P<offered>\d+)")


def outcome(path):
    """The trace footer as a dict, or None.

    How far a run actually got, which is not the same as how many keys it was
    given: random keys drown or shoot the tank early, so a fuzz campaign's
    honest coverage number is keys *consumed*, not keys generated.
    """
    foot = read_trace(path)
    if not foot:
        return None
    m = None
    for line in foot[2]:
        m = _FOOT_RE.search(line) or m
    if not m:
        return None
    d = m.groupdict()
    for k in ("ticks", "moves", "shots", "used", "offered"):
        d[k] = int(d[k])
    return d


def difftrace_report(ta, tb, extra=()):
    """Shell out to difftrace.py -- the report a bug reader would produce."""
    cmd = [sys.executable, str(DIFFTRACE), str(ta), str(tb)] + list(extra)
    p = subprocess.run(cmd, capture_output=True, text=True, cwd=str(ROOT))
    return " ".join(cmd), (p.stdout + p.stderr).rstrip()


# --- scratch space ----------------------------------------------------------


class Scratch:
    """A pair of reusable trace paths.  One per worker thread."""

    def __init__(self, tag="lt"):
        self.dir = pathlib.Path(tempfile.mkdtemp(prefix="%s-" % tag))
        self.a = self.dir / "a.trace"
        self.b = self.dir / "b.trace"

    def close(self):
        for p in (self.a, self.b):
            try:
                p.unlink()
            except OSError:
                pass
        try:
            self.dir.rmdir()
        except OSError:
            pass

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()
