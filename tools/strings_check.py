#!/usr/bin/env python
"""The i18n gate: eleven catalogues, and the rule that keeps them honest.

`data/language/<code>.json` is this port's own UI text -- one flat keyed
catalogue per language, keys named after the widget that draws them.  It
replaced the conversion of the original's ten `Language.dat` files, whose 155
slots described a menu bar, a nine-button control panel and sixteen dialogs this
port does not have; `src/LaserTank.Core/Strings.cs` records why, and
`tools/convert_language.py --check` still decodes the 2007 files for anyone who
wants to look at them.

next-steps item 1 asked for an audit -- *a key is read by a widget or it goes* --
and an audit is a thing somebody has to remember to run again.  So it is a check
instead, and it runs **both ways**:

  keys       Every file has the same key set, none of them empty, every `{0}`
             in English present in every translation, and the handful of labels
             that are placed by *character column* rather than measured are
             within the width the table leaves them.  That last one is the only
             place in this interface where a translation has a hard limit, and
             it is a limit because those cells are positioned by the original's
             own printf widths -- a long word does not reflow the table, it
             lands on the next column's numbers.

  source     Every key in `en.json` appears in the C# sources, and every lookup
             in the C# sources names a key `en.json` has.  A key nobody draws
             fails; a label with no string fails.  Comments and doc comments are
             stripped first, so a key that survives only because it is mentioned
             in a comment counts as dead -- which is what it is.

  godot      The game's own `--check-strings`, which resolves the catalogue
             through `BoardView.Strings`, the property every label on screen
             goes through, and is diffed against this tool's own read of the
             JSON.  Two readers agreeing proves the data; only the game proves
             that what the screen shows is that data.

  ini        `--check-strings-ini`: the picker's choice survives a restart, an
             unknown code degrades to the base language, the per-key fallback
             fills a blank and an absent key without clobbering a translated
             one, and a foreign INI key comes back untouched.  Runs in a
             temporary directory, because an instrument must not write the
             player's state.

    python tools/strings_check.py                 # all four, ~20 s
    python tools/strings_check.py --no-godot      # the first two, instant
    python tools/strings_check.py --lang cs       # one language, verbosely

Needs Godot for the last two only; without it they SKIP loudly.  Nothing here
touches build/, so it is safe beside a live solve.
"""
import argparse
import json
import pathlib
import re
import subprocess
import sys
import tempfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from engines import ROOT                                # noqa: E402
from atlas_check import find_godot                      # noqa: E402

LANGDIR = ROOT / "data" / "language"
GAME = ROOT / "src" / "LaserTank.Game"
SOURCES = [ROOT / "src" / "LaserTank.Game", ROOT / "src" / "LaserTank.Core"]

BASE = "en"

#: A key: a lowercase head, then at least one dotted segment.  The grammar is
#: what lets the source scan tell a key from a file name without a table.
KEY = re.compile(r"^[a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9_-]+)+$")

#: `{0}`, `{1}` ... -- Strings.F's whole placeholder syntax.
SLOT = re.compile(r"\{(\d+)\}")

#: The labels that are drawn at a character column rather than measured, with
#: the width the column leaves them.  LevelList.Cells is where the numbers come
#: from: the name cell starts at 9 and the author at 40, so `levels.colName`
#: has 31 columns and is capped one short of it; and so on down.  CollectionList
#: heads its three at 0 / 25 / 37 over rows whose own fields are 24 wide and
#: `%5d/%-5d`.
WIDTHS = {
    "levels.colNumber": 4,
    "levels.colName": 30,
    "levels.colAuthor": 20,
    "levels.colMoves": 5,
    "levels.colShots": 5,
    "levels.colWho": 5,
    "levels.groupBest": 18,
    "levels.groupYours": 16,
    "coll.colName": 24,
    "coll.colSolved": 11,
    "coll.colWhere": 20,
}


# --------------------------------------------------------------------------
# reading the catalogues
# --------------------------------------------------------------------------
def load():
    """Every `<code>.json` under data/language/, by code."""
    docs = {}
    for path in sorted(LANGDIR.glob("*.json")):
        docs[path.stem] = json.loads(path.read_text(encoding="utf-8"))
    return docs


def check_structure(docs):
    fails = []
    if BASE not in docs:
        return ["no %s.json -- there is no base language to fall back to" % BASE]
    for code in sorted(docs):
        doc = docs[code]
        for field in ("code", "name", "credit", "strings"):
            if field not in doc:
                fails.append("%s: no %r field" % (code, field))
        if doc.get("code") != code:
            fails.append("%s: `code` is %r, not the file name"
                         % (code, doc.get("code")))
        if not isinstance(doc.get("strings"), dict):
            fails.append("%s: `strings` is not an object" % code)
    return fails


def check_keys(docs, verbose=False):
    """One key set, no blanks, matching placeholders, capped widths."""
    fails = []
    base = docs[BASE]["strings"]

    for key, val in sorted(base.items()):
        if not KEY.match(key):
            fails.append("%s: %r is not a key -- want `head.segment`"
                         % (BASE, key))

    for code in sorted(docs):
        if code == BASE:
            continue
        got = docs[code]["strings"]
        missing = sorted(set(base) - set(got))
        extra = sorted(set(got) - set(base))
        for k in missing[:6]:
            fails.append("%s: missing %s" % (code, k))
        if len(missing) > 6:
            fails.append("%s: ... and %d more missing" % (code, len(missing) - 6))
        for k in extra[:6]:
            fails.append("%s: %s is in no other file" % (code, k))
        if len(extra) > 6:
            fails.append("%s: ... and %d more unknown" % (code, len(extra) - 6))

    for code in sorted(docs):
        strings = docs[code]["strings"]
        for key in sorted(strings):
            val = strings[key]
            if not val.strip():
                fails.append("%s: %s is empty -- a blank falls back and hides"
                             % (code, key))
            if key in base:
                want = sorted(set(SLOT.findall(base[key])))
                have = sorted(set(SLOT.findall(val)))
                if want != have:
                    fails.append("%s: %s has placeholders %s, %s has %s"
                                 % (code, key, have or "none", BASE,
                                    want or "none"))
            cap = WIDTHS.get(key)
            if cap is not None and len(val) > cap:
                fails.append("%s: %s is %d characters and the column leaves %d"
                             % (code, key, len(val), cap))
        if verbose:
            print("  %-7s %d keys" % (code, len(strings)))
    return fails


# --------------------------------------------------------------------------
# reading the source
# --------------------------------------------------------------------------
def strip_comments(src):
    """C# with its comments blanked out and its string literals intact.

    A character-by-character walk rather than a regex, because the two cases
    that matter are exactly the ones a regex gets wrong: `"out/recordings/"` is
    not a comment, and `/// `quit.title`` is.  Blanked rather than removed so
    the line numbers in a failure still point at the source.
    """
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            # A verbatim string (@"...") doubles its quotes; a normal one
            # escapes them.  Both end at a quote that is not escaped.
            verbatim = i > 0 and src[i - 1] == "@"
            out.append(c)
            i += 1
            while i < n:
                if src[i] == "\\" and not verbatim:
                    out.append(src[i:i + 2])
                    i += 2
                    continue
                out.append(src[i])
                if src[i] == '"':
                    if verbatim and i + 1 < n and src[i + 1] == '"':
                        out.append(src[i + 1])
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            continue
        if c == "'":
            out.append(c)
            i += 1
            while i < n:
                if src[i] == "\\":
                    out.append(src[i:i + 2])
                    i += 2
                    continue
                out.append(src[i])
                i += 1
                if out[-1] == "'":
                    break
            continue
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            while i < n and src[i] != "\n":
                out.append(" ")
                i += 1
            continue
        if c == "/" and i + 1 < n and src[i + 1] == "*":
            while i < n and not (src[i] == "*" and i + 1 < n and src[i + 1] == "/"):
                out.append("\n" if src[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


LITERAL = re.compile(r'"((?:[^"\\\n]|\\.)*)"')
LOOKUP = re.compile(r'\[\s*"((?:[^"\\\n]|\\.)*)"\s*\]'
                    r'|\.F\(\s*"((?:[^"\\\n]|\\.)*)"')


def scan_sources():
    """-> (looked_up, mentioned), each {key: [file:line, ...]}.

    **Two sets, and the asymmetry is deliberate.**  A *lookup* is the indexer or
    `.F(`, and every one of those has to name a key that exists -- that is the
    check that catches a typo in a draw call.  A *mention* is any string literal
    shaped like a key, anywhere, which is what proves a key is alive: several
    tables here carry their keys as plain data (the F1 overlay's bindings, the
    editor's object names, CollectionNotes' stems) and a lookup-only scan would
    call every one of them dead.  Read the other way round it would be wrong --
    a mention is not evidence a key resolves -- which is why it only ever
    *keeps* a key and never demands one.
    """
    looked, mentioned = {}, {}
    for root in SOURCES:
        for path in sorted(root.rglob("*.cs")):
            if "/obj/" in path.as_posix() or "/bin/" in path.as_posix():
                continue
            rel = path.relative_to(ROOT).as_posix()
            src = strip_comments(path.read_text(encoding="utf-8"))
            for m in LOOKUP.finditer(src):
                key = m.group(1) or m.group(2)
                if KEY.match(key):
                    where = "%s:%d" % (rel, src.count("\n", 0, m.start()) + 1)
                    looked.setdefault(key, []).append(where)
            for m in LITERAL.finditer(src):
                key = m.group(1)
                if KEY.match(key):
                    where = "%s:%d" % (rel, src.count("\n", 0, m.start()) + 1)
                    mentioned.setdefault(key, []).append(where)
    return looked, mentioned


def check_source(docs, verbose=False):
    fails = []
    base = docs[BASE]["strings"]
    looked, mentioned = scan_sources()

    for key in sorted(looked):
        if key not in base:
            fails.append("%s is looked up at %s and is in no catalogue"
                         % (key, looked[key][0]))

    for key in sorted(base):
        if key not in mentioned:
            fails.append("%s is in %s.json and no source file names it"
                         % (key, BASE))

    if verbose:
        print("  %d keys, %d lookups, %d named in source"
              % (len(base), sum(len(v) for v in looked.values()), len(mentioned)))
    return fails


# --------------------------------------------------------------------------
# the game's own reading of the same files
# --------------------------------------------------------------------------
def escape(s):
    """Step6Check.Escape, rebuilt."""
    return (s.replace("\\", "\\\\").replace("\t", "\\t")
             .replace("\r", "\\r").replace("\n", "\\n"))


def expected_dump(docs, code):
    """What `--check-strings CODE` has to print, fallback applied."""
    doc = docs.get(code, docs[BASE])
    merged = dict(docs[BASE]["strings"])
    for k, v in doc["strings"].items():
        if v:
            merged[k] = v
    lines = ["code\t" + escape(doc["code"]),
             "name\t" + escape(doc["name"]),
             "credit\t" + escape(doc.get("credit", ""))]
    for key in sorted(merged):
        lines.append("S\t%s\t%s" % (key, escape(merged[key])))
    return lines


def run_godot(godot, args, timeout=300):
    """Run the Godot project and decode its output as UTF-8.

    Explicitly, and not with `text=True`, which is what most gates in tools/
    use.  `text=True` decodes with the *locale* codec -- cp1252 on this machine
    -- and this gate's output is not ASCII.  The failure is worse than mojibake:
    the reader thread raises UnicodeDecodeError on the first undefined byte,
    subprocess swallows it in the thread, and `p.stdout` comes back **empty**,
    so a working language looks like a crashed game.
    """
    p = subprocess.run([godot, "--headless", "--path", str(GAME), "--"] + args,
                       capture_output=True, cwd=str(ROOT), timeout=timeout)
    out = (p.stdout or b"") + (p.stderr or b"")
    return out.decode("utf-8", errors="replace"), p.returncode


def check_godot(godot, docs, codes):
    fails = []
    for code in codes:
        out, rc = run_godot(godot, ["--check-strings", code])
        if rc != 0:
            fails.append("%s: --check-strings exited %d\n%s"
                         % (code, rc, out[-800:]))
            continue
        got = [l for l in out.replace("\r\n", "\n").split("\n")
               if l.startswith(("code\t", "name\t", "credit\t", "S\t"))]
        want = expected_dump(docs, code)
        if got == want:
            continue
        for i in range(max(len(got), len(want))):
            g = got[i] if i < len(got) else "<end>"
            w = want[i] if i < len(want) else "<end>"
            if g != w:
                fails.append("%s: line %d\n      game %r\n      json %r"
                             % (code, i + 1, g, w))
                break
    return fails


def check_ini(godot):
    fails = []
    checks = []
    with tempfile.TemporaryDirectory(prefix="lt-strings-") as d:
        out, rc = run_godot(godot, ["--check-strings-ini", d])
        if rc != 0:
            fails.append("--check-strings-ini exited %d\n%s" % (rc, out[-1500:]))
            return fails, 0
        checks = [l for l in out.replace("\r\n", "\n").split("\n")
                  if l.startswith("ini\t")]
        if not checks:
            fails.append("--check-strings-ini printed no `ini` lines")
        for line in checks:
            parts = line.split("\t")
            if "FAIL" in parts[-1]:
                fails.append("INI: %s" % " ".join(parts[1:]))
    return fails, len(checks)


# --------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lang", action="append",
                    help="only this language code (repeatable)")
    ap.add_argument("--no-godot", action="store_true",
                    help="skip the game half")
    args = ap.parse_args()

    docs = load()
    if not docs:
        print("strings_check: nothing under %s" % LANGDIR.relative_to(ROOT))
        return 2
    codes = args.lang or sorted(docs)
    for c in codes:
        if c not in docs:
            print("strings_check: no such language %r (have: %s)"
                  % (c, " ".join(sorted(docs))))
            return 2

    ok = True
    verbose = bool(args.lang)

    print("keys -- one key set across %d files, no blanks, matching "
          "placeholders, capped columns:" % len(docs))
    fails = check_structure(docs) or check_keys(docs, verbose)
    if fails:
        ok = False
        for f in fails[:14]:
            print("  FAIL: %s" % f)
        if len(fails) > 14:
            print("  ... and %d more" % (len(fails) - 14))
    else:
        print("  ok   %d keys x %d languages" % (len(docs[BASE]["strings"]),
                                                 len(docs)))

    print("source -- every key drawn, every lookup answered:")
    fails = check_source(docs, verbose)
    if fails:
        ok = False
        for f in fails[:14]:
            print("  FAIL: %s" % f)
        if len(fails) > 14:
            print("  ... and %d more" % (len(fails) - 14))
    else:
        print("  ok   no dead key, no unanswered lookup")

    godot = None if args.no_godot else find_godot()
    if args.no_godot:
        print("godot -- SKIPPED (--no-godot)")
    elif godot is None:
        print("godot -- SKIPPED: no Godot binary found (set $LT_GODOT)")
        print("ini   -- SKIPPED for the same reason")
    else:
        print("godot -- the game resolving the same files:")
        fails = check_godot(godot, docs, codes)
        if fails:
            ok = False
            for f in fails[:8]:
                print("  FAIL: %s" % f)
        else:
            print("  ok   %d language(s) agree with the JSON" % len(codes))

        print("ini -- the choice survives a restart, and the fallback works:")
        fails, n = check_ini(godot)
        if fails:
            ok = False
            for f in fails[:8]:
                print("  FAIL: %s" % f)
        else:
            print("  ok   %d checks" % n)

    print("strings_check: %s" % ("OK" if ok else "FAILED"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
