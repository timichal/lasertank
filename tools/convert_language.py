#!/usr/bin/env python3
"""Convert the original Language.dat files into UTF-8 JSON the port can read.

The 2007 distribution ships ten translations, each a `Language\\Language.dat`
under `original/src/Setups/<dir>/`.  The format is *positional*: 240 lines,
comments and blanks skipped, in six fixed sections whose sizes live in
`LT32L_US.H` (SIZE_MMENU 49, SIZE_EMENU 24, SIZE_BUTTON 9, SIZE_TEXT 48,
SIZE_DIALOGS 96, SIZE_ABOUTMSG 14).  Each file is also in a *different* 8-bit
codepage, chosen by whichever Windows locale the translator happened to run.

Step 6 does not transliterate `LANGUAGE.C`.  The reason is written down in
PROGRESS.md: nothing about the rules depends on how a UI string reaches the
screen, and the port will never have to consume a new `.dat` -- these ten files
are the whole population, frozen in 2007.  So the conversion happens *once*,
here, and the port reads keyed UTF-8 JSON instead of counting lines.

What this script does NOT do is invent the key names.  Every one is read out of
the frozen `LT32L_US.H` (`ButText1..9`, `txt001..txt045`, `REC_Title`,
`help01..03`, `HelpFileName`, and the 96 `ID_*` dialog slots), and the menu
tree's shape and command ids are read out of the frozen `lt32l_us.inc`.  That
is what makes the output auditable: `lang_check.py` re-derives all of it and
round-trips every string back to the original bytes.

    python tools/convert_language.py              # -> data/language/*.json
    python tools/convert_language.py --check      # compare, write nothing
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SETUPS = ROOT / "original" / "src" / "Setups"
HEADER = ROOT / "original" / "src" / "LT32L_US.H"
INC = ROOT / "original" / "src" / "lt32l_us.inc"
OUT = ROOT / "data" / "language"

# LT32L_US.H:42-48.  These sizes are what makes the file positional, and
# lang_check.py asserts the boundaries land where the menu lines stop.
SIZE_MMENU, SIZE_EMENU = 49, 24
SIZE_BUTTON, SIZE_TEXT = 9, 48
SIZE_DIALOGS, SIZE_ABOUTMSG = 96, 14
START_EMENU = SIZE_MMENU                      # 49
START_BUTTON = START_EMENU + SIZE_EMENU       # 73
SIZE_ALL = (SIZE_BUTTON + SIZE_TEXT + SIZE_MMENU
            + SIZE_EMENU + SIZE_ABOUTMSG + SIZE_DIALOGS)   # 240

# Offsets into LANGText[] (LT32L_US.H:60-62)
OFFSET_TEXT = SIZE_BUTTON                          # 9
OFFSET_DIALOGS = OFFSET_TEXT + SIZE_TEXT           # 57
OFFSET_ABOUTMSG = OFFSET_DIALOGS + SIZE_DIALOGS    # 153

# The ten translations, keyed by the **ISO code the port uses**.  Each row is
# (the `Setups/` directory the frozen artifact keeps it in, the display name,
# the codepage the translator's machine wrote).
#
# The directory names are what the 2007 installer needed and they are not
# language codes: `Du` is Dutch, `Sp` Spanish, `Sw` Swedish -- and `Cs`/`Ct`
# are **Simplified and Traditional Chinese**, not Czech, which is what two
# files of identical size and line count looked like until they were decoded.
# `original/` is frozen, so those names live on the left of this table and
# nowhere else in the port.
#
# The codepages are not recorded anywhere in the distribution either.  They
# were established by decoding every file under every candidate and reading the
# result; a wrong one now fails loudly, because `read_lines` decodes strictly
# and `lang_check.py` re-encodes every line back to it and compares bytes.
#
# The display name is the port's, **not** the translator's own banner string.
# Those read `"English - ( Example )"`, `"Español ( 85% complete !)"`,
# `"Croatian - ( 100 %)"` -- a version note, a completeness claim and a stray
# space, which is a fine thing to write at the top of a file you are editing by
# hand and a poor thing to put in a picker.  The banner survives verbatim as
# `sourceName` (with its percentage, which is real information -- four files
# are labelled 90% or less), and `lang_check.py` checks it against the artifact.
# It is the English name rather than the endonym because the port draws its UI
# in `ThemeDB.FallbackFont`, which has no CJK glyphs: `简体中文` would be two boxes.
LANGUAGES = {
    #            dir   display name           codepage     translator
    "en":      ("US", "English",             "cp1252"),  # Jim Kindley
    "fr":      ("Fr", "French",              "cp1252"),  # Maingoy & Drouin
    "de":      ("De", "German",              "cp1252"),  # Manfred Sauke
    "nl":      ("Du", "Dutch",               "cp1252"),  # Ron Meijer
    "pt":      ("Pt", "Portuguese",          "cp1252"),  # Lima e Lima
    "es":      ("Sp", "Spanish",             "cp1252"),  # Sebastian Soto
    "sv":      ("Sw", "Swedish",             "cp1252"),  # Charon Direnj
    "hr":      ("Hr", "Croatian",            "cp1250"),  # Ivan Hrsto
    "zh-Hans": ("Cs", "Simplified Chinese",  "gbk"),     # Roy Chen
    "zh-Hant": ("Ct", "Traditional Chinese", "big5"),    # Roy Chen
}

SETUP_DIR = {code: row[0] for code, row in LANGUAGES.items()}
NAMES = {code: row[1] for code, row in LANGUAGES.items()}
ENCODINGS = {code: row[2] for code, row in LANGUAGES.items()}

# `en` is the base every other language falls back to, key by key.  That is the
# original's own behaviour rather than an invention: InitLanguage pre-fills the
# button and text slots from App_Strings and only *clears* the dialog and
# about-message ones, and LoadWindowText leaves a dialog control alone when its
# slot is empty -- which leaves the English text compiled into the .rc.  Four of
# the ten translations are partial and rely on exactly that.
BASE = "en"


def dat_path(code: str) -> pathlib.Path:
    """The frozen `Language.dat` behind one ISO code."""
    return SETUPS / SETUP_DIR[code] / "Language" / "Language.dat"


# --------------------------------------------------------------------------
# the frozen header: key names
# --------------------------------------------------------------------------
def slot_names() -> tuple[list[str], list[str], list[str]]:
    """Read the button / text / dialog key names out of LT32L_US.H.

    Hand-typing 153 names would be one transcription slip away from silently
    mislabelling a string, so they are parsed instead.  The text section's
    numbering has two gaps -- there is no txt003 and no txt030 -- which is
    exactly the kind of thing a hand-written table gets wrong.
    """
    h = HEADER.read_text(encoding="cp1252")
    buttons = [f"ButText{i}" for i in range(1, SIZE_BUTTON + 1)]
    for name in buttons:
        if not re.search(rf"#define\s+{name}\s+LANGText\[", h):
            raise SystemExit(f"{HEADER.name}: no #define for {name}")

    text = {int(n): nm for nm, n in re.findall(
        r"#define\s+(\w+)\s+LANGText\[OFFSET_TEXT\s*\+\s*(\d+)\]", h)}
    dialogs = {int(n): nm for nm, n in re.findall(
        r"#define\s+(ID_\w+)\s+\(OFFSET_DIALOGS\s*\+\s*(\d+)\)", h)}

    if gap := [i for i in range(SIZE_TEXT) if i not in text]:
        raise SystemExit(f"{HEADER.name}: text slots without a #define: {gap}")
    if gap := [i for i in range(SIZE_DIALOGS) if i not in dialogs]:
        raise SystemExit(f"{HEADER.name}: dialog slots without a #define: {gap}")

    return (buttons,
            [text[i] for i in range(SIZE_TEXT)],
            [dialogs[i] for i in range(SIZE_DIALOGS)])


# --------------------------------------------------------------------------
# the frozen .inc: menu shape and command ids
# --------------------------------------------------------------------------
_POPUP = re.compile(r'^POPUP\s+"(.*)"\s*$')
_ITEM = re.compile(r'^MENUITEM\s+"(.*)"\s*,\s*(\d+)\s*(?:,\s*\w+\s*)*$')
_SEP = re.compile(r"^MENUITEM\s+SEPARATOR\s*$")


def parse_menu(name: str) -> list[dict]:
    """Parse one `<name> MENU BEGIN ... END` block out of lt32l_us.inc.

    Returns the top-level item list; a popup carries `items`.  This is the tree
    `ChangeMenuText` addresses by position, so the shape has to be exact --
    every `NN,NN,NN` triple in a .dat is a set of indices into it.
    """
    src = INC.read_text(encoding="cp1252")
    src = re.sub(r"/\*.*?\*/", "", src, flags=re.S)
    src = re.sub(r"//[^\n]*", "", src)
    lines = [l.strip() for l in src.splitlines()]

    start = next((i for i, l in enumerate(lines)
                  if re.match(rf"^{name}\s+MENU\b", l)), None)
    if start is None:
        raise SystemExit(f"{INC.name}: no `{name} MENU` block")
    if lines[start + 1] != "BEGIN":
        raise SystemExit(f"{INC.name}: `{name} MENU` is not followed by BEGIN")

    root: list[dict] = []
    stack: list[list[dict]] = [root]
    pending: list[dict] = []          # popups awaiting their BEGIN
    for i in range(start + 2, len(lines)):
        line = lines[i]
        if not line:
            continue
        if line == "BEGIN":
            if not pending:
                raise SystemExit(f"{INC.name}: BEGIN with no POPUP ({name})")
            node = pending.pop()
            node["items"] = []
            stack.append(node["items"])
        elif line == "END":
            stack.pop()
            if not stack:
                break
        elif m := _POPUP.match(line):
            node = {"text": m.group(1)}
            stack[-1].append(node)
            pending.append(node)
        elif _SEP.match(line):
            stack[-1].append({"separator": True})
        elif m := _ITEM.match(line):
            stack[-1].append({"text": m.group(1), "cmd": int(m.group(2))})
        else:
            raise SystemExit(f"{INC.name}: unparsed line in {name}: {line!r}")
    else:
        raise SystemExit(f"{INC.name}: unbalanced BEGIN/END in {name}")

    if pending:
        raise SystemExit(f"{INC.name}: POPUP without BEGIN in {name}")
    return root


def count_items(items: list[dict]) -> int:
    """Every node counts, popups included -- that is what SIZE_MMENU counts."""
    n = 0
    for it in items:
        n += 1
        if "items" in it:
            n += count_items(it["items"])
    return n


# --------------------------------------------------------------------------
# reading a .dat
# --------------------------------------------------------------------------
def read_lines(code: str) -> list[str]:
    """The non-comment, non-blank lines of one Language.dat, decoded.

    This is `InitLanguage`'s own filter (LANGUAGE.C:120-155): skip anything
    empty or starting with '#', and -- because the C chops the newline with
    `szTmp[strlen(szTmp)-1]` after fgets in text mode -- treat CRLF as the
    terminator.  Nothing else about that loop is reproduced; see the module
    docstring for why.
    """
    raw = dat_path(code).read_bytes()
    text = raw.decode(ENCODINGS[code])          # raises on a wrong codepage
    return [l for l in text.split("\r\n") if l and not l.startswith("#")]


def convert_escapes(s: str) -> str:
    """`ConvertTabChar` (LANGUAGE.C:206-234), minus the in-place pointer walk.

    `\\t` becomes a tab and `\\n` becomes CR LF; every other backslash is left
    alone, which is why `HelpFileName` survives as `Language\\LaserTank.hlp`.
    The CR LF is normalised to a bare newline here -- the port renders text, it
    does not hand a buffer to GDI -- and lang_check.py inverts exactly this.
    """
    out, i = [], 0
    while i < len(s):
        if s[i] == "\\" and i + 1 < len(s) and s[i + 1] == "t":
            out.append("\t")
            i += 2
        elif s[i] == "\\" and i + 1 < len(s) and s[i + 1] == "n":
            out.append("\n")
            i += 2
        else:
            out.append(s[i])
            i += 1
    return "".join(out)


def decode_menu_int(pair: str) -> int:
    """`DecodeMenuInt` (LANGUAGE.C:158-168), including its ceiling of 19.

    '--' and anything starting with a digit above 1 decode to -1, so a menu
    position of 20 or more is unaddressable.  Both menus are well inside that.
    """
    if pair[0] == "0":
        return ord(pair[1]) - ord("0")
    if pair[0] == "1":
        return 10 + ord(pair[1]) - ord("0")
    return -1


def split_accel(text: str) -> tuple[str, str | None]:
    """Split `&New\\tF2` into its label and the accelerator *hint* beside it.

    The hint is cosmetic in both the original and the port: the keys that
    actually fire come from the ACC1 / ACC2 accelerator tables, which step 1
    ported.  It is kept verbatim, typo included -- `Restore &Position` is
    labelled `Crtl+V` in all ten files.
    """
    if "\t" in text:
        label, _, accel = text.partition("\t")
        return label, accel
    return text, None


def apply_menu(items: list[dict], line: str) -> bool:
    """`ChangeMenuText` (LANGUAGE.C:171-203) against the parsed tree.

    Returns True when the line addressed a string item and replaced its text.
    The two ways it can decline are the original's, not this port's: a line of
    9 characters or fewer is ignored outright, and a target whose `fType` is not
    `MFT_STRING` is left alone -- which is why every separator in the file
    carries the untranslated word `SEPARATOR` and no translator ever had to
    wonder about it.
    """
    if len(line) <= 9:
        return False
    n0, n1, n2 = (decode_menu_int(line[0:2]),
                  decode_menu_int(line[3:5]),
                  decode_menu_int(line[6:8]))
    text = line[9:]

    node = None
    if n2 >= 0:
        if n0 < len(items) and "items" in items[n0]:
            lvl1 = items[n0]["items"]
            if n1 < len(lvl1) and "items" in lvl1[n1]:
                lvl2 = lvl1[n1]["items"]
                if n2 < len(lvl2):
                    node = lvl2[n2]
    elif n1 >= 0:
        if n0 < len(items) and "items" in items[n0]:
            lvl1 = items[n0]["items"]
            if n1 < len(lvl1):
                node = lvl1[n1]
    elif 0 <= n0 < len(items):
        node = items[n0]

    if node is None or node.get("separator"):
        return False
    label, accel = split_accel(convert_escapes(text))
    node["text"] = label
    if accel is not None:
        node["accel"] = accel
    else:
        node.pop("accel", None)
    return True


# --------------------------------------------------------------------------
# building one language
# --------------------------------------------------------------------------
def header_field(code: str, field: str) -> str:
    """Pull `# Language :` / `# Author   :` out of the file's own banner."""
    text = dat_path(code).read_bytes().decode(ENCODINGS[code])
    for line in text.split("\r\n")[:8]:
        if m := re.match(rf"#\s*{field}\s*:\s*(.*?)\s*#*\s*$", line):
            return m.group(1).strip()
    return ""


def build(code: str, names: tuple[list[str], list[str], list[str]]) -> dict:
    buttons, texts, dialogs = names
    lines = read_lines(code)

    main = parse_menu("MAIN")
    editor = parse_menu("MENU2")
    if count_items(main) != SIZE_MMENU:
        raise SystemExit(f"MAIN has {count_items(main)} items, SIZE_MMENU is {SIZE_MMENU}")
    if count_items(editor) != SIZE_EMENU:
        raise SystemExit(f"MENU2 has {count_items(editor)} items, SIZE_EMENU is {SIZE_EMENU}")

    # Sections 1 and 2: the two menus, addressed rather than assigned.
    for i in range(min(START_EMENU, len(lines))):
        apply_menu(main, lines[i])
    for i in range(START_EMENU, min(START_BUTTON, len(lines))):
        apply_menu(editor, lines[i])

    # Sections 3-6: LANGText[0..166], straight assignment.  A file may simply
    # stop early -- four of the ten do, all of them in the about message -- and
    # a slot never written stays absent so the fallback to `en` can fill it.
    slots: dict[int, str] = {}
    for i in range(START_BUTTON, min(SIZE_ALL, len(lines))):
        slots[i - START_BUTTON] = convert_escapes(lines[i])

    def section(offset: int, keys: list[str]) -> dict[str, str]:
        return {k: slots[offset + i] for i, k in enumerate(keys)
                if offset + i in slots}

    about = [slots[OFFSET_ABOUTMSG + i] for i in range(SIZE_ABOUTMSG)
             if OFFSET_ABOUTMSG + i in slots]

    return {
        "code": code,
        "name": NAMES[code],
        "author": header_field(code, "Author") or header_field(code, "Authors"),
        "sourceDir": SETUP_DIR[code],
        "sourceName": header_field(code, "Language"),
        "sourceEncoding": ENCODINGS[code],
        "sourceLines": len(lines),
        "buttons": section(0, buttons),
        "text": section(OFFSET_TEXT, texts),
        "dialogs": section(OFFSET_DIALOGS, dialogs),
        "about": about,
        "menu": {"main": main, "editor": editor},
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true",
                    help="compare against what is on disk; write nothing")
    args = ap.parse_args()

    names = slot_names()
    OUT.mkdir(parents=True, exist_ok=True)
    stale = []
    for code in LANGUAGES:
        doc = build(code, names)
        blob = json.dumps(doc, ensure_ascii=False, indent=1) + "\n"
        path = OUT / f"{code}.json"
        if args.check:
            old = path.read_text(encoding="utf-8") if path.exists() else None
            state = "ok" if old == blob else ("MISSING" if old is None else "STALE")
            if state != "ok":
                stale.append(code)
        else:
            path.write_text(blob, encoding="utf-8", newline="\n")
            state = "written"
        print(f"  {code:<7} {doc['sourceDir']}  {doc['name'][:21]:<21} "
              f"{doc['sourceLines']:>3} lines  {len(blob):>6} B  {state}")

    if stale:
        print(f"\nFAIL: {len(stale)} out of date: {' '.join(stale)}"
              "\n      run tools/convert_language.py to regenerate")
        return 1
    print(f"\nOK: {len(LANGUAGES)} languages -> {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
