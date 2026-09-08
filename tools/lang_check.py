#!/usr/bin/env python
"""Phase 5 step 6's gate: the ten translations, tied back to the 2007 bytes.

Step 6 is the one step of this phase that does not go through the C oracle, and
the reason is not that the oracle is inconvenient -- it is that the oracle would
answer the wrong question.  `LANGUAGE.C` reads a *positional* file: 240 lines in
six fixed sections, each translation in whichever 8-bit codepage its author's
Windows used, with a `while(!feof)` loop that applies the last line twice.  The
port does not read that file (Language.cs says why: nothing about the rules
depends on a UI string, and the population of such files closed in 2007), so
diffing the port against a transliteration of that loop would only prove the two
agree about a format neither of them ships.

What can be proven, and is what this checks, is that **nothing was lost or moved
in the conversion.**  The frozen `original/src/Setups/*/Language/Language.dat`
files are the ground truth; `data/language/*.json` is the derived artifact; and
the interesting direction is backwards.

  roundtrip  Rebuild every one of the 240 source lines out of the JSON -- undo
             the escape conversion, re-attach the accelerator hint after a tab,
             re-encode to the codepage the file was written in -- and compare
             **bytes** against the original line.  Not "loads and looks right":
             byte identity, all ten files, every line.  This is the check that
             would catch a mangled accent, a shifted section, a dropped string
             or a mis-keyed slot, and it is the reason the conversion can be
             trusted without a transliteration standing behind it.

             The nine lines that address a menu separator cannot be rebuilt --
             the JSON keeps no text for a separator, because the original never
             applies one (its `fType` is not `MFT_STRING`, which is why all ten
             files carry the untranslated word `SEPARATOR` there).  Those are
             asserted to be exactly the lines the original ignores, and counted.

  structure  The key set is the same in all ten files and complete against the
             frozen `LT32L_US.H` (9 buttons, 48 texts with the header's own two
             gaps, 96 dialog slots); the menu trees match the frozen
             `lt32l_us.inc` node for node, including every command id and which
             nodes are popups and separators; the sizes are the header's
             (SIZE_MMENU 49, SIZE_EMENU 24) rather than whatever the files
             happen to hold.

  csharp     `lasertank-core --lang-dump CODE`, for all ten, against the same
             dump rebuilt here from the JSON -- fallback and all.  The sprite
             sheet pattern: two languages decoding the same bytes and agreeing.
             The fallback is the only logic in the loading path, so the check
             asserts it is actually exercised (six of the ten files are partial)
             rather than trusting a green run that never reached it.

  godot      The game's own `--check-lang`, which resolves its UI strings
             through the *same* Language object the picker switches, plus the
             `[DATA] Language` key surviving a restart.  Third implementation,
             for step 4's reason: two loaders agreeing proves the data, and only
             the game proves that what the screen shows is that data.

    python tools/lang_check.py                    # all four, ~25 s
    python tools/lang_check.py --no-godot         # the first three, ~5 s
    python tools/lang_check.py --lang Fr          # one language, verbosely

Needs Godot for the last half only; without it that half SKIPs loudly.  Nothing
here touches build/, so it is safe beside a live solve.

Exit: 0 clean, 1 a mismatch, 2 environment.
"""
import argparse
import json
import pathlib
import subprocess
import sys
import tempfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import ROOT                                # noqa: E402
from atlas_check import find_godot                      # noqa: E402
import convert_language as conv                         # noqa: E402

LANGDIR = ROOT / "data" / "language"
GAME = ROOT / "src" / "LaserTank.Game"


# --------------------------------------------------------------------------
# the inverse of the conversion
# --------------------------------------------------------------------------
def unconvert_escapes(s, tabs):
    """The inverse of `convert_language.convert_escapes`.

    A newline becomes `\\n`; a backslash is left alone, which is what makes
    `Language\\LaserTank.hlp` come back as itself.  Written out here rather than
    imported so the round trip is two implementations meeting in the middle
    instead of one function agreeing with itself.

    `tabs` is the one place the inverse is not symmetric, and it is a fact about
    the corpus rather than a convention.  `ConvertTabChar` turns `\\t` into a
    tab on *every* line, so a tab in the JSON could have come from either an
    escape or a real 0x09 byte -- and which one it was is not recoverable.  It
    does not have to be: in the 73 menu lines a tab is always the accelerator
    separator and never literal, and in the other 167 it is always literal and
    never an escape.  `check_tab_policy` asserts both halves over all ten files,
    so this stays a measurement and not an assumption.
    """
    out = []
    for c in s:
        if c == "\t" and tabs:
            out.append("\\t")
        elif c == "\n":
            out.append("\\n")
        else:
            out.append(c)
    return "".join(out)


def check_tab_policy():
    """What licenses `unconvert_escapes`'s asymmetry: measure it, all ten files.

    One line in the corpus is why this exists -- Du's about message line 233
    starts with a real 0x09 byte -- and treating that tab as an escape is
    exactly the kind of silent mangling the round trip is here to catch.  It
    did catch it.
    """
    fails = []
    for code in conv.ENCODINGS:
        lines, _enc = source_lines(code)
        for i, l in enumerate(lines):
            if i < conv.START_BUTTON:
                if "\t" in l:
                    fails.append("%s line %d: a menu line holds a literal tab: %r"
                                 % (code, i, l))
            elif "\\t" in l:
                fails.append("%s line %d: a non-menu line holds an escaped \\t: %r"
                             % (code, i, l))
    return fails


def source_lines(code):
    """The original .dat's non-comment lines, and their raw bytes."""
    path = conv.SETUPS / code / "Language" / "Language.dat"
    raw = path.read_bytes()
    enc = conv.ENCODINGS[code]
    text = raw.decode(enc)
    return [l for l in text.split("\r\n") if l and not l.startswith("#")], enc


def resolve(items, addr):
    """Walk the JSON menu tree to the node at `addr`, or None."""
    node, cur = None, items
    for i, n in enumerate(addr):
        if n < 0 or n >= len(cur):
            return None
        node = cur[n]
        if i + 1 < len(addr):
            cur = node.get("items")
            if cur is None:
                return None
    return node


def address_of(line):
    """The `NN,NN,NN` triple a menu line carries, as `ChangeMenuText` reads it."""
    n0 = conv.decode_menu_int(line[0:2])
    n1 = conv.decode_menu_int(line[3:5])
    n2 = conv.decode_menu_int(line[6:8])
    if n2 >= 0:
        return (n0, n1, n2)
    if n1 >= 0:
        return (n0, n1)
    return (n0,)


def check_roundtrip(doc, code, verbose=False):
    """Rebuild every source line out of the JSON and compare bytes."""
    lines, enc = source_lines(code)
    _buttons, texts, dialogs = conv.slot_names()
    keys = ([f"ButText{i}" for i in range(1, conv.SIZE_BUTTON + 1)]
            + texts + dialogs)

    fails, rebuilt, skipped = [], 0, 0

    for i, src in enumerate(lines):
        if i >= conv.SIZE_ALL:
            fails.append((i, "past SIZE_ALL=%d; the original ignores it" % conv.SIZE_ALL,
                          src, None))
            continue

        # --- sections 1 and 2: the menus, addressed rather than assigned
        if i < conv.START_BUTTON:
            which = "main" if i < conv.START_EMENU else "editor"
            tree = doc["menu"][which]
            if len(src) <= 9:
                skipped += 1                    # the original's own length guard
                continue
            node = resolve(tree, address_of(src))
            if node is None:
                fails.append((i, "address %r resolves to nothing" % (address_of(src),),
                              src, None))
                continue
            if node.get("separator"):
                # Unapplied by the original too.  Assert that rather than
                # assume it: a separator line whose payload is a real
                # translated string would mean the tree is misaligned.
                if src[9:] != "SEPARATOR":
                    fails.append((i, "separator line carries %r" % src[9:], src, None))
                skipped += 1
                continue
            got = node["text"]
            if node.get("accel") is not None:
                got += "\t" + node["accel"]
            back = src[:9] + unconvert_escapes(got, tabs=True)
            rebuilt += 1
        # --- sections 3-6: LANGText[], straight assignment
        else:
            slot = i - conv.START_BUTTON
            if slot < conv.OFFSET_ABOUTMSG:
                key = keys[slot]
                section = ("buttons" if slot < conv.OFFSET_TEXT
                           else "text" if slot < conv.OFFSET_DIALOGS else "dialogs")
                if key not in doc[section]:
                    fails.append((i, "%s/%s missing from the JSON" % (section, key),
                                  src, None))
                    continue
                back = unconvert_escapes(doc[section][key], tabs=False)
            else:
                n = slot - conv.OFFSET_ABOUTMSG
                if n >= len(doc["about"]):
                    fails.append((i, "about[%d] missing from the JSON" % n, src, None))
                    continue
                back = unconvert_escapes(doc["about"][n], tabs=False)
            rebuilt += 1

        if back != src:
            fails.append((i, "text differs", src, back))
            continue
        # Byte identity, not just string equality: a character that decoded
        # from the codepage but cannot be encoded back to it would pass the
        # comparison above and still mean the JSON is not this file.
        try:
            if back.encode(enc) != src.encode(enc):
                fails.append((i, "bytes differ after re-encoding", src, back))
        except UnicodeEncodeError as ex:
            fails.append((i, "will not re-encode to %s: %s" % (enc, ex), src, back))

    # Every slot the JSON holds must have come from a line -- otherwise the
    # conversion invented one.
    have = (len(doc["buttons"]) + len(doc["text"])
            + len(doc["dialogs"]) + len(doc["about"]))
    want = max(0, min(len(lines), conv.SIZE_ALL) - conv.START_BUTTON)
    if have != want:
        fails.append((-1, "JSON holds %d strings, the file has %d lines past the "
                      "menus" % (have, want), "", None))

    if verbose:
        print("    %d rebuilt, %d ignored (separators and short lines), "
              "%d lines" % (rebuilt, skipped, len(lines)))
    return fails, rebuilt, skipped


# --------------------------------------------------------------------------
# structure
# --------------------------------------------------------------------------
def shape(items):
    """The tree reduced to what must not vary between languages."""
    out = []
    for it in items:
        out.append((bool(it.get("separator")), it.get("cmd", -1), "items" in it))
        if "items" in it:
            out.append(shape(it["items"]))
    return out


def check_structure(docs):
    fails = []
    buttons, texts, dialogs = conv.slot_names()

    base = docs[conv.BASE]
    for section, want in (("buttons", buttons), ("text", texts), ("dialogs", dialogs)):
        if list(base[section].keys()) != want:
            fails.append("%s/%s: keys are not LT32L_US.H's, in order" % (conv.BASE, section))

    # The .inc is the shape; every language must match it exactly.
    ref = {"main": shape(conv.parse_menu("MAIN")),
           "editor": shape(conv.parse_menu("MENU2"))}
    sizes = {"main": conv.SIZE_MMENU, "editor": conv.SIZE_EMENU}

    for code, doc in docs.items():
        for section in ("buttons", "text", "dialogs"):
            extra = set(doc[section]) - set(base[section])
            if extra:
                fails.append("%s/%s: keys not in %s: %s"
                             % (code, section, conv.BASE, sorted(extra)))
        for which in ("main", "editor"):
            tree = doc["menu"][which]
            if shape(tree) != ref[which]:
                fails.append("%s/menu.%s: shape or command ids differ from %s"
                             % (code, which, conv.INC.name))
            n = conv.count_items(tree)
            if n != sizes[which]:
                fails.append("%s/menu.%s: %d nodes, the header says %d"
                             % (code, which, n, sizes[which]))
        if len(doc["about"]) > conv.SIZE_ABOUTMSG:
            fails.append("%s/about: %d lines, SIZE_ABOUTMSG is %d"
                         % (code, len(doc["about"]), conv.SIZE_ABOUTMSG))
        if doc["sourceEncoding"] != conv.ENCODINGS[code]:
            fails.append("%s: sourceEncoding is %r, the table says %r"
                         % (code, doc["sourceEncoding"], conv.ENCODINGS[code]))
    return fails


# --------------------------------------------------------------------------
# the C# dump
# --------------------------------------------------------------------------
def escape(s):
    """LangDump.Escape, rebuilt."""
    if s is None:
        return ""
    out = []
    for c in s:
        out.append({"\\": "\\\\", "\t": "\\t", "\r": "\\r", "\n": "\\n"}.get(c, c))
    return "".join(out)


def expected_dump(docs, code):
    """What `--lang-dump CODE` has to print, rebuilt from the JSON.

    Mirrors Language.Load's fallback: a key the language does not answer, or
    answers with an empty string, comes from the base language.  `about` does
    not fall back -- see Language.About for why.
    """
    doc = docs[code]
    base = docs[conv.BASE]

    strings = {}
    for section in ("buttons", "text", "dialogs"):
        strings.update(base[section] if code == conv.BASE else {})
    if code != conv.BASE:
        for section in ("buttons", "text", "dialogs"):
            for k, v in doc[section].items():
                if v:
                    strings[k] = v
    else:
        strings = {}
        for section in ("buttons", "text", "dialogs"):
            strings.update(doc[section])

    lines = ["code\t" + escape(doc["code"]),
             "name\t" + escape(doc["name"]),
             "author\t" + escape(doc["author"])]
    for k in sorted(strings):
        lines.append("S\t%s\t%s" % (k, escape(strings[k])))
    for i, a in enumerate(doc["about"]):
        lines.append("A\t%d\t%s" % (i, escape(a)))
    for which in ("main", "editor"):
        lines.extend(menu_lines(doc["menu"][which], which, ""))
    return lines, strings


def menu_lines(items, which, at):
    out = []
    for i, n in enumerate(items):
        addr = ("" if not at else at + ",") + "%02d" % i
        kind = "SEP" if n.get("separator") else ("POPUP" if "items" in n else "ITEM")
        out.append("M\t%s\t%s\t%d\t%s\t%s\t%s"
                   % (which, addr, n.get("cmd", -1), kind,
                      escape(n.get("accel")), escape(n.get("text"))))
        if "items" in n:
            out.extend(menu_lines(n["items"], which, addr))
    return out


def run_core(args):
    p = subprocess.run([str(engines.CORE)] + args, capture_output=True,
                       cwd=str(ROOT), timeout=120)
    return p.stdout.decode("utf-8").replace("\r\n", "\n"), p.returncode


def check_csharp(docs, codes):
    fails, borrowed_total = [], 0
    for code in codes:
        want, resolved = expected_dump(docs, code)
        out, rc = run_core(["--lang-dir", str(LANGDIR), "--lang-dump", code])
        if rc != 0:
            fails.append("%s: --lang-dump exited %d" % (code, rc))
            continue
        got = [l for l in out.split("\n") if l]
        if got != want:
            first = next((i for i in range(max(len(got), len(want)))
                          if i >= len(got) or i >= len(want) or got[i] != want[i]), 0)
            fails.append("%s: dump differs at line %d\n"
                         "      c#     %r\n      python %r"
                         % (code, first,
                            got[first] if first < len(got) else None,
                            want[first] if first < len(want) else None))
            continue
        own = set()
        for section in ("buttons", "text", "dialogs"):
            own |= {k for k, v in docs[code][section].items() if v}
        borrowed = len(set(resolved) - own) if code != conv.BASE else 0
        borrowed_total += borrowed
    return fails, borrowed_total


def check_fallback(docs):
    """Language.Load's fill-from-base branch, against a synthetic partial file.

    The shipped ten cannot test this, and finding that out is what the first run
    of this gate was for.  They look partial -- four are labelled 90% or less --
    but a translator who skipped a line copied the English one instead of
    leaving it blank, and the only section any file actually stops short in is
    `about`, which does not fall back (Language.About says why).  So against
    this corpus the fallback resolves nothing and a green `csharp` half proves
    nothing about it.

    Rather than assert something the corpus cannot show -- or, worse, drop the
    fallback and let a future file render `[ID_WINBOX_03]` on screen -- this
    builds the partial file the corpus does not contain: one key blanked, one
    removed outright, one section truncated.  Both ways of being absent are
    tested, because they are different lines of code.
    """
    fails = []
    base = docs[conv.BASE]
    blanked = "ID_WINBOX_01"          # present but empty
    dropped = "ID_HIGHLIST_00"        # absent from the file
    kept = "ID_GHIGHLIST_00"          # translated, must NOT be overwritten
    for k in (blanked, dropped, kept):
        if k not in base["dialogs"]:
            fails.append("the synthetic partial language names %s, which %s "
                         "does not have" % (k, conv.BASE))
            return fails, 0
    if fails:
        return fails, 0

    with tempfile.TemporaryDirectory(prefix="lt-lang-fb-") as d:
        tmp = pathlib.Path(d)
        (tmp / ("%s.json" % conv.BASE)).write_text(
            json.dumps(base, ensure_ascii=False), encoding="utf-8")

        partial = json.loads(json.dumps(base))       # deep copy
        partial["code"] = "Zz"
        partial["name"] = "Synthetic partial"
        partial["dialogs"][blanked] = ""
        del partial["dialogs"][dropped]
        partial["dialogs"][kept] = "KEPT"
        partial["about"] = partial["about"][:2]
        (tmp / "Zz.json").write_text(json.dumps(partial, ensure_ascii=False),
                                     encoding="utf-8")

        out, rc = run_core(["--lang-dir", str(tmp), "--lang-dump", "Zz"])
        if rc != 0:
            fails.append("the synthetic language would not dump (exit %d)" % rc)
            return fails, 0

        got = {}
        for line in out.split("\n"):
            parts = line.split("\t")
            if len(parts) == 3 and parts[0] == "S":
                got[parts[1]] = parts[2]

        for key, want, why in (
                (blanked, base["dialogs"][blanked], "an empty string must fall back"),
                (dropped, base["dialogs"][dropped], "an absent key must fall back"),
                (kept, "KEPT", "a translated string must survive the fallback")):
            if got.get(key) != want:
                fails.append("%s: %s -- got %r, wanted %r"
                             % (key, why, got.get(key), want))

        n_about = len([l for l in out.split("\n") if l.startswith("A\t")])
        if n_about != 2:
            fails.append("about does not fall back, so the truncated language "
                         "should dump 2 lines; it dumped %d" % n_about)

    return fails, 3


# --------------------------------------------------------------------------
# the game
# --------------------------------------------------------------------------
def run_godot(godot, args, timeout=300):
    """Run the Godot project and decode its output as UTF-8.

    Explicitly, and not with `text=True`, which is what every other gate in
    tools/ uses.  `text=True` decodes with the *locale* codec -- cp1252 on this
    machine -- and step 6 is the first gate whose output is not ASCII.  The
    failure is worse than mojibake: the reader thread raises UnicodeDecodeError
    on the first byte cp1252 leaves undefined (0x8d, inside `ã`), subprocess
    swallows it in the thread, and `p.stdout` comes back **empty**.  Four of the
    ten languages looked like a crashed game.  Nothing else here can grow that
    bug quietly, so the note goes where the fix is.
    """
    p = subprocess.run([godot, "--headless", "--path", str(GAME), "--"] + args,
                       capture_output=True, cwd=str(ROOT), timeout=timeout)
    out = (p.stdout or b"") + (p.stderr or b"")
    return out.decode("utf-8", errors="replace"), p.returncode


def check_godot(godot, docs, codes):
    """The game resolving the same strings, and remembering the choice."""
    fails = []
    for code in codes:
        out, rc = run_godot(godot, ["--check-lang", code])
        if rc != 0:
            fails.append("%s: --check-lang exited %d\n%s" % (code, rc, out[-1500:]))
            continue
        got = [l for l in out.replace("\r\n", "\n").split("\n")
               if l.startswith(("code\t", "name\t", "author\t", "S\t", "A\t", "M\t"))]
        want, _ = expected_dump(docs, code)
        if got != want:
            first = next((i for i in range(max(len(got), len(want)))
                          if i >= len(got) or i >= len(want) or got[i] != want[i]), 0)
            fails.append("%s: the game's strings differ at line %d\n"
                         "      godot  %r\n      python %r"
                         % (code, first,
                            got[first] if first < len(got) else None,
                            want[first] if first < len(want) else None))
    return fails


def check_ini(godot):
    """`[DATA] Language` survives a restart, and an unknown code falls back.

    Same shape as options_check's INI half, and for the same reason: the choice
    is only useful if it is still there next time.  An instrument must not write
    the player's state, so this runs in a temporary directory.
    """
    fails = []
    with tempfile.TemporaryDirectory(prefix="lt-lang-") as d:
        out, rc = run_godot(godot, ["--check-lang-ini", d])
        if rc != 0:
            fails.append("--check-lang-ini exited %d\n%s" % (rc, out[-1500:]))
            return fails, 0
        checks = [l for l in out.replace("\r\n", "\n").split("\n")
                  if l.startswith("ini\t")]
        if not checks:
            fails.append("--check-lang-ini printed no `ini` lines")
        for line in checks:
            parts = line.split("\t")
            if parts[-1] != "ok":
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

    codes = args.lang or list(conv.ENCODINGS)
    for c in codes:
        if c not in conv.ENCODINGS:
            print("lang_check: no such language %r (have: %s)"
                  % (c, " ".join(conv.ENCODINGS)))
            return 2
    if conv.BASE not in codes:
        codes = [conv.BASE] + codes          # the fallback needs it loaded

    docs = {}
    for code in codes:
        path = LANGDIR / ("%s.json" % code)
        if not path.exists():
            print("lang_check: %s missing\nrun: python tools/convert_language.py"
                  % path.relative_to(ROOT))
            return 2
        docs[code] = json.loads(path.read_text(encoding="utf-8"))

    ok = True

    print("roundtrip -- every source line rebuilt out of the JSON, byte for byte:")
    # What licenses the round trip's one asymmetry, measured before it is used.
    fails = check_tab_policy()
    if fails:
        ok = False
        for f in fails[:6]:
            print("  FAIL: %s" % f)
    else:
        print("  ok   tab policy: no menu line holds a literal tab, no other "
              "line holds an escaped one")

    total_rebuilt = total_skipped = 0
    for code in codes:
        fails, rebuilt, skipped = check_roundtrip(docs[code], code,
                                                  verbose=bool(args.lang))
        total_rebuilt += rebuilt
        total_skipped += skipped
        if fails:
            ok = False
            print("  %-3s FAIL (%d)" % (code, len(fails)))
            for i, why, src, back in fails[:6]:
                print("      line %d: %s" % (i, why))
                print("        original %r" % src)
                if back is not None:
                    print("        rebuilt  %r" % back)
        else:
            print("  %-3s ok   %3d lines rebuilt, %d ignored"
                  % (code, rebuilt, skipped))
    print("  %d lines rebuilt across %d file(s), %d ignored as the original "
          "ignores them" % (total_rebuilt, len(codes), total_skipped))

    print("structure -- keys and menu shape against the frozen header and .inc:")
    fails = check_structure(docs)
    if fails:
        ok = False
        for f in fails[:12]:
            print("  FAIL: %s" % f)
    else:
        print("  ok   %d buttons + %d texts + %d dialog slots, "
              "%d + %d menu nodes, %d file(s)"
              % (conv.SIZE_BUTTON, conv.SIZE_TEXT, conv.SIZE_DIALOGS,
                 conv.SIZE_MMENU, conv.SIZE_EMENU, len(codes)))

    print("csharp -- --lang-dump against the same dump rebuilt here:")
    if not engines.CORE.exists():
        print("  SKIP -- %s not built (run: bash src/build.sh)" % engines.CORE.name)
        ok = False
    else:
        fails, borrowed = check_csharp(docs, codes)
        if fails:
            ok = False
            for f in fails[:8]:
                print("  FAIL: %s" % f)
        else:
            print("  ok   %d language(s) identical (%d string(s) reached through "
                  "the fallback)" % (len(codes), borrowed))

        fails, n = check_fallback(docs)
        if fails:
            ok = False
            for f in fails[:8]:
                print("  FAIL: %s" % f)
        else:
            print("  ok   %d fallback case(s) on a synthetic partial language: "
                  "blank, absent, and translated" % n)

    if args.no_godot:
        print("godot -- SKIPPED (--no-godot)")
    else:
        godot = find_godot()
        if godot is None:
            print("godot -- SKIP: Godot not found (set LT_GODOT)")
        else:
            _ok, warn = engines.build_godot_game()
            print("godot -- build ok%s; the game's own strings, and the INI:"
                  % (" (%d warnings)" % warn if warn else ""))
            fails = check_godot(godot, docs, codes)
            if fails:
                ok = False
                for f in fails[:8]:
                    print("  FAIL: %s" % f)
            else:
                print("  ok   %d language(s) resolved identically in the game" % len(codes))
            fails, n = check_ini(godot)
            if fails:
                ok = False
                for f in fails[:8]:
                    print("  FAIL: %s" % f)
            else:
                print("  ok   %d INI check(s): the choice survives a restart" % n)

    print("lang_check " + ("OK" if ok else "FAILED"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
