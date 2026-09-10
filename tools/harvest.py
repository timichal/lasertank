#!/usr/bin/env python
"""The lasertanksolutions.blogspot.com goal-board harvester (next actions item 6).

Each solution post carries a *start* screenshot of the board and one screenshot
per flag showing the board at the moment that flag was reached -- no move list
and no keystream.  What that buys the solver is not solutions, it is a *goal*:
a final board says which blocks were moved where and which bricks were
destroyed, so "reach the flag" becomes "reach *this* board", a progress measure
that decreases with every push in the right direction.

**A level solved with a scraped goal board is hint-assisted and must never
enter the solver's headline rate.**  Its value is as a bootstrap: hint-assisted
solutions are still real recordings, and real recordings are what --profile and
basin.py measure and what layer 4 is fit on.

**Run `complete`.**  It is the whole chain in one command and one report, and
the nine phases below are the instruments to reach for when one of them is
what is being worked on:

  complete   index -> map -> fetch -> codebook -> tiles gate -> bank, into the
             committed bench/goal-boards.json, with every phase's own output in
             build/harvest/complete.log and, on stdout, one funnel and one
             table: every post the decode did not finish on its own, why, and
             whether that is intentional or is waiting on a human.  The last
             line is the count of the latter.
             ~65 min, against ~2h40 for the phases run one at a time -- three
             of those phases decode all 7,484 goal boards at 37 minutes each
             and `complete` needs only the one that banks them.  --report
             re-prints the last run's table without running anything

Ten subcommands, cheapest first:

  index      the whole post index from the Blogger feed -- title, URL, date and
             image URLs for all 6,218 posts in 42 requests, one minute.  No
             HTML scraping: the feed serves the post body, and the image
             filenames are in it
  map        (collection, level) -> name, checked against the corpus .lvl.
             Reads the index; no network, no images
  fetch      download the start and goal screenshots for selected levels --
             every part of a multi-part post, and start-vs-goal from the post's
             own order where the URLs carry no filename to read it off
  codebook   build the 24x24 tile codebook from start screenshots, whose boards
             the corpus already knows -- 256 labelled tiles per post, free.
             Learns only from a picture that *is* a start position (tank on the
             .lvl's own T cell, facing up); a play state labelled from the .lvl
             labels everything play produced with what was there before it.
             --goals sizes what this half does *not* cover; that was the item's
             remaining cost until `tiles` derived all of it.  Also the
             *staleness* check: a start board that disagrees with its own .lvl
             is a level re-authored since the post, and those go to stale.json
  tiles      the goal-only sprites *derived* rather than labelled, from the
             game's own sheet -- tools/sprites.py.  This is the gate on that
             derivation, and it retired `sheet`/`label` as the item's phase 1
  sheet      the sprites the derivation does not cover, as one contact sheet
             plus a sidecar to fill in.  Empty over the 150-post sample and
             three tiles at corpus scale -- and all three are *board* facts
             rather than tile facts, so they live in bench/post-fixups.json
             instead: the pixels of a tank over an anti-tank are the same
             whatever is underneath, so the same hash means different things on
             different boards and a hash table is the wrong shape for it.  It
             also writes the residual.json `tiles`' second gate reads, so
             skipping it silently skips that gate
  label      merge the filled-in sidecar into bench/goal-tiles.json
  decode     a screenshot -> a 16x16 board *and* where the tank is.  --check
             re-decodes a start screenshot and diffs it against the .lvl,
             which is the gate
  bank       every fetched goal screenshot decoded into one file: the bank
             `lasertank-solve --goal-board` reads.  Refuses a board with an
             undecoded cell in it rather than banking a key with a hole, and a
             level in stale.json rather than banking a target it cannot reach

    python tools/harvest.py complete                    # the whole chain, ~65 min
    python tools/harvest.py complete --report           # its table again, free
    python tools/harvest.py index                       # 42 requests, ~46 s
    python tools/harvest.py map                         # no network, no images
    python tools/harvest.py fetch --limit 150 --goals    # ~7 min
    python tools/harvest.py codebook --goals
    python tools/harvest.py tiles                       # the derivation's gate
    python tools/harvest.py sheet                       # what tiles could not derive
    python tools/harvest.py label                       # after filling the sidecar in
    python tools/harvest.py fetch --levels LaserTank:10 --goals
    python tools/harvest.py decode build/harvest/img/LaserTank_10_a.png --check
    python tools/harvest.py bank                        # -> build/harvest/goals.json

The derivable half lands under build/harvest/, which is gitignored: the index
and the images are re-fetchable and the codebook is re-derivable from them.
**And the goal-only half turned out to be derivable too**, which is what
`tiles` is: session 35 found that the 2010 binary's own graphics are committed
under `original/src/`, so compositing them the way `UpDateSprite` does
reproduces the blog's pixels exactly and labels every state play produces --
116 of 116 residual sprites, 0 unknown tiles over 173 goal boards.  So
`bench/goal-tiles.json` is no longer where this item's answer lives; it stays a
committed input for anything the sheet cannot draw, which so far is one tile
caught between the mask blit and the sprite blit.

Exit: 0 clean, 1 a decode disagreed with the corpus in a way the sprite sheet
blames on the *codebook* -- a disagreement it blames on the .lvl is a stale
post and a finding rather than a failure -- 2 environment.
"""
import argparse
import collections
import hashlib
import json
import os
import pathlib
import random
import re
import struct
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import png                                              # noqa: E402

# Level names in the corpus are latin1 bytes, and some of them (Sokoban-I 633,
# LaserTank 1209) are outside this console's cp1252 -- so `map` died on its own
# output before this line existed.  Replace rather than raise: a name that will
# not print is not a reason to lose the check that printed it.
for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(errors="replace")
    except (AttributeError, ValueError):        # not a TextIOWrapper
        pass

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "build" / "harvest"
FEED = "https://lasertanksolutions.blogspot.com/feeds/posts/default"
UA = {"User-Agent": "Mozilla/5.0"}

LEVEL_REC = 576                 # tools/engines.py LEVEL_REC; PROGRESS.md
GHS_REC = 10

# The board sits inside a two-pixel (128,128,128) frame at 24 px per cell --
# the 32x32 sprites at three quarters.  Two window geometries appear over the
# nine years of posts, 609x463 and 619x473, and the frame finds both.
GREY = b"\x80\x80\x80"
PITCH = 24

SYM = {0: ".", 1: "T", 2: "F", 3: "~", 4: "#", 5: "B", 6: "b", 7: "^", 8: ">",
       9: "v", 10: "<", 11: "m", 12: "n", 13: "o", 14: "p", 15: "U", 16: "R",
       17: "D", 18: "L", 19: "C", 20: "q", 21: "w", 22: "e", 23: "r", 24: "I",
       25: "i"}

# Post titles are 'Collection - number - name'; 2016-era posts drop the
# collection and are all LaserTank.  A '(Part N)' suffix is one solution split
# over several posts -- but four *level names* end in '(Part N)' too, so the
# suffix is only ever stripped as a fallback.
PAT_FULL = re.compile(r"^(?P<coll>[A-Za-z][A-Za-z0-9 \-]*?)\s*-\s*"
                      r"(?P<num>\d+)\s*-\s*(?P<name>.+)$")
PAT_BARE = re.compile(r"^(?P<num>\d+)\s*-\s*(?P<name>.+)$")
PART = re.compile(r"\s*\((?:Part|part)\s*\d+\)\s*$")

# Blog furniture, not boards: the difficulty stars, the flag bullet and the
# sidebar logos.  Everything else in a post body is a screenshot.
CHROME = re.compile(r"/(?:LaserTank2|F_icon|Pinterest_icon|Twitter_logo|Blogger"
                    r"|logos|GrammarSchool|one|two|three|four|five|six|flag2)"
                    r"\.png$", re.I)


# ---------------------------------------------------------------- the corpus

def cell(v):
    return str((v & 0x0F) >> 1) if v >= 0x40 else SYM.get(v, "?%d" % v)


_lvl = {}


def collection(coll):
    """{level: (board, name)} for one .lvl, or None if it is not shipped."""
    if coll not in _lvl:
        p = ROOT / "data" / "levels" / (coll + ".lvl")
        if not p.exists():
            _lvl[coll] = None
        else:
            d = p.read_bytes()
            out = {}
            for i in range(len(d) // LEVEL_REC):
                r = d[i * LEVEL_REC:(i + 1) * LEVEL_REC]
                pf = r[:256]
                out[i + 1] = ([[cell(pf[x * 16 + y]) for x in range(16)]
                               for y in range(16)],
                              r[256:287].split(b"\0")[0].decode("latin1"))
            _lvl[coll] = out
    return _lvl[coll]


def board(coll, n):
    c = collection(coll)
    return None if not c or n not in c else c[n][0]


def level_name(coll, n):
    c = collection(coll)
    return None if not c or n not in c else c[n][1]


def ghs(coll, n):
    """The best-known (moves, shots) for one level, or None."""
    p = ROOT / "data" / "levels" / (coll + ".ghs")
    if not p.exists():
        return None
    r = p.read_bytes()[(n - 1) * GHS_REC:n * GHS_REC]
    return struct.unpack("<HH", r[:4]) if len(r) == GHS_REC else None


def norm(s):
    s = unicodedata.normalize("NFKD", s)
    s = "".join(c for c in s if not unicodedata.combining(c))
    return re.sub(r"[^a-z0-9]", "", s.lower())


# ---------------------------------------------------------------- the index

def get(url, timeout=60):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA),
                                  timeout=timeout).read()


def hms(s):
    """Seconds as the coarsest unit that still says something: 45s, 7m, 1h12."""
    s = int(s)
    if s < 90:
        return "%ds" % s
    if s < 3600:
        return "%dm" % round(s / 60.0)
    return "%dh%02d" % (s // 3600, (s % 3600) // 60)


class Ticker:
    """The progress line for the loops that take minutes.

    stderr and not stdout, because every subcommand's stdout is a report worth
    redirecting to a file and a counter is not part of it.  Two shapes, though,
    because a terminal and a log want different things: on a tty the line is
    rewritten in place four times a second, and on a redirect -- which is the
    run whose progress nobody can otherwise see -- it is one *new* line every
    ten seconds, so `harvest ... > log 2>&1` collects a readable trail instead
    of a megabyte of carriage returns.  `total` stays None until something
    knows it, which for `index` is the first response; with it comes the
    number these runs are actually watched for, which is how much is left.
    """

    def __init__(self, unit, total=None):
        self.unit, self.total = unit, total
        self.n = 0
        self.t0 = self.last = time.monotonic()
        self.pad = 0
        try:
            self.tty = sys.stderr.isatty()
        except (AttributeError, ValueError):
            self.tty = False
        self.every = 0.25 if self.tty else 10.0

    def tick(self, n=1, note=""):
        self.n += n
        t = time.monotonic()
        if t - self.last < self.every and self.n != self.total:
            return
        self.last = t
        eta = ""
        if self.total and self.n:
            el = t - self.t0
            eta = "  %s elapsed, %s left" % (
                hms(el), hms(el / self.n * (self.total - self.n)))
        line = "  %d/%s %s%s%s" % (self.n, "?" if self.total is None
                                   else self.total, self.unit, eta, note)
        if self.tty:
            # Pad to the longest line so far: the note changes length, and a
            # short line written over a longer one leaves its tail behind.
            self.pad = max(self.pad, len(line))
            sys.stderr.write("\r" + line.ljust(self.pad))
        else:
            sys.stderr.write(line + "\n")
        sys.stderr.flush()

    def done(self):
        """Close the line -- on a tty, where one is still open and unterminated."""
        if self.tty and self.n:
            sys.stderr.write("\n")
            sys.stderr.flush()


def cmd_index(args, out=None):
    OUT.mkdir(parents=True, exist_ok=True)
    rows = []
    i = 1
    total = None
    tick = Ticker("posts")
    while True:
        j = json.loads(get("%s?alt=json&max-results=150&start-index=%d" % (FEED, i)))
        f = j["feed"]
        total = int(f["openSearch$totalResults"]["$t"])
        es = f.get("entry", [])
        if not es:
            break
        for e in es:
            imgs = []
            for m in re.finditer(
                    r"https://blogger\.googleusercontent\.com/img/[^\"'<>\s]+",
                    e["content"]["$t"]):
                u = m.group(0)
                if not CHROME.search(u) and u not in imgs:
                    imgs.append(u)
            rows.append({
                "title": e["title"]["$t"],
                "url": [l["href"] for l in e["link"] if l["rel"] == "alternate"][0],
                "published": e["published"]["$t"][:10],
                "imgs": imgs,
            })
        tick.total = total
        tick.tick(len(es))
        i += len(es)
        if len(rows) >= total:
            break
        time.sleep(0.2)
    tick.done()
    p = OUT / "index.jsonl"
    with open(p, "w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    print("index: %d posts of %d -> %s" % (len(rows), total, p))
    if out is not None:
        out["posts"] = len(rows)
    return 0


def read_index():
    p = OUT / "index.jsonl"
    if not p.exists():
        raise SystemExit("no %s -- run: python tools/harvest.py index" % p)
    with open(p, encoding="utf-8") as f:
        return [json.loads(l) for l in f]


def parse_title(t):
    """'Challenge-I - 1901 - name' -> (collection, level, name, part)."""
    t = t.strip()
    m = PAT_BARE.match(t)
    coll = "LaserTank"
    if not m:
        m = PAT_FULL.match(t)
        if not m:
            return None
        coll = m.group("coll").strip()
    name = m.group("name").strip()
    pm = re.search(r"\((?:Part|part)\s*(\d+)\)\s*$", name)
    return coll, int(m.group("num")), name, int(pm.group(1)) if pm else 0


# Blogger's newer size suffix: `<blob>=s609`, `<blob>=w400-h316`.
SIZED = re.compile(r"=[a-z]+\d+(?:-[a-z]+\d+)*$")


def blob_id(bn):
    """A *filename-less* Blogger basename -> the blob its renditions share.

    Two URL shapes, and only one of them names the picture.  The old shape puts
    the size in a path segment and keeps the author's filename --
    `/s1600/LaserTank_725.png` -- which is what `pick_images` reads to tell a
    start frame from a goal frame.  The newer `/img/a/<blob>=s609` shape carries
    **no filename at all**: the several renditions of one picture differ only in
    that suffix, and nothing in the URL says which level, let alone which frame.

    Returns None for a basename that does have a filename, so every 2016-era
    and every `<coll>_<n>.png` URL is untouched by this.  For the rest it
    returns the blob, which collapses the renditions to one entry -- they were
    two dict keys before, so a post with a start and a goal looked like four
    pictures.
    """
    b = SIZED.sub("", bn)
    return None if "." in b else b


def pictures(row):
    """One index row -> its screenshots, `[(filename or None, url)]`.

    In the post's own document order, **one entry per picture**, which is not
    the same as one entry per filename.  Blogger serves a picture at several
    sizes and the feed carries them all: the old shape varies a path segment
    (`.../<blob>/s1600/LaserTank_801.png`) and the new one a suffix
    (`<blob>=s609`), so the picture's identity is the blob and the rest is a
    rendition.  Keying on the *basename* instead collapsed two different
    pictures whenever the author saved both under one name, which 37 posts do:
    `LaserTank_801.png` is the start and `LaserTank_801.png` is the goal, one
    blob each, and the second was dropped before anything could look at it.
    Those 37 are 30 of the 58 levels that looked like they carried no goal
    screenshot, and 3 of the 21 that looked like they carried no start.

    The filename comes back unquoted -- `ChallengeI_1748_G%6010.png` is a
    backtick the author typed and the feed percent-encoded -- and it is None
    for the newer shape, which carries no filename at all.
    """
    out, seen = [], set()
    for u in row["imgs"]:
        seg = u.split("/")
        bn = seg[-1]
        blob = blob_id(bn)
        key = blob or (seg[-3] if len(seg) >= 3 else bn)
        if key in seen:
            continue
        seen.add(key)
        out.append((None if blob else urllib.parse.unquote(bn), u))
    return out


_fix = None


def fixup(url):
    """This post's committed correction, or {} -- see bench/post-fixups.json.

    Three of 6,218 posts name the wrong level, and no rule can separate a typo
    from a picture of a genuinely different level: `pick_images`' whole premise
    is that a filename is a *claim* about which level a picture is of, and these
    are the posts where the claim and the title disagree.  Deciding between them
    means playing the level, so it is hand input and it is committed, and every
    entry carries the note that says who checked it.
    """
    global _fix
    if _fix is None:
        p = ROOT / "bench" / "post-fixups.json"
        _fix = {k: v for k, v in json.loads(p.read_text()).items()
                if not k.startswith("_")} if p.exists() else {}
    return _fix.get(url, {})


def posts_by_level(rows=None):
    """(collection, level) -> [post], in the feed's order.

    `imgs` keeps the *document* order of the post's pictures, because a picture
    the filenames do not account for is placed by that order and nothing else;
    see `pick_images`.
    """
    out = collections.defaultdict(list)
    for r in rows if rows is not None else read_index():
        p = parse_title(r["title"])
        if not p:
            continue
        coll, n, name, part = p
        fx = fixup(r["url"])
        coll, n = fx.get("coll", coll), fx.get("level", n)
        out[(coll, n)].append({"url": r["url"], "published": r["published"],
                               "name": name, "part": part,
                               "imgs": pictures(r),
                               "image_level": fx.get("image_level", n),
                               "image_coll": fx.get("image_coll")})
    return out


def cmd_map(args, out=None):
    """Check every post's (collection, level) against the corpus, and every
    picture's filename against the post.

    The second half is here rather than in `fetch` because it is a fact about
    the blog and costs no network: a picture whose filename names *another*
    level is dropped by `pick_images` -- see its docstring for why it cannot be
    anything else -- and a drop nobody reports is a frame silently lost.  So
    `map` walks the same filenames offline and collects them, which is what
    puts them in `complete --offline`'s table too.
    """
    rows = read_index()
    stat = collections.Counter()
    bad = []
    seen = set()
    nocorpus = []
    misnamed = []
    for r in rows:
        p = parse_title(r["title"])
        if not p:
            stat["not a solution post"] += 1
            continue
        coll, n, name, _ = p
        fx = fixup(r["url"])
        if fx.get("coll") or fx.get("level"):
            coll, n = fx.get("coll", coll), fx.get("level", n)
            stat["retargeted by bench/post-fixups.json"] += 1
        got = level_name(coll, n)
        if got is None:
            stat["collection or level not in corpus"] += 1
            bad.append((coll, n, name, None))
            nocorpus.append((coll, n, name, r["url"]))
            continue
        seen.add((coll, n))
        if norm(got) in (norm(name), norm(PART.sub("", name))):
            stat["exact"] += 1
        else:
            stat["title name differs"] += 1
            bad.append((coll, n, name, got))
        mis = pick_images(coll, n, pictures(r), fx.get("image_level"),
                          fx.get("image_coll"))[3]
        if mis:
            stat["picture names another level"] += len(mis)
            misnamed.append((coll, n, r["url"], mis))
    print("posts: %d" % len(rows))
    for k, v in stat.most_common():
        print("  %-38s %5d" % (k, v))
    print("distinct (collection, level) covered: %d" % len(seen))
    if bad:
        print("\nthe residual -- every one a blog-side title typo, not a bad index:")
        for coll, n, name, got in bad:
            print("  %-14s %5d  post %r  corpus %r" % (coll, n, name, got))
    if misnamed:
        print("\npictures dropped because the filename names another level -- "
              "one `image_level` line in bench/post-fixups.json each, where the "
              "title is the one to believe:")
        for coll, n, url, mis in misnamed:
            print("  %-14s %5d  %s\n                  %s"
                  % (coll, n, ", ".join(mis), url))
    per = collections.Counter(k[0] for k in seen)
    print("\nper collection:")
    for c in sorted(per):
        c_all = collection(c)
        print("  %-14s %5d covered of %5d" % (c, per[c], len(c_all) if c_all else -1))
    if out is not None:
        out["posts"] = len(rows)
        out["solution posts"] = len(rows) - stat["not a solution post"]
        out["not a solution post"] = stat["not a solution post"]
        out["covered"] = len(seen)
        out["nocorpus"] = nocorpus
        out["misnamed"] = misnamed
    return 0


# ---------------------------------------------------------------- the images

_prefixes = {}


def name_prefixes(coll):
    """The image-filename prefixes that name this collection and nothing else.

    'Challenge-I' is written 'ChallengeI' and that is the only spelling every
    collection but one uses; 235 Special-I posts write 'Special_519b.png',
    dropping the '-I'.  The abbreviation to the part before the hyphen is
    accepted exactly when it is unambiguous -- 'Special' names one shipped
    .lvl, where 'Challenge', 'Sokoban', 'Beginner' and 'Gary' each name
    several.  That is the whole rule, and it is the rule because the point of
    reading the prefix is that it is a claim about which level the picture is
    of: an abbreviation that could mean two collections makes no such claim,
    and neither does the 2016 era's prefixless '540.png', which is why the
    empty prefix is always allowed.
    """
    if not _prefixes:
        stems = [p.stem for p in (ROOT / "data" / "levels").glob("*.lvl")]
        first = collections.Counter(c.split("-")[0].lower() for c in stems)
        for c in stems:
            ok = {c.replace("-", "").lower()}
            if first[c.split("-")[0].lower()] == 1:
                ok.add(c.split("-")[0].lower())
            _prefixes[c] = ok
    return _prefixes.get(coll) or {coll.replace("-", "").lower()}


# A screenshot's filename, as the authors write it: an optional collection
# prefix, the level number, and whatever is left.  The remainder may not open
# with a digit -- `LaserTank_45.png` in a level-4 post is level 45's picture,
# not level 4's frame '5' -- which is the whole of what keeps this from reading
# every neighbouring level number as a frame tag.
PIC = re.compile(r"(?i)([a-z]+)?_?(\d+)(|[^0-9].*)")


def claim(name):
    """What a filename claims: (collection prefix, level, the rest), or None.

    A filename is a *claim* about which picture this is, and the three parts
    are three separate claims: the prefix says which collection, the number
    says which level, and the remainder says which frame.  They are read
    together and judged apart -- a wrong number is a typo, a wrong collection
    is a different level's screenshot, and neither is the same mistake as a
    remainder this tool has never seen.  None means the name makes no claim at
    all, which is not the same as making one that is wrong.
    """
    m = PIC.fullmatch(name.rsplit(".", 1)[0].rstrip("."))
    return None if not m else ((m.group(1) or "").lower(), int(m.group(2)),
                               m.group(3))


def frame_tag(rest):
    """A filename's remainder -> the goal frame's tag, or '' if it spells none.

    The tag is an ordering key and a piece of the fetched file's own name; it
    is not interpreted anywhere, so the rule is to keep the author's own word
    rather than to understand it.  Two spellings are pinned because they are
    what the corpus is already banked under: a lone letter is lowercased ('b'),
    and `_A15` -- the underscore is what separates a *flag cell* from a
    sub-frame -- is uppercased ('A15', which is why `696b2` is 'b2' and not
    cell B2).  Everything else keeps its shape minus the characters a filename
    should not carry: `_percent_25` -> 'percent_25', `_Finish` -> 'Finish',
    ``_G`10`` -> 'G10', `b_3` -> 'b_3'.
    """
    r = rest.lstrip("_")
    if re.fullmatch(r"(?i)[a-z]", r):
        return r.lower()
    if rest.startswith("_") and re.fullmatch(r"(?i)[a-z]\d+", r):
        return r.upper()
    return re.sub(r"[^A-Za-z0-9_]", "", r)


def pick_images(coll, n, pics, image_level=None, image_coll=None):
    """-> (start url, [(tag, goal url)], the rule that picked the start, [misnamed]).

    Two naming eras, both keyed by the level number.  2016: '10a.png' is the
    start, '10b.png' and '10c.png' the goals.  Later: 'ChallengeI_1901.png' is
    the start and the goals are either 'ChallengeI_1901b.png' or one
    'ChallengeI_1901_G4.png' per flag -- collection, level, and *which flag was
    reached in what order*, in the game's own column-row notation.  So for a
    multi-flag level the subgoal sequence needs no pixel decoding at all.

    **Where a post carries both spellings the bare one is the start**, and
    'Na.png' is the first goal frame.  35 posts do, and this read it the other
    way round until session 37: both spellings assigned `start` and whichever
    the post listed last won.  Those 35 are 34 of the 37 play states `codebook`
    then took for start boards -- which is where its 982 conflicts and all 13
    of its wrong labels came from.  Checked four ways: on LaserTank 1024,
    Sokoban-I 141, Special-I 343 and Challenge-II 247 the bare name decodes to
    the .lvl exactly, tank on its start cell facing up, and 'Na.png' does not.

    **A claim that names another level or another collection is dropped, and
    reported.**  'LaserTank_452.png' in the Sokoban-I 452 post is another
    level's screenshot and was 201 conflicts on its own; 'SokobanI_1081.png' in
    the Sokoban-I 1080 post is a neighbouring number under a title that reads
    1080, and no rule separates the two cases -- decoding the picture against
    both candidates does, and playing the level does, and reading the filename
    does not.  So the picture is dropped and `complete` asks for a word: one
    `image_level` line (a number the author typo'd) or `image_coll` line (a
    prefix) in bench/post-fixups.json, which is why those fields exist.
    Dropping is not the same as ignoring: these come back as `misnamed`, and
    before they were reported they were 21 posts silently losing a frame
    apiece -- 21 that split almost evenly, 12 typos and 9 posts that really do
    carry a neighbouring level's screenshot.

    **The frame tag is whatever the filename has left** (`frame_tag`), which is
    the rule that costs nothing and was worth 164 frames: `502_1.png`,
    `36b1.png`, `173b_4.png`, `SokobanI_620_A1_2.png`, `SpecialI_431_end.png`
    and `LaserTank_1230bb.png` are all goal frames of the level they name, and
    a stricter reading dropped every one of them.  It also drops the prefix the
    flag-cell spelling used to require, which is what hid the 2016 era's own
    '476_P3.png'.

    **Pictures the filenames do not account for fill the roles the filenames
    left open, in the post's own document order.**  Three shapes need this and
    they are one rule: a post whose pictures carry no filename at all
    (`/img/a/<blob>=s609`, 242 levels, every one of which contributed
    *nothing*); a post that mixes named pictures with filename-less ones, where
    the start is the unnamed one and 'SokobanI_942b.png' is the goal; and a
    post where two *different* pictures make the same claim, which is the
    author saving both screenshots as `<coll>_<n>.png` -- the claim cannot be
    honoured for either, so both fall through to here and the first is the
    start.  This is the one guess in the file and it is **arbitrated where the
    pixels are**: `codebook` admits a start board only if the tank is on the
    `.lvl`'s own `T` cell facing up, so a post listed the other way round is
    rejected there rather than teaching 256 wrong labels -- and it is rejected
    *by name*, because `fetch` records which rule picked the start.  What the
    fallback never does is overrule a filename: a claim that stands is
    honoured, and only what is left over is placed by order.
    """
    # What the *filenames* say, which is what the title says except in the
    # posts `fixup` corrects: `image_level` for a number the author typo'd,
    # `image_coll` for a prefix, and they are separate fields because they are
    # separate mistakes -- 'SokobanI_234_B7_C6.png' in the Sokoban-I 236 post
    # is the first and 'LaserTank_431_H3.png' in the Sokoban-I 431 post is the
    # second.  Both **widen** rather than replace: the same post carries
    # 'SokobanI_236.png' spelled right, and a fixup that renamed the level out
    # from under it would trade the goal frames for the start.
    pref = name_prefixes(coll) | (name_prefixes(image_coll)
                                  if image_coll else set())
    nums = {n} | ({image_level} if image_level is not None else set())
    # Three states per picture, and the difference between two of them is the
    # whole point: None is a picture that claims nothing and can be placed,
    # False is one whose claim is about something else and cannot.
    seq, claims, misnamed = [], collections.defaultdict(list), []
    for name, url in pics:
        if name is None:
            seq.append((None, url))
            continue
        c = claim(name)
        role = None
        if c and c[1] in nums and (not c[0] or c[0] in pref):
            rest = c[2]
            role = ("" if not rest else "a" if rest.lower() == "a"
                    else frame_tag(rest) or None)
        if role is None:
            misnamed.append(name)
            seq.append((False, url))
            continue
        claims[role].append(url)
        seq.append((role, url))
    # A claim counts only where it is the only one: two pictures spelling the
    # same frame have made the claim useless, not true twice.
    one = {t: u[0] for t, u in claims.items() if len(u) == 1}
    start = one.get("") or one.get("a")
    order = "filename" if start else None
    goals = [(t, u) for t, u in one.items() if t not in ("", "a")]
    # Demoted rather than dropped: it is a real goal frame and the only reason
    # it cannot keep the post's own 'a' is that the start is already written as
    # `<coll>_<n>_a.png`.  'a1' is that collision and nothing else.
    if "" in one and "a" in one:
        goals.append(("a1", one["a"]))
    left = [(role, u) for role, u in seq
            if role is None or (role is not False and len(claims[role]) > 1)]
    # 25 because that is where the letters run out, and because a post carrying
    # more unaccounted-for pictures than that is not one this rule understands.
    if len(left) <= 25:
        if start is None and left:
            start, order = left.pop(0)[1], "document"
        # In the post's order, and a picture that lost a claim keeps that
        # claim's letter where it is free: '36c.png' twice is 'c' and 'd', not
        # 'b' and 'c' in front of the post's own b1..b7.
        used = {t for t, _ in goals}
        prev = ""
        for role, u in left:
            t = role if role and role not in used else next(
                (c for c in "bcdefghijklmnopqrstuvwxyz"
                 if c > prev and c not in used), None)
            if t is None:
                break
            used.add(t)
            prev = max(prev, t if len(t) == 1 else prev)
            goals.append((t, u))
    return start, sorted(goals), order, misnamed


def png_width(d):
    """A PNG's width from its IHDR, without decoding it.

    Eight bytes of signature, then the IHDR chunk's length and type, then
    width: so byte 16.  Cheap enough to check every download.
    """
    if not d.startswith(b"\x89PNG\r\n\x1a\n") or d[12:16] != b"IHDR":
        return 0
    return struct.unpack(">I", d[16:20])[0]


# The board needs 16*24 px plus its frame plus the window chrome, and both
# geometries the posts use are just over 600 wide (609x463, 619x473).  Anything
# narrower is a *resample*, which is the point of the check below.
MIN_W = 600


def fetch_one(url, dest, refetch=False):
    """Blogger serves each image under several size directories; ask for the
    original.  Some posts answer 404 or 500 on a rewritten size, so try those
    first and fall back to the URL exactly as the post gave it.

    **A size directory does not only crop the choice of rendition, it can
    resample the image**, and a resampled screenshot has no 24-pixel grid left
    in it -- which was both `NOFRAME`s of the first corpus-scale run.  Neither
    post is unusable and neither was resized by its author: `SpecialI_491.png`
    is 609x463 under `/s16000/` and 512x389 under the `/s1600/` the post itself
    links, and `SpecialI_343a.png` is 619x473 under `/s1600/` and 512x391 under
    its post's `/s619/`.  Blogger answers a small-size path with a downscale
    even when the number is larger than the image (491's own `s1600` > 609), so
    the *post's* URL is the last thing to ask, not the first -- which is what
    this docstring said all along while the code tried it first.

    So: rewrites first, and a rendition narrower than a LaserTank window is not
    accepted while a candidate is still untried.  The widest of a bad set is
    still written rather than dropped, because `origin` reporting NOFRAME on a
    picture that is on disk beats having no picture to look at.

    **Both URL shapes get rewritten, which they did not.**  The old one keeps
    the size in a path segment (`/s1600/<name>.png`); the newer filename-less
    one keeps it in a suffix (`<blob>=s609`), and rewriting `p[-2]` on that
    replaces the literal `a` of `/img/a/` and asks for a URL that does not
    exist.  Every one of those levels therefore fell straight through to the
    post's own rendition -- which is the smallest one, and the one this
    docstring exists to say should be asked for last.
    """
    if dest.exists() and not refetch:
        return True
    p = url.split("/")
    tries = []
    for s in ("s16000", "s1600"):
        if SIZED.search(p[-1]):
            q = list(p)
            q[-1] = SIZED.sub("=" + s, p[-1])
        elif p[-2] != s:
            q = list(p)
            q[-2] = s
        else:
            continue
        if "/".join(q) != url:
            tries.append("/".join(q))
    tries.append(url)
    best = None
    for u in tries:
        try:
            d = get(u)
        except urllib.error.HTTPError:
            continue
        time.sleep(0.05)
        w = png_width(d)
        if best is None or w > best[0]:
            best = (w, d)
        if w >= MIN_W:
            break
    if best is None:
        return False
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(best[1])
    return True


def selected(args, by_level):
    keys = sorted(by_level)
    if args.levels:
        want = set()
        for spec in args.levels:
            coll, _, ns = spec.partition(":")
            for n in ns.split(","):
                want.add((coll, int(n)))
        return [k for k in keys if k in want]
    if args.collection:
        keys = [k for k in keys if k[0] == args.collection]
    random.Random(args.seed).shuffle(keys)
    return keys[:args.limit] if args.limit else keys


def img_path(coll, n, tag):
    return OUT / "img" / ("%s_%d_%s.png" % (coll.replace("-", ""), n, tag))


def manifest():
    p = OUT / "fetched.json"
    if not p.exists():
        raise SystemExit("no %s -- run: python tools/harvest.py fetch" % p)
    return json.loads(p.read_text())


def cmd_fetch(args, out=None):
    """Download one start frame and every goal frame each level's posts carry.

    **Every part of a multi-part post is the same level and the same start
    screenshot**; what differs is which flags its goal frames are of.  This read
    `by_level[...][0]` and the feed is newest-first, so an 11-part level
    contributed part 11's two frames and dropped the other 38 -- `Challenge-I`
    306 is that level, and 83 levels lost 518 frames between them.  The final
    board survived, because `bank` orders by flags still on the board rather
    than by the post's layout, so what was lost was not the target but the
    **per-flag subgoal sequence** that ordering exists to carry.

    So: parts ascending, first part wins a tag collision (17 of the 535 collide,
    all of them a genuine reshoot of the same flag), and each goal frame keeps
    the url of the post it came from so a `bank` refusal names the right one.
    """
    by_level = posts_by_level()
    got = collections.Counter()
    # Merge rather than replace: a `--levels LaserTank:10` after a `--limit 150`
    # must not strand the codebook the 150 built.
    man = []
    keep = {}
    p = OUT / "fetched.json"
    if p.exists():
        for r in json.loads(p.read_text()):
            keep[(r["coll"], r["level"])] = r
    sel = selected(args, by_level)
    tick = Ticker("levels", len(sel))
    for coll, n in sel:
        # The level goes in the note: this is the one subcommand on the
        # network, so which post is being waited on is the useful half.
        tick.tick(note="  %s %d" % (coll, n))
        posts = sorted(by_level[(coll, n)],
                       key=lambda q: (q["part"], q["published"]))
        start = startorder = None
        goals, seen = [], set()
        for post in posts:
            s, gg, how, _ = pick_images(coll, n, post["imgs"],
                                        post["image_level"],
                                        post["image_coll"])
            if start is None and s:
                start, startorder = s, how
            for tag, url in gg:
                if tag not in seen:
                    seen.add(tag)
                    goals.append((tag, url, post))
        goals.sort(key=lambda t: t[0])
        rec = {"coll": coll, "level": n, "published": posts[0]["published"],
               "url": posts[0]["url"], "start": None, "goals": []}
        if start:
            # Which rule picked it, so `codebook` can say so when it rejects
            # one: a filename is a claim, an order is a guess.
            rec["order"] = startorder
        if start and fetch_one(start, img_path(coll, n, "a"), args.refetch):
            rec["start"] = str(img_path(coll, n, "a").relative_to(ROOT))
            got["start"] += 1
        else:
            got["no start image"] += 1
            # Which of the two it is decides whether anyone can do anything
            # about it: a post with no start screenshot is the blog's shape,
            # a download that failed is this tool's problem.
            if out is not None:
                out.setdefault("nostart", []).append(
                    (coll, n, rec["url"], "the post carries no start screenshot"
                     if not start else "the start screenshot did not download"))
        if args.goals:
            for tag, url, post in goals:
                if fetch_one(url, img_path(coll, n, tag), args.refetch):
                    rg = {"tag": tag,
                          "file": str(img_path(coll, n, tag).relative_to(ROOT))}
                    if post["url"] != rec["url"]:
                        rg["url"] = post["url"]   # a later part of this level
                    rec["goals"].append(rg)
                    got["goal"] += 1
        man.append(rec)
        keep[(coll, n)] = rec
    tick.done()
    OUT.mkdir(parents=True, exist_ok=True)
    all_recs = [keep[k] for k in sorted(keep)]
    p.write_text(json.dumps(all_recs, indent=1))
    for k, v in got.most_common():
        print("  %-16s %5d" % (k, v))
    print("manifest -> %s  (%d this run, %d in all)"
          % (p, len(man), len(all_recs)))
    if out is not None:
        out["levels"] = len(all_recs)
        out["start"] = sum(1 for r in all_recs if r["start"])
        out["goal frames"] = sum(len(r["goals"]) for r in all_recs)
    return 0


# ---------------------------------------------------------------- the decode

# The inner frame row's grey run, measured end to end, -> the sprite pitch it
# implies: 16 tiles of board plus 2 pixels of frame.  These are SetGameSize's
# three zooms (LTANK2.C:1729): 386 is the 24-pixel shrink GFXInit's StretchBlt
# produces, 514 the sheet's own 32-pixel cells, 642 the largest.  Measured over
# every image in the corpus: 12,498 at 386, 5 at 514, none at 642 -- which is
# in the table anyway, because recognising a zoom costs one derived table and
# beats decoding a fourth surprise into garbage.
FRAME_RUN = {16 * 24 + 2: 24, 16 * 32 + 2: 32, 16 * 40 + 2: 40}


def origin(w, h, px):
    """-> (board's top-left pixel x, y, sprite pitch), from the frame round it.

    The frame's horizontal lines are the only rows carrying a long run of
    (128,128,128), and the run's own start is the left frame; the second such
    row is the inner one.  bytes.find does the scan, which is why this costs a
    millisecond rather than a 300k-iteration pixel loop.

    **The run's own length is the measurement, and reading it as "at least 384"
    cost three goal boards.**  It is 16 tiles of board plus 2 pixels of frame,
    so it says what the sprite pitch is -- and three posts are not the 24-pixel
    build at all but the game at the sheet's own 32-pixel cells, where the run
    is 514 instead of 386.  `find` matched inside the longer run, a plausible
    corner came back, and all 256 tiles decoded to unknown with nothing in the
    output saying why.  Measuring the run end to end both refuses a window this
    cannot read and *identifies* the two it can, which is why the pitch is a
    return value rather than the module constant it used to be.
    """
    hits = []
    for pitch in sorted(FRAME_RUN.values()):
        run = GREY * 16 * pitch
        for y in range(h):
            row = bytes(px[y * w * 3:(y + 1) * w * 3])
            i = row.find(run)
            if i < 0:
                continue
            s = i
            while s >= 3 and row[s - 3:s] == GREY:
                s -= 3
            e = i
            while e + 3 <= len(row) and row[e:e + 3] == GREY:
                e += 3
            hits.append((y, s // 3, (e - s) // 3))
        if len(hits) >= 2:
            break
    if len(hits) < 2:
        raise ValueError("no board frame -- not a LaserTank window?")
    y, x, n = hits[1]
    if n not in FRAME_RUN:
        raise ValueError("board frame is %d px, not %s -- no whole-pixel sprite "
                         "grid under it" % (n, " or ".join(
                             str(k) for k in sorted(FRAME_RUN))))
    return x + 2, y + 1, FRAME_RUN[n]


def tile_hashes(path):
    """-> ({(cx, cy): tile hash}, (x0, y0), pitch).

    The pitch comes from the frame, not from a constant, so a screenshot of the
    32-pixel build hashes its own 32-pixel cells.  A hash is md5 over the whole
    tile, so the two pitches share one table without colliding: the byte
    strings are different lengths.
    """
    w, h, px = png.load(path)
    x0, y0, P = origin(w, h, px)
    out = {}
    for cy in range(16):
        for cx in range(16):
            b = bytearray()
            for yy in range(P):
                i = ((y0 + cy * P + yy) * w + x0 + cx * P) * 3
                b += px[i:i + P * 3]
            out[(cx, cy)] = hashlib.md5(bytes(b)).hexdigest()[:16]
    return out, (x0, y0), P


def show(g):
    print("    " + "".join(str(x % 10) for x in range(16)))
    for y in range(16):
        print(" %2d %s" % (y, "".join(g[y][x] for x in range(16))))


def cbpath():
    """The start-bootstrapped half: derivable, so it lives in gitignored build/."""
    return OUT / "codebook.json"


STALE_README = (
    "The levels whose blog post is of a level the .lvl no longer matches -- "
    "tools/harvest.py codebook.  The post's start screenshot decodes to a "
    "board that is a genuine start position (tank on the .lvl's own T cell, "
    "so not a play state -- the facing is free, since a turn in place changes "
    "no PF) and yet disagrees with the .lvl, and "
    "tools/sprites.py -- which owes nothing to either side -- confirms the "
    "picture at every disagreeing cell.  The level was re-authored some time "
    "after the post.  **The consequence is on the goal boards, not the start "
    "one**: a goal from a superseded revision is a target the current level "
    "cannot reach, so `bank` refuses these rather than handing the solver a "
    "board to chase forever."
)


def stalepath():
    """Derivable from the corpus and the images, so it is gitignored too."""
    return OUT / "stale.json"


def labelpath():
    """The hand-labelled half: *not* derivable, so it is committed.

    A start screenshot labels itself, because the corpus already knows that
    board.  Nothing labels the states only play produces -- the tank facing
    anywhere but up, a pushed anti-tank, a block sunk in water -- so those are
    human input, and human input in a gitignored directory is input the project
    does not have.  Same rule bench/ exists for.
    """
    return ROOT / "bench" / "goal-tiles.json"


def load_codebook(derived=True):
    """One hash -> PF map from three sources, and where each tile came from.

    In precedence order, lowest first:

      * the **start-bootstrapped** half, `build/harvest/codebook.json` --
        derivable from the corpus, so gitignored;
      * the **hand-labelled** half, `bench/goal-tiles.json` -- committed;
      * the **derived** table, `tools/sprites.py` -- the game's own sprite sheet
        composited the way `UpDateSprite` composites it, which covers every
        state play produces and needs no labelling at all.  It goes last
        because it is the only one of the three that also knows where the tank
        is, and because it is the only one with the engine's `PF` rules in it:
        the start-bootstrapped half labels the tank cell `T` (the `.lvl` stores
        1 there) where the engine clears `PF` at that cell, `Engine.cs:348`.

    Returns `(cb, tank, counts)` -- `tank[hash]` is "up"/"right"/"down"/"left"
    for the tiles the tank is standing on, and absent for every other tile.
    """
    if not cbpath().exists():
        raise SystemExit("no codebook -- run: python tools/harvest.py codebook")
    cb = json.loads(cbpath().read_text())
    counts = {"start": len(cb), "hand": 0, "derived": 0}
    if labelpath().exists():
        hand = {k: v for k, v in json.loads(labelpath().read_text()).items()
                if not k.startswith("_")}
        cb.update(hand)
        counts["hand"] = len(hand)
    tank = {}
    if derived:
        import sprites
        before = set(cb)
        # One table per pitch the frame gate accepts, merged: a tile hash is
        # md5 over the whole cell, so a 24-pixel tile and a 32-pixel one cannot
        # collide -- the byte strings are different lengths.
        for pitch in sorted(set(FRAME_RUN.values())):
            c = sprites.Cells(pitch=pitch)
            for hs, (pf, facing, _) in c.table().items():
                cb[hs] = pf
                if facing:
                    tank[hs] = facing
            # The facing is derivable even where the PF is not, so a cell no
            # table can label still says where the tank is: sprites.facings().
            tank.update(c.facings())
        counts["derived"] = len(set(cb) - before)
    return cb, tank, counts


def cmd_codebook(args, out=None):
    """Bootstrap the codebook from start screenshots.

    For every post the collection and level are known from its title, so a
    start screenshot is 256 *labelled* tiles for free and no codebook has to be
    built by hand.  Each board is decoded against the codebook built from the
    boards *before* it, so the printed curve is an honest saturation curve and
    a disagreement is a hard error rather than a self-fulfilling one.

    **Whether the picture is a start position at all is checked first**, and
    that check is the whole of session 37's fix.  The labels here come from the
    `.lvl`, so a picture taken mid-solution labels every state play produced
    with what was there *before* play -- a block sunk in water gets labelled
    `~`, a destroyed anti-tank gets labelled `v` -- and `setdefault` makes the
    first such board win for good.  37 of the 5,780 were play states and they
    taught 13 wrong labels, every one of the 13 (traced back one at a time).
    They also caused most of the 982 conflicts, because a wrong label is a
    conflict on every later board that shows the tile honestly.

    The test is the tank: on a start board it is on the cell the `.lvl` stores
    as `T`, which is in the pixels.  That needs `tools/sprites.py`, which is the
    one thing here that does not come from the corpus -- but it is used only to
    *admit* a board, never to label a tile, so the curve below is still built
    from `.lvl` labels alone and is still honest.

    **Which derivation to test it against is read off the picture, not
    assumed**, and assuming it named two good start boards `NOT A START`.  This
    built one table from `sprites.Cells()` -- the internal sheet at the default
    24-pixel pitch -- and asked all 6,022 frames about it, so a screenshot of
    the 32-pixel zoom or of a shipped `.ltg` pack had no tank in it by
    construction: `LaserTank` 1126 is the first, 1619 is both, and each decodes
    to its `.lvl` with 0 unknown and 0 differing cells once asked properly.
    `frame_sheet` does the asking, and the seat comes from `sprites.facings()`
    rather than from the table for the same reason session 39 gave: the only
    question here is *which cell* the tank is on, and a tank standing on an
    anti-tank is PF-ambiguous while its facing is not.

    **The cell is the test; the facing is not, and requiring it cost six
    boards.**  A turn in place is a move that changes no `PF` whatever, so a
    screenshot taken after one is still a picture of the start position -- and
    turning before the capture is an ordinary thing for a player to do.  The
    check used to reject a non-up facing before it ever looked at the cell, and
    on the full run that named six: `Challenge-V` 743 and `LaserTank` 19, 223,
    283, 385 and 498, every one of which decodes to its `.lvl` with **0
    unknown and 0 differing cells** -- the tank on its own `T` cell, turned.
    What that cost was not labelling (their one new tile is their own turned
    tank cell, which the derived sheet covers anyway) but the *checks a
    rejected board skips*: those six levels were never conflict-checked
    against the `.lvl`, so a re-authored one among them would have reached
    `bank` as a live target.  A tank *off* the `T` cell is still a play state
    and still rejected, which is what `LaserTank` 521 really was -- reported as
    "tank facing right" where the tank is 20 cells and a whole solve away from
    its start.

    **A conflict is arbitrated rather than merely counted**, and the same sheet
    does it.  A disagreement between the codebook and the `.lvl` at a cell has
    exactly two causes, and they point opposite ways: either the codebook's
    label is wrong, or the *screenshot* is of a level the `.lvl` no longer
    matches -- the blog is nine years old and levels have been re-authored
    under it.  `sprites.py` decides which, because it derives that hash from
    the game's own graphics and owes nothing to either side.  All 46 cells of
    the first corpus-scale run came back STALE: the picture is right, the
    codebook keeps the right label, and the level has moved on.

    A STALE board is then not allowed to *teach*.  `setdefault` makes the first
    board to show a tile own it for good, so a stale board that came early
    would poison a label with no conflict to show for it -- the honest boards
    conflict, not the one that lied.  Ordering luck is not a check, so a board
    that disagrees is excluded from the labelling pass altogether, exactly as a
    play state is.  The levels are written to `stale.json` because a stale
    *start* board means the post's *goal* boards are goals for a level revision
    that no longer exists: unreachable targets, which is `bank`'s problem.
    """
    import sprites
    have = [r for r in manifest() if r["start"] and (ROOT / r["start"]).exists()]
    if args.limit:
        have = have[:args.limit]
    cb = json.loads(cbpath().read_text()) if args.extend and cbpath().exists() else {}
    conflicts = []
    notstart = []
    exact = noframe = turned = 0
    alt = collections.Counter()
    curve = []
    tick = Ticker("start boards", len(have))
    for k, r in enumerate(have):
        tick.tick(note="  codebook %d, conflicts %d" % (len(cb), len(conflicts)))
        B = board(r["coll"], r["level"])
        if B is None:
            continue
        try:
            t, _, pitch = tile_hashes(ROOT / r["start"])
        except Exception as e:
            noframe += 1
            tick.done()
            print("  NOFRAME %s: %s\n           %s" % (r["start"], e, r["url"]))
            continue
        # Which zoom and which graphics pack this picture is of, read off the
        # picture: see `frame_sheet` for the two start boards that assuming
        # either one cost.
        pk, sheet, facing = frame_sheet(t, pitch)
        if pk or pitch != sprites.PITCH:
            alt[(pk or "internal", pitch)] += 1
        seat = [(cx, cy, facing[hs]) for (cx, cy), hs in t.items() if hs in facing]
        # **The cell is the test and the facing is not.**  A turn in place is a
        # move that changes no PF at all, so a picture taken after one is still
        # a start position -- see the docstring for the six this used to reject.
        why = ("no tank in the picture" if not seat else
               "%d tanks in the picture" % len(seat) if len(seat) > 1 else
               "tank off the .lvl start cell"
               + (", facing %s" % seat[0][2] if seat[0][2] != "up" else "")
               if B[seat[0][1]][seat[0][0]] != "T" else None)
        if why:
            # `pick_images`' document-order fallback is the one guess in the
            # chain, and this is where it is arbitrated -- so say which rule
            # picked the frame that failed, or the reader cannot tell a play
            # state from a post listed goal-first.
            if r.get("order") == "document":
                why += " [order-picked]"
            notstart.append((r["coll"], r["level"], why, r["url"]))
            continue
        turned += seat[0][2] != "up"
        unk = bad = 0
        for (cx, cy), hs in t.items():
            true = B[cy][cx]
            got = cb.get(hs)
            if got is None:
                unk += 1
            elif got != true:
                bad += 1
                v = sheet.get(hs)
                who = ("the .lvl" if v and v[0] == got else
                       "the codebook" if v and v[0] == true else
                       "unarbitrated")
                conflicts.append((r["coll"], r["level"], cx, cy, got, true,
                                  hs, who))
        if not unk and not bad:
            exact += 1
        curve.append((len(curve) + 1, len(cb), unk, bad))
        # A board that disagrees teaches nothing: see the docstring.  Its own
        # unknown cells are exactly the ones no later board can contradict.
        if bad:
            continue
        for (cx, cy), hs in t.items():
            cb.setdefault(hs, B[cy][cx])
    tick.done()
    order = sum(1 for c in notstart if c[2].endswith("[order-picked]"))
    print("start boards: %d  (no frame: %d, not a start position: %d%s%s)"
          % (len(curve), noframe, len(notstart),
             ", of which %d order-picked" % order if order else "",
             "; %d admitted with the tank turned in place" % turned
             if turned else ""))
    for (pk, pitch), n in sorted(alt.items()):
        print("  read against %s at %d px: %d board%s"
              % (pk, pitch, n, "" if n == 1 else "s"))
    for coll, lvl, why, url in notstart:
        print("  NOT A START  %-14s %5d  %-44s %s" % (coll, lvl, why, url))
    print("  n  codebook  unknown  conflict")
    for n, sz, unk, bad in curve:
        if n <= 10 or unk or bad or n % 25 == 0 or n == len(curve):
            print("%3d  %8d  %7d  %8d" % (n, sz, unk, bad))
    last = max([n for n, _, u, _ in curve if u] or [0])
    print("\ndecoded exactly against earlier boards only: %d of %d" % (exact, len(curve)))
    print("last board that taught the codebook a new tile: %d of %d" % (last, len(curve)))
    # By board, because a conflict is a property of a board and not of a cell:
    # one disagreeing screenshot used to fill the whole list with its own cells.
    per = collections.Counter((c[0], c[1]) for c in conflicts)
    print("codebook entries: %d   conflicts: %d cells on %d boards"
          % (len(cb), len(conflicts), len(per)))
    urls = {(r["coll"], r["level"]): r["url"] for r in have}
    stale = []
    for (coll, lvl), nc in per.most_common():
        cells = [c for c in conflicts if (c[0], c[1]) == (coll, lvl)]
        blame = collections.Counter(c[7] for c in cells)
        # STALE only if the sheet blamed the .lvl for *every* cell: one cell it
        # could not arbitrate is a board to go and look at, not a verdict.
        is_stale = list(blame) == ["the .lvl"]
        print("  %-8s %-14s %5d  %2d cells  %s  %s"
              % ("STALE" if is_stale else "CONFLICT", coll, lvl, nc,
                 "the .lvl no longer matches the picture" if is_stale
                 else ", ".join("%d %s" % (n, w) for w, n in blame.most_common()),
                 urls.get((coll, lvl), "")))
        for c in cells[:10]:
            print("      (%2d,%2d) codebook %s  corpus %s  %s  sheet blames %s"
                  % c[2:])
        if is_stale:
            stale.append({"coll": coll, "level": lvl, "cells": nc,
                          "url": urls.get((coll, lvl), "")})
    cbpath().write_text(json.dumps(cb, indent=0, sort_keys=True))
    print("codebook -> %s" % cbpath())
    write_json(stalepath(), {"_README": STALE_README,
                             "levels": sorted(
                                 stale, key=lambda s: (s["coll"], s["level"]))})
    print("stale levels -> %s  (%d)" % (stalepath(), len(stale)))
    if args.goals:
        # count the hand-labelled half as covered: the residual is what is
        # left to do, not what the bootstrap alone happens to reach
        merged = dict(cb)
        if labelpath().exists():
            merged.update({k: v for k, v in
                           json.loads(labelpath().read_text()).items()
                           if not k.startswith("_")})
        goal_residual(merged)
    if out is not None:
        out["start boards"] = len(curve)
        out["turned in place"] = turned
        out["notstart"] = notstart
        out["stale"] = stale
        out["unarbitrated"] = [c for c in conflicts if c[7] != "the .lvl"]
    # A STALE board is a finding, not a failure: the codebook came out right at
    # every one of its cells and `stale.json` carries the consequence on to
    # `bank`.  What still fails the gate is a conflict the sheet blames on the
    # codebook, or one it cannot arbitrate at all.
    return 1 if [c for c in conflicts if c[7] != "the .lvl"] else 0


def goal_residual(cb):
    """What a start-bootstrapped codebook does *not* cover, and why.

    A start board only ever shows authored states: the tank faces up, no laser
    is in flight, and no block has been pushed anywhere.  Everything play
    produces is therefore goal-only, and this is the size of that set -- the
    remaining cost of the item.  It is a labelling job rather than an
    ambiguity, because BMF -> PF is many-to-one and PF is the half the solver
    wants: the commonest of them all is a block pushed into water, which
    Engine.cs:731 records as PF = 0 with BMF = 19.
    """
    files = [(r["coll"], r["level"], g["tag"], ROOT / g["file"], r["url"])
             for r in manifest() for g in r["goals"]]
    unk = collections.Counter()
    per = collections.Counter()
    n = noframe = 0
    tick = Ticker("goal boards", len(files))
    for coll, lvl, tag, f, url in files:
        tick.tick(note="  %d sprites the start boards never label" % len(unk))
        if not f.exists():
            continue
        try:
            t, _, _ = tile_hashes(f)
        except Exception as e:
            noframe += 1
            tick.done()
            print("  NOFRAME %-14s %5d %-4s  %s  %s" % (coll, lvl, tag, e, url))
            continue
        n += 1
        u = sum(1 for hs in t.values() if hs not in cb)
        for hs in t.values():
            if hs not in cb:
                unk[hs] += 1
        per["0" if u == 0 else "1" if u == 1 else "2" if u == 2 else
            "3-5" if u <= 5 else "6-10" if u <= 10 else ">10"] += 1
    tick.done()
    if not n:
        print("\nno goal images -- run fetch with --goals")
        return
    print("\ngoal boards decoded: %d  (no frame: %d)" % (n, noframe))
    print("goal-only sprites the start boards never label: %d"
          % len(unk))
    if unk:
        print("  instances: %d of %d tiles (%.2f%%);  commonest one alone: %d (%.1f%%)"
              % (sum(unk.values()), n * 256, 100.0 * sum(unk.values()) / (n * 256),
                 unk.most_common(1)[0][1],
                 100.0 * unk.most_common(1)[0][1] / max(1, sum(unk.values()))))
    print("unknown cells per goal board:")
    for k in ("0", "1", "2", "3-5", "6-10", ">10"):
        if per[k]:
            print("  %-5s %4d boards (%.1f%%)" % (k, per[k], 100.0 * per[k] / n))
    # This number is deliberately the *start-bootstrapped* half's own coverage
    # curve and nothing else, which is what makes it an honest saturation
    # measurement.  It is no longer this item's remaining cost: the sprites it
    # counts are derived from the game's own sheet, so the figure that matters
    # is `tiles`' 0.00%.  Do not "fix" this by folding the derived table in --
    # the two measure different things and both are worth keeping.
    print("\nthese are derived, not labelled: python tools/harvest.py tiles"
          " (`sheet` then `label` remains the loop for anything it misses)")


def cmd_label(args):
    """Merge the filled-in sidecar into the committed goal-tile table.

    **A `stated` record must not be labelled here, and refusing it is the
    point.**  `sheet` marks a residual tile `stated` when the answer lives in
    `bench/post-fixups.json`, and it lives there because the tile is
    *information-free*: the tank drawn on an anti-tank occludes the cell so
    completely that two different `PF` values give identical pixels, on that
    board and on every other.  So the hash means one thing on this post's frame
    and something else on the next one, and a hash table is the one place the
    answer cannot go.  Writing it here would be a per-board fact promoted to a
    global one -- silently wrong on some future board rather than loudly wrong
    now, which is why it is an error and not a warning.
    """
    side = OUT / "residual.json"
    if not side.exists():
        raise SystemExit("no %s -- run: python tools/harvest.py sheet" % side)
    rec = json.loads(side.read_text())
    filled = [r for r in rec if r.get("pf", "").strip()]
    bad = [r for r in filled if r.get("stated")]
    if bad:
        raise SystemExit(
            "%s: %s is answered per post in bench/post-fixups.json (%r at %s) "
            "and cannot be a codebook entry -- the cell is occluded past "
            "reading, so the same pixels mean different things on different "
            "boards.  Clear its \"pf\"."
            % (bad[0]["hash"], bad[0]["first_seen"], bad[0]["stated"],
               bad[0]["first_seen"].rsplit(" ", 1)[-1]))
    dup = [r for r in filled if r.get("derived")]
    if dup:
        print("%d of the %d filled-in sprites are already derived by "
              "sprites.py and need no label; merging them anyway is harmless "
              "but redundant: %s"
              % (len(dup), len(filled),
                 ", ".join("%s (%s)" % (r["hash"], r["derived"]) for r in dup)))
    if not filled:
        print("nothing filled in yet: put a PF symbol in each \"pf\" of %s" % side)
        print("the symbols are the ones dump_level.py prints: %s"
              % " ".join(sorted(set(SYM.values()))))
        return 0
    table = {}
    if labelpath().exists():
        table = json.loads(labelpath().read_text())
    table.setdefault("_README", (
        "hash -> PF symbol for the sprites a start screenshot can never label "
        "-- the states only play produces.  Hand input, which is why this is "
        "committed and build/harvest/codebook.json is not.  Symbols are "
        "dump_level.py's; write the PF value, not what the sprite looks like "
        "(a block sunk in water is drawn as a sunken block and is '.', per "
        "Engine.cs:731).  Built by tools/harvest.py sheet + label."))
    added = changed = 0
    ok = set(SYM.values()) | {str(i) for i in range(8)}
    for r in filled:
        pf = r["pf"].strip()
        if pf not in ok:
            raise SystemExit("%s: %r is not a PF symbol (%s)"
                             % (r["hash"], pf, " ".join(sorted(ok))))
        if r["hash"] not in table:
            added += 1
        elif table[r["hash"]] != pf:
            changed += 1
        table[r["hash"]] = pf
    labelpath().parent.mkdir(parents=True, exist_ok=True)
    labelpath().write_text(json.dumps(table, indent=1, sort_keys=True))
    print("%d labelled: %d new, %d changed -> %s"
          % (len(filled), added, changed, labelpath()))
    print("%d sprites in the table, %d still blank in the sidecar"
          % (len(table) - 1, len(rec) - len(filled)))
    return 0


def cmd_sheet(args):
    """Emit the goal-only sprites as one contact sheet, plus a sidecar to fill in.

    This is how the residual gets labelled, and it is a subcommand rather than
    a scratch script because that is the whole of the item's remaining phase 1:
    look at the sheet, write a `PF` symbol next to each hash in the sidecar,
    and commit the result.  Sprites come most-common first, so the work is
    front-loaded -- one of them is ~70% of all instances on its own.

    The symbol to write is the `PF` value, not what the sprite looks like: a
    block pushed into water is drawn as a sunken block and is `.`, because
    Engine.cs:731 records it as PF = 0 with BMF = 19 and the solver only ever
    wants the PF half.

    **A tile outside the codebook is not the same thing as a tile nobody can
    read, and conflating them asked for nine labels that were all already
    answered.**  `load_codebook` merges the internal sheet at every pitch the
    frame gate accepts -- and nothing else -- so this pass reported as
    unlabelled every tile whose answer lives somewhere the *codebook* does not
    go.  There are exactly two such places, and both are already the shipping
    answer for the boards concerned:

      * a **`.ltg` pack**, which `decode_board` has read since session 39.  The
        two EyeSaver+Grid goal frames in the corpus, `LaserTank` 726 `c` and
        1619 `b`, are banked with `unknown: 0` and contributed **5 of the 9
        sprites, 32 of the 36 instances** -- 28 of them one tile of 726's.
      * a **cell stated in `bench/post-fixups.json`**, which is where a tile
        that is *information-free* has to be answered: the tank drawn on an
        anti-tank occludes the cell so completely that two different `PF`
        values give identical pixels, so the answer cannot be a hash-table
        entry on any board, let alone this one.  Those are the other 4.

    So each residual tile is accounted for before it is asked about, and the
    sidecar records the accounting.  The sheet is only ever the tiles left
    over, because the sheet is a request for human work; the sidecar is the
    whole list, because `tiles`' second gate checks the accounting against
    `sprites.py` independently and a gate cannot check what it cannot see.
    """
    cb, _, n = load_codebook()
    if n["hand"]:
        print("%d sprites already hand-labelled in %s; this sheet is what is left"
              % (n["hand"], labelpath().relative_to(ROOT)))
    files = [(r["coll"], r["level"], g["tag"], ROOT / g["file"], r["url"])
             for r in manifest() for g in r["goals"]]
    seen = {}
    cnt = collections.Counter()
    acct = {}
    tick = Ticker("goal boards", len(files))
    for coll, lvl, tag, f, url in files:
        tick.tick(note="  %d unlabelled sprites" % len(seen))
        if not f.exists():
            continue
        try:
            w, h, px = png.load(f)
            x0, y0, P = origin(w, h, px)
        except Exception as e:
            tick.done()
            print("  SKIPPED %-14s %5d %-4s  %s  %s" % (coll, lvl, tag, e, url))
            continue
        stated = fixup(url).get("frames", {}).get(tag, {}).get("cells", {})
        for cy in range(16):
            for cx in range(16):
                b = bytearray()
                for yy in range(P):
                    i = ((y0 + cy * P + yy) * w + x0 + cx * P) * 3
                    b += px[i:i + P * 3]
                hs = hashlib.md5(bytes(b)).hexdigest()[:16]
                if hs in cb:
                    continue
                cnt[hs] += 1
                if hs not in seen:
                    seen[hs] = (bytes(b), coll, lvl, tag, cx, cy, P)
                if hs in acct:
                    continue
                at = "%s%d" % (chr(65 + cx), cy + 1)
                pack = derived_by(hs, P)
                if pack:
                    acct[hs] = {"derived": pack}
                elif at in stated:
                    acct[hs] = {"stated": stated[at]}
    tick.done()

    # Most-common first, so the work is front-loaded -- but the accounted-for
    # ones go last whatever their count, because the sheet's index is what a
    # human reads off it and the unanswered tiles are the point of the sheet.
    order = sorted(cnt, key=lambda h: (h in acct, -cnt[h]))
    todo = [h for h in order if h not in acct]
    for what, key in (("derived from a .ltg pack or another zoom", "derived"),
                      ("stated per post in bench/post-fixups.json", "stated")):
        got = [h for h in order if key in acct.get(h, {})]
        if got:
            print("%d of the %d unlabelled sprites are %s (%d of %d instances):"
                  % (len(got), len(order), what, sum(cnt[h] for h in got),
                     sum(cnt.values())))
            for h in got:
                print("  n=%-4d %s  %s at %s%d  -> %s"
                      % (cnt[h], h, "%s %d %s" % seen[h][1:4],
                         chr(65 + seen[h][4]), seen[h][5] + 1,
                         acct[h][key]))

    sheet = OUT / "residual.png"
    if todo:
        s, cols = args.scale, args.cols
        rows = (len(todo) + cols - 1) // cols
        # The cell is the largest pitch present, because the 32-pixel build's
        # tiles and the 24-pixel build's can both land in one residual.
        cell = max(seen[h][6] for h in todo)
        W, H = cols * cell * s, rows * cell * s
        out = bytearray(W * H * 3)
        for k, hs in enumerate(todo):
            tile, P = seen[hs][0], seen[hs][6]
            gx, gy = (k % cols) * cell * s, (k // cols) * cell * s
            for yy in range(P * s):
                for xx in range(P * s):
                    si = ((yy // s) * P + (xx // s)) * 3
                    di = ((gy + yy) * W + gx + xx) * 3
                    out[di:di + 3] = tile[si:si + 3]
        sheet.write_bytes(png.encode(W, H, out))

    # The sidecar is written even when there is nothing to label, because
    # `tiles`' second gate reads it and a gate that never runs is the defect
    # this file already caught once: it was silent through the whole
    # corpus-scale run and nothing in the output said so.
    side = OUT / "residual.json"
    rec = [dict({"i": k, "hash": hs, "n": cnt[hs], "pf": "",
                 "pitch": seen[hs][6],
                 "first_seen": "%s %d %s at %s%d"
                               % (seen[hs][1], seen[hs][2], seen[hs][3],
                                  chr(65 + seen[hs][4]), seen[hs][5] + 1)},
                **acct.get(hs, {}))
           for k, hs in enumerate(order)]
    write_json(side, rec)
    print("%d unlabelled sprites, %d instances -- %d of them accounted for, "
          "%d to label" % (len(order), sum(cnt.values()), len(acct), len(todo)))
    if todo:
        print("sheet   -> %s  (%dx%d, %d per row, index left-to-right "
              "top-to-bottom, scale %dx)" % (sheet, W, H, cols, s))
        print("sidecar -> %s  (fill in each \"pf\", then merge into "
              "codebook.json)" % side)
    else:
        print("sidecar -> %s  (nothing to fill in: every goal tile is either "
              "in the codebook, derived, or stated per post)" % side)
    return 0


_alt = {}


def pack_cells(pitch, pack):
    """One `(pitch, pack)` derivation -> `(table, facings)`, built once and kept.

    Lazy because the shipped packs cost 9 seconds for all of them against 0.7
    for the internal sheet alone, and 12,502 of the corpus's 12,503 images do
    not need them.

    Both halves come back because `facings()` has to be called on the same
    `Cells` that built the table -- it reads what `table()` accumulated -- and
    because the *start* gate needs the facings and not the table: a tank on an
    anti-tank is PF-ambiguous and dropped from the table, but its facing is
    not, and `codebook` only ever asks the pixels **which cell the tank is on**.
    """
    key = (pitch, pack)
    if key not in _alt:
        import sprites
        c = sprites.Cells(pitch=pitch, pack=pack)
        _alt[key] = (c.table(), c.facings())
    return _alt[key]


def pack_table(pitch, pack):
    """The derived table alone for one `(pitch, pack)`.  See `pack_cells`."""
    return pack_cells(pitch, pack)[0]


def derived_by(hs, pitch):
    """Which shipped derivation reads this tile at this pitch, or None.

    The **union** across the packs rather than a pick, because the only
    question here is whether *anything* derives the tile -- and the union is as
    safe as the pick for the same measured reason `decode_board` gives: across
    all eight `(pack, pitch)` tables no hash carries two different `PF` values,
    so a hash that is in a table has one meaning wherever it is found.
    """
    import sprites
    for name, pk in sprites.packs().items():
        if hs in pack_cells(pitch, pk)[0]:
            return name or "internal"
    return None


def derived_cell(hs):
    """A bare tile hash -> its derived `(PF, facing, note)` and where from.

    `(None, "")` if nothing derives it.  Searching every `(pack, pitch)` table
    for a hash that carries no pitch of its own is unambiguous, and both halves
    of that are measured rather than hoped for: a hash is md5 over the whole
    cell, so a 24-pixel tile and a 32-pixel one cannot collide -- the byte
    strings are different lengths -- and no hash carries two different `PF`
    values across the eight tables.  That is what lets `codebook.json` stay a
    flat hash -> PF map with no pitch in it.

    Lazy in the same way `pack_cells` is: an ordinary 24-pixel internal tile
    hits on the first table and costs one lookup.
    """
    import sprites
    for pitch in sorted(set(FRAME_RUN.values())):
        for name, pk in sprites.packs().items():
            v = pack_cells(pitch, pk)[0].get(hs)
            if v is not None:
                return v, "%s at %d px" % (name or "internal", pitch)
    return None, ""


def frame_sheet(t, pitch):
    """Which derivation a screenshot is actually of -> (pack name, table, seats).

    **Which graphics a picture is of is a property of the picture**, and the
    start gate used to assume both halves of the answer: it built one table
    from `sprites.Cells()` -- the internal sheet at the module's default pitch
    -- and asked every frame in the corpus about it.  Two good start boards
    were named `NOT A START` for it, both reported as "no tank in the picture"
    when the tank is plainly there:

      * `LaserTank` 1126 is the game at the sheet's own 32-pixel cells, which
        `origin` already measures off the frame and already hands back;
      * `LaserTank` 1619 is 32-pixel *and* `EyeSaver+Grid`, which
        `decode_board` has read since session 39 but this pass never tried.

    So the pitch comes off the frame and the pack is whichever derivation the
    tiles are in -- the most of them wins, the same rule and the same safety
    `decode_board` runs on: across all eight `(pack, pitch)` tables no hash
    carries two different `PF` values, so a match is a match and a wrong pack
    cannot out-cover the right one by inventing agreements.  The packs are
    tried only once the internal sheet has left something unread, which is
    `decode_board`'s policy too and is what keeps the 6,020 ordinary boards
    paying for one table lookup a tile instead of four.
    """
    import sprites
    tab, seats = pack_cells(pitch, None)
    best = (sum(1 for hs in t.values() if hs in tab or hs in seats),
            "", tab, seats)
    if best[0] < len(t):
        for name, pk in sprites.packs().items():
            if not pk:
                continue
            tab, seats = pack_cells(pitch, pk)
            n = sum(1 for hs in t.values() if hs in tab or hs in seats)
            if n > best[0]:
                best = (n, name, tab, seats)
    return best[1], best[2], best[3]


def decode_one(t, cb, tank):
    g = [["?"] * 16 for _ in range(16)]
    unk = collections.Counter()
    where = None
    for (cx, cy), hs in t.items():
        s = cb.get(hs)
        if s is None:
            unk[hs] += 1
        else:
            g[cy][cx] = s
        if tank and hs in tank:
            where = (cx, cy, tank[hs])
    return g, unk, where


def decode_board(path, cb, tank=None, packs=True):
    """One screenshot -> (PF board, unknown tiles, origin, tank, graphics pack).

    The tank is separate from the board on purpose: `PF` does not hold it --
    BuildBMField clears `PF` at the tank's cell on load (`Engine.cs:348`) -- so
    a cell the tank is standing on decodes to the terrain under it, and the
    tank comes back as `(x, y, facing)`.  That is exactly the shape a goal
    board wants, and it is what turned `LaserTank.lvl` 10's one undecoded cell
    into derived data.

    **A screenshot is not necessarily of the internal sheet**, and assuming it
    was cost `LaserTank` 1619 both of its frames: the post is of *EyeSaver+Grid*
    -- a `.ltg` pack this repo already ships under `data/graphics/` -- and every
    one of its 256 tiles came back unknown, teal where dirt is olive.  So when
    the internal table leaves unknowns behind, each shipped pack is tried and
    the best decode wins.  That is safe rather than a fishing expedition
    because it was measured first: across all eight `(pack, pitch)` tables,
    **no hash carries two different `PF` values**, so a match is a match and
    the fallback cannot invent an agreement.  The chosen pack comes back so the
    bank can record which graphics the picture was of.
    """
    t, org, pitch = tile_hashes(path)
    g, unk, where = decode_one(t, cb, tank)
    if unk and packs:
        import sprites
        best = (sum(unk.values()), "", g, unk, where)
        for name, pk in sprites.packs().items():
            if not pk:
                continue
            tab = pack_table(pitch, pk)
            cb2 = dict(cb)
            tk2 = dict(tank or {})
            for h, v in tab.items():
                cb2[h] = v[0]
                if v[1]:
                    tk2[h] = v[1]
            g2, unk2, where2 = decode_one(t, cb2, tk2)
            if sum(unk2.values()) < best[0]:
                best = (sum(unk2.values()), name, g2, unk2, where2)
        _, name, g, unk, where = best
        return g, unk, org, where, name
    return g, unk, org, where, ""


def cmd_tiles(args, out=None):
    """What the sprite-sheet derivation covers, against both other halves.

    This is the gate on `tools/sprites.py`, and it is a gate rather than a
    report because the derivation is only worth anything if it reproduces
    tiles it was not fitted to.  Three checks, cheapest first:

      * the **start-bootstrapped codebook**, whose labels come from the `.lvl`
        files and not from any sprite: every entry must come back with the same
        `PF`.  A `T` entry is not a clash -- the `.lvl` stores 1 at the tank's
        cell where the engine clears it (`Engine.cs:348`), so it must come back
        as the terrain plus a tank facing, and that reconciliation is checked.
      * the **goal residual**, the sprites a start board can never label.
      * every **goal board**, decoded end to end, as unknown cells per board.

    **Each of the three checks a tile against the derivation that tile is
    actually of**, which is what this file got wrong in three places at once.
    The first check used to be "against the 24-pixel table only, because that
    is the build every start screenshot in the corpus is of" -- and that
    premise was never true: `LaserTank` 1126 is the 32-pixel zoom and 1619 is
    32-pixel EyeSaver+Grid.  So a hash is looked up across every
    `(pack, pitch)` table, unambiguously, because none of them disagrees with
    another about a `PF` -- `derived_cell`.
    """
    import sprites
    c = sprites.Cells()
    t = c.table()
    for pitch in sorted(set(FRAME_RUN.values())):
        cc = c if pitch == PITCH else sprites.Cells(pitch=pitch)
        tt = t if pitch == PITCH else cc.table()
        print("derived from %s at %dx%d: %d cells with an unambiguous PF, "
              "%d dropped as PF-ambiguous"
              % (pathlib.Path(sprites.GAME_BMP).name, pitch, pitch, len(tt),
                 len(cc.conflicts())))

    cb = json.loads(cbpath().read_text()) if cbpath().exists() else {}
    agree = tankok = clash = absent = 0
    src = collections.Counter()
    for hs, pf in cb.items():
        # **Against the derivation the tile is actually of**, not against the
        # 24-pixel internal one.  Two start screenshots are of the 32-pixel
        # zoom and one of those is EyeSaver+Grid, and they teach 45 of this
        # table's entries between them: checked at 24 px internal, all 45 came
        # back `not derived` -- 48 lines of gate output saying nothing except
        # that the gate was looking in the wrong table.
        v, where = derived_cell(hs)
        if v is None:
            absent += 1
            print("  not derived: %s = %r" % (hs, pf))
            continue
        src[where] += 1
        if v[0] == pf:
            agree += 1
        elif pf == "T" and v[1] is not None:
            tankok += 1                     # the tank cell, reconciled
        else:
            clash += 1
            print("  CLASH %s: codebook %r, derived %r (%s)  [%s]"
                  % (hs, pf, v[0], v[2], where))
    if cb:
        print("start-bootstrapped codebook: %d of %d agree, %d tank cells "
              "reconciled, %d clash, %d not derived"
              % (agree, len(cb), tankok, clash, absent))
        for where, k in sorted(src.items()):
            print("    %3d against %s" % (k, where))

    side = OUT / "residual.json"
    if side.exists():
        rec = json.loads(side.read_text())
        # **At the sprite's own pitch and against every shipped pack**, which
        # is `sheet`'s rule and `decode_board`'s: checking a 32-pixel
        # EyeSaver+Grid tile against the internal 24-pixel table is a question
        # with a foregone answer, and it reported 5 of 9 sprites `not derived`
        # when every one of them is read by the pack the frame is of.
        hit = [r for r in rec if derived_by(r["hash"], r["pitch"])]
        rest = [r for r in rec if not derived_by(r["hash"], r["pitch"])]
        print("goal residual: %d of %d sprites derived, %d of %d instances"
              % (len(hit), len(rec), sum(r["n"] for r in hit),
                 sum(r["n"] for r in rec)))
        # An information-free cell is not a gate failure and never becomes
        # derivable: it is answered per post, and the gate's job is to say
        # which of the two it is looking at.
        open_ = [r for r in rest if not r.get("stated")]
        for r in rest:
            print("  %s: n=%-4d %s  %s%s"
                  % ("not derived" if r in open_ else "stated by hand",
                     r["n"], r["hash"], r["first_seen"],
                     "" if r in open_ else " -> %r" % r["stated"]))
        if rest and not open_:
            print("  -- every one occluded past reading, so stated in "
                  "bench/post-fixups.json rather than labelled: nothing open")
    else:
        # A gate that does not run has to say so.  This one was silent through
        # the whole corpus-scale run -- `sheet` writes residual.json and the run
        # never called it, so the middle of the three checks the docstring
        # promises simply was not there, and nothing in the output said so.
        print("goal residual: SKIPPED, no %s -- run: python %s sheet"
              % (side.relative_to(ROOT), pathlib.Path(__file__).name))

    if out is not None:
        out["clash"] = clash
        out["not derived"] = absent
        out["residual gate"] = side.exists()
        # A residual sprite nothing derives and nobody has stated is the one
        # thing this gate can find that needs a human, so it goes to the
        # report rather than staying in the log with the accounting.
        out["residual open"] = [r["first_seen"] for r in open_] \
            if side.exists() else []
    # **The third check is `bank`'s own pass, and running both costs it
    # twice.**  Decoding all 7,484 goal boards is 35 minutes and `bank` does
    # exactly the same decode -- so `complete` takes the two cheap gates here
    # and lets the bank pass report the boards, which is where the fixups and
    # the refusals are anyway.  Standalone `tiles` still runs all three: it is
    # the gate to run while working on sprites.py, where there is no bank.
    if getattr(args, "gate_only", False):
        return 1 if clash else 0

    cbfull, tank, n = load_codebook()
    print("\ndecoding every goal board against all three halves: %d start-"
          "bootstrapped + %d hand-labelled + %d derived = %d tiles"
          % (n["start"], n["hand"], n["derived"], len(cbfull)))
    # The frame's own url, not the level's: a multi-part post's later frames
    # each have one, and it is also the key bench/post-fixups.json is written
    # against -- so the level's url would both send the reader to a post the
    # picture is not in and miss the fixup that answers the cell.
    files = [(r["coll"], r["level"], g["tag"], ROOT / g["file"],
              g.get("url", r["url"]))
             for r in manifest() for g in r["goals"]]
    per = collections.Counter()
    unk = collections.Counter()
    nb = notank = answered = notankfx = 0
    named = []
    tick = Ticker("goal boards", len(files))
    for coll, lvl, tag, f, url in files:
        tick.tick(note="  %d unknown tiles" % sum(unk.values()))
        if not f.exists():
            continue
        try:
            g, u, _, where, pk = decode_board(f, cbfull, tank)
        except Exception as e:
            tick.done()
            print("  SKIPPED %-14s %5d %-4s  %s  %s" % (coll, lvl, tag, e, url))
            continue
        nb += 1
        notank += where is None
        unk.update(u)
        v = sum(u.values())
        # What `bank` will do with this board, said here rather than left for
        # the reader to cross-reference: an occluded cell is the one thing no
        # derivation reaches, so the honest report is not "4 unknown tiles" but
        # "3 of them already answered by hand, 1 open".  Counted per cell that
        # actually fills a '?', the same condition bank applies.
        fx = fixup(url).get("frames", {}).get(tag, {})
        fill = sum(1 for at in fx.get("cells", {})
                   if g[int(at[1:]) - 1][ord(at[0].upper()) - 65] == "?")
        tankfx = where is None and bool(fx.get("tank"))
        answered += fill
        notankfx += tankfx
        if v or where is None or pk:
            named.append((v, coll, lvl, tag, where, pk, url, fill, tankfx))
        per["0" if v == 0 else "1" if v == 1 else "2" if v == 2 else
            "3-5" if v <= 5 else "6-10" if v <= 10 else ">10"] += 1
    tick.done()
    if not nb:
        print("\nno goal images -- run fetch with --goals")
        return 0
    print("\ngoal boards decoded: %d;  unknown tiles %d of %d (%.2f%%);  "
          "%d distinct" % (nb, sum(unk.values()), nb * 256,
                           100.0 * sum(unk.values()) / (nb * 256), len(unk)))
    if sum(unk.values()):
        print("  of those, %d answered by bench/post-fixups.json, %d open "
              "(each one refuses its board in bank)"
              % (answered, sum(unk.values()) - answered))
    print("boards with no tank found: %d%s"
          % (notank, "  (%d answered by bench/post-fixups.json)" % notankfx
             if notankfx else ""))
    print("unknown cells per goal board:")
    for k in ("0", "1", "2", "3-5", "6-10", ">10"):
        if per[k]:
            print("  %-5s %4d boards (%.1f%%)" % (k, per[k],
                                                  100.0 * per[k] / nb))
    # A percentage is not a finding.  0.02% of tiles was one image with no
    # 24-pixel grid in it at all plus four capture artifacts, and the histogram
    # above could not say which -- so every board that is not clean is named,
    # worst first, with the post to go and look at.  `bank` refuses every one
    # of these unless bench/post-fixups.json answers it, which the note column
    # says per row: a row with no note is a row to go and look at.
    if named:
        print("every goal board that is not plain-and-clean, worst first:")
        for v, coll, lvl, tag, where, pk, url, fill, tankfx in sorted(
                named, reverse=True):
            note = ", ".join(
                (["%d of %d from a post fixup" % (fill, v)] if fill else [])
                + (["tank from a post fixup"] if tankfx else []))
            print("  %-14s %5d %-4s %3d unknown, tank %-12s %-16s %-26s %s"
                  % (coll, lvl, tag, v,
                     "not found" if where is None
                     else "%s%d %s" % (chr(65 + where[0]), where[1] + 1,
                                       where[2]),
                     pk or "", note, url))
    if args.out:
        pathlib.Path(args.out).write_text(json.dumps(
            {h: {"pf": v[0], "tank": v[1], "is": v[2]}
             for h, v in sorted(t.items())}, indent=1))
        print("table -> %s" % args.out)
    return 1 if clash else 0


# ------------------------------------------------------------------- the bank

def counters():
    """The Moves/Shots the post's own panel shows, hand-read.

    Every tile on the board is derivable from graphics this repo commits, and
    `tiles` derives all of them.  These two numbers are not: the panel is
    `TextOut(pdc, ContXPos+48, 207, itoa(Game.ScoreMove, ...))` (`LTANK.C:563`)
    in whatever system font the machine that took the screenshot had, so no
    composite of Game.BMP can draw them and nothing in this repo says what they
    are.  That makes them the one thing about a post that is genuinely hand
    input -- which is what bench/ is for, and why the reading session 34 made
    by eye is committed there instead of being lost.
    """
    p = ROOT / "bench" / "goal-counters.json"
    if not p.exists():
        return {}
    return {k: v for k, v in json.loads(p.read_text()).items()
            if not k.startswith("_")}


def write_json(path, obj):
    """json.dumps to `path`, LF, no BOM.

    pathlib's write_text translates newlines, so on Windows it turns every
    line of a committed file into a CRLF diff.  This repo is LF-only.
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(json.dumps(obj, indent=1) + "\n")


BANK_README = (
    "The blogspot goal boards, decoded -- tools/harvest.py bank.  One record "
    "per (collection, level): the board at the moment each flag was reached, "
    "in dump_level.py's PF symbols, row-major by y.  `goals` is ordered so "
    "that the FINAL board is last -- by flags still on the board, descending, "
    "which is derived from the pixels rather than from the post's layout, "
    "because the later-era image tags are cell names and not an order.  "
    "`tank` is separate from the board on purpose: BuildBMField clears PF at "
    "the tank's cell (Engine.cs:348), so a cell the tank stands on carries the "
    "terrain under it.  `moves`/`shots` are the post's own panel counters, "
    "hand-read from bench/goal-counters.json, and are null for a post nobody "
    "has read.  **A level solved against one of these is hint-assisted and "
    "must never enter the solver's headline rate.**"
)


def cmd_bank(args, out=None):
    """Decode every fetched goal screenshot into the bank the solver reads.

    This is the artefact `--goal-board` takes: (collection, level, goal PF,
    tank, moves, shots).  Everything in it is derived -- `tiles` labels the
    pixels and the corpus names the level -- except the two panel counters,
    which no graphic in this repo can draw; see counters().

    A board with an unknown cell in it is **refused** rather than banked with a
    hole.  A goal board is a ranking key, and a key with a '?' in it prices
    that cell as "already right" wherever the search happens to be: the one
    failure mode that is worse than having no key at all.  So is a board with
    **no tank in it**, which is the same defect one step earlier: every real
    goal frame shows the tank, so a frame without one is a picture of a moment
    the game never displayed and is wrong somewhere the decode cannot see.
    --allow-unknown banks both anyway, which is for looking at them, not for
    solving.

    **A level `codebook` found stale is refused for the same reason, one step
    worse.**  An undecoded cell prices one cell wrongly; a goal board from a
    level revision that no longer exists prices the whole board as reachable
    when it is not, so the key bottoms out above zero and the search chases it
    to the time limit.  14 of the 5,779 start boards in the corpus-scale run
    disagree with their `.lvl`; 11 are re-authored levels and are what
    `stale.json` holds.  A *missing* `stale.json` is a hard stop for the
    same reason a missing codebook is -- both come out of `codebook`, so a
    codebook without a stale list is one from before the check existed.
    --allow-stale banks anyway, which is for looking.
    """
    cb, tank, _ = load_codebook()
    hand = counters()
    stale = set()
    if stalepath().exists():
        stale = {(s["coll"], s["level"])
                 for s in json.loads(stalepath().read_text())["levels"]}
    elif not args.allow_stale:
        # A hard stop rather than a note, for the same reason load_codebook
        # stops on a missing codebook: the two come out of the same command, so
        # a codebook without a stale list is one from before the check existed
        # -- and banking an unreachable target is worse than banking nothing.
        raise SystemExit("no %s -- run: python tools/harvest.py codebook "
                         "(or --allow-stale to bank without the check)"
                         % stalepath())
    want = None
    if args.levels:
        want = set()
        for spec in args.levels:
            coll, _, ns = spec.partition(":")
            for n in ns.split(","):
                want.add((coll, int(n)))

    banked = []
    stat = collections.Counter()
    refused = []
    answered = []
    nogoal = []
    recs = [r for r in manifest()
            if want is None or (r["coll"], r["level"]) in want]
    tick = Ticker("levels", len(recs))
    for r in recs:
        tick.tick(note="  %d banked, %d refused"
                       % (stat["banked"], stat["unknown cells -- refused"]))
        if not r["goals"]:
            stat["no goal image in the post"] += 1
            nogoal.append((r["coll"], r["level"], "-",
                           "the post carries no goal screenshot", r["url"]))
            continue
        if (r["coll"], r["level"]) in stale and not args.allow_stale:
            stat["re-authored since the post"] += 1
            refused.append((r["coll"], r["level"], "-",
                            "stale: re-authored", r["url"]))
            continue
        boards = []
        for g in r["goals"]:
            f = ROOT / g["file"]
            if not f.exists():
                stat["image not fetched"] += 1
                continue
            # A frame from a later part of a multi-part post has its own url;
            # a refusal that printed the level's url sent the reader to a post
            # the picture is not in.
            src = g.get("url", r["url"])
            try:
                grid, unk, _, where, pk = decode_board(f, cb, tank)
            except ValueError as e:
                stat["no board grid"] += 1
                refused.append((r["coll"], r["level"], g["tag"], str(e), src))
                continue
            # The one thing the pixels cannot say, said by hand: see
            # bench/post-fixups.json.  Per *board*, so it is never a claim
            # about another board that happens to draw the same tile -- which
            # is exactly why it is not in the hash table beside it.
            fx = fixup(src).get("frames", {}).get(g["tag"], {})
            fixed = []
            for at, pf in fx.get("cells", {}).items():
                cx, cy = ord(at[0].upper()) - 65, int(at[1:]) - 1
                if grid[cy][cx] == "?":
                    grid[cy][cx] = pf
                    fixed.append("%s=%r" % (at.upper(), pf))
                    stat["cells filled from a post fixup"] += 1
            # **Where the holes are, not just how many.**  The cell is the
            # whole of what a human has to state, so a refusal that says "1
            # undecoded cells" and nothing more sends the reader back through
            # `decode` to find out which one -- twice, in two sessions.
            holes = ["%s%d" % (chr(65 + x), y + 1)
                     for y in range(16) for x in range(16) if grid[y][x] == "?"]
            u = len(holes)
            if where is None and fx.get("tank"):
                where = (fx["tank"]["x"], fx["tank"]["y"], fx["tank"]["dir"])
                fixed.append("tank=%s%d %s" % (chr(65 + where[0]),
                                               where[1] + 1, where[2]))
                stat["tank from a post fixup"] += 1
            if fixed:
                answered.append((r["coll"], r["level"], g["tag"],
                                 ", ".join(fixed), src))
            if u and not args.allow_unknown:
                stat["unknown cells -- refused"] += 1
                refused.append((r["coll"], r["level"], g["tag"],
                                "%d undecoded cell%s (%s)"
                                % (u, "" if u == 1 else "s",
                                   ", ".join(holes[:8])
                                   + (" ..." if u > 8 else "")), src))
                continue
            if where is None and not args.allow_unknown:
                # **A goal frame with no tank in it is a faulty capture**, and
                # that is a finding rather than a nuisance: every real one has
                # the tank somewhere, so its absence means the picture is of a
                # moment the game never displayed -- `Challenge-I` 1598 `b` is
                # one, and playing the level puts the tank at B11 facing right
                # where the screenshot has bare terrain.  Refused for the same
                # reason an undecoded cell is: the board is a ranking key, and
                # a key built from a frame that is wrong somewhere is worse
                # than no key.  (It used to bank, harmlessly only because
                # `tank` is the one bank field nothing reads yet -- Goal.cs
                # parses it into TankX and no ranking looks at it.)
                stat["no tank in the frame -- refused"] += 1
                refused.append((r["coll"], r["level"], g["tag"],
                                "no tank in the frame", src))
                continue
            stat["banked"] += 1
            if pk:
                stat["decoded against a .ltg pack"] += 1
            boards.append({
                "tag": g["tag"],
                "flags": sum(row.count("F") for row in grid),
                # Which graphics the screenshot was of, when it was not the
                # internal sheet.  Absent is the internal sheet.
                **({"pack": pk} if pk else {}),
                "unknown": u,
                "tank": None if where is None
                        else {"x": where[0], "y": where[1], "dir": where[2]},
                "pf": ["".join(row) for row in grid],
            })
        if not boards:
            continue
        # Final board last.  Reaching a flag removes it, and the screenshot is
        # taken with the tank one move short of the one it is reaching, so the
        # board with the fewest flags left on it is the last of the sequence.
        boards.sort(key=lambda b: (-b["flags"], b["tag"]))
        c = hand.get("%s:%d" % (r["coll"], r["level"]), {})
        banked.append({"coll": r["coll"], "level": r["level"],
                       "name": level_name(r["coll"], r["level"]),
                       "url": r["url"],
                       "moves": c.get("moves"), "shots": c.get("shots"),
                       "goals": boards})
    tick.done()

    dest = pathlib.Path(args.out) if args.out else OUT / "goals.json"
    # **The destination is overwritten and a worse result is reported, never
    # refused.**  `complete` writes the committed bench/goal-boards.json, so a
    # run that banks fewer levels than the file on disk is a regression in the
    # chain -- and guarding the write would hide it where the diff shows it.
    # What this owes the reader is the comparison, which is these two numbers.
    was = None
    if dest.exists():
        try:
            old = json.loads(dest.read_text())["levels"]
            was = [len(old), sum(len(r["goals"]) for r in old)]
        except (ValueError, KeyError, TypeError):
            was = None                          # not a bank; just overwrite it
    write_json(dest, {"_README": BANK_README, "levels": banked})
    for k, v in stat.most_common():
        print("  %-28s %5d" % (k, v))
    # The post itself, because a refusal is something to go and look at: the
    # counters alone never said *which* board, let alone where to see it.
    for coll, lvl, tag, why, url in refused:
        print("  REFUSED %-14s %5d %-4s %-30s %s" % (coll, lvl, tag, why, url))
    print("bank -> %s  (%d levels, %d boards, %d with counters)"
          % (dest, len(banked), sum(len(r["goals"]) for r in banked),
             sum(1 for r in banked if r["moves"] is not None)))
    if was:
        print("  it replaced a bank of %d levels and %d boards%s"
              % (was[0], was[1], "   *** FEWER LEVELS THAN BEFORE ***"
                 if len(banked) < was[0] else ""))
    if out is not None:
        out["dest"] = str(dest)
        out["was"] = was
        out["levels"] = len(banked)
        out["boards"] = sum(len(r["goals"]) for r in banked)
        out["refused"] = refused
        out["answered"] = answered
        out["nogoal"] = nogoal
        out["stat"] = dict(stat)
    return 0



NAMED = re.compile(r"([A-Za-z]+)_(\d+)_(\w+)\.png$")


def cmd_decode(args):
    cb, tank, n = load_codebook()
    print("codebook: %d tiles (%d bootstrapped from start boards, %d hand-labelled,"
          " %d derived from the sprite sheet)"
          % (len(cb), n["start"], n["hand"], n["derived"]))
    # 'ChallengeI' in a filename is 'Challenge-I' in data/levels/
    unhyphen = {c.replace("-", ""): c for c in
                (p.stem for p in (ROOT / "data" / "levels").glob("*.lvl"))}
    # The post the picture came from, so a disagreement can be looked at rather
    # than only read about.  Optional: `decode` works on any file, fetched or not.
    try:
        posturl = {(r["coll"], r["level"]): r["url"] for r in manifest()}
    except SystemExit:
        posturl = {}
    rc = 0
    for path in args.images:
        g, unk, org, where, pk = decode_board(path, cb, tank)
        print("=== %s  origin=%s  unknown tiles=%d  tank %s"
              % (path, org, sum(unk.values()),
                 "at (%d,%d) facing %s" % where if where else "not found"))
        show(g)
        m = NAMED.search(os.path.basename(path))
        if not (m and args.check):
            continue
        coll = args.collection or unhyphen.get(m.group(1), m.group(1))
        n, tag = int(m.group(2)), m.group(3)
        B = board(coll, n)
        if B is None:
            print("  no corpus board for %s %s" % (coll, n))
            continue
        # The tank's cell is not a mismatch: the .lvl stores 1 there and the
        # engine clears PF at it on load (Engine.cs:348), so the decode is
        # right to report the terrain plus a separate tank.  Reconcile it
        # rather than excusing it -- 'T' means the terrain under the tank is
        # dirt, so anything else there is still a real disagreement.
        diff = [(x, y, B[y][x], g[y][x]) for y in range(16) for x in range(16)
                if B[y][x] != g[y][x]
                and not (B[y][x] == "T" and g[y][x] == "."
                         and where and where[:2] == (x, y))]
        rec = ghs(coll, n)
        print("  %s %d %r   .ghs record %s" %
              (coll, n, level_name(coll, n),
               "%d moves %d shots" % rec if rec else "none"))
        if posturl.get((coll, n)):
            print("  %s" % posturl[(coll, n)])
        print("  %s: %d" % ("mismatches vs the corpus board" if tag == "a"
                            else "cells differing from the corpus board", len(diff)))
        for x, y, a, b in diff:
            print("    (%2d,%2d) corpus %s  image %s" % (x, y, a, b))
        if tag == "a" and diff:
            rc = 1
    return rc


# --------------------------------------------------------------- the one command

# Every way a post can fail to reach the bank, and whether anyone has to do
# anything about it.  `complete` prints these; the point of the table is the
# last column, so each entry says what would resolve the row and who can.
#   'why'    what the tool found, in the game's own A-P/1-16 coordinates
#   'open'   True means it is waiting on Michal -- nothing else can supply it
CLARIFY = "CLARIFY"

# The bank a full run produces, committed: bench/ is where this project's
# persistent solver artefacts live, and this one cannot be re-derived without
# the blog.  See bench/README.md for the rule that directory pays for.
BANK = ROOT / "bench" / "goal-boards.json"


def _phase_args(**kw):
    """A phase's own argv, as a namespace -- `complete` calls the same
    functions the subcommands do rather than a private copy of them."""
    return argparse.Namespace(**kw)


def complete_rows(ph, man):
    """The findings of every phase -> one row per post that the plain decode
    did not finish on its own, merged by (collection, level, frame).

    Merged because a post can fail twice and is still one post: the level that
    contributes *nothing* carries both "no start screenshot" and "no goal
    screenshot", and printing it twice would double a count the reader is
    reading precisely to know how many posts are affected.

    The two "no screenshot" populations come from the **manifest** rather than
    from `fetch`'s findings, so `--offline` reports them too -- a post that
    carries no start frame is a fact about the blog and does not need the
    network to notice.  `fetch`'s own rows are then only the *other* reason
    the file can be missing: a download that failed, which is this tool's
    problem and not the blog's shape.
    """
    rows = {}
    # The url the table prints for a row that carries none of its own:
    # `codebook`'s conflicts are keyed by level rather than by post, and a
    # row the reader cannot open is a row he has to go and search for.
    manurl = {(r["coll"], r["level"]): r["url"] for r in man}

    def add(kind, coll, lvl, tag, why, status, url, open_=False):
        url = url or manurl.get((coll, lvl), "")
        k = (coll, lvl, tag)
        r = rows.setdefault(k, {"kind": kind, "coll": coll, "level": lvl,
                                "tag": tag, "why": [], "status": [],
                                "open": False, "url": url})
        r["why"].append(why)
        r["status"].append(status)
        r["open"] = r["open"] or open_
        r["url"] = r["url"] or url
        return r

    mp, ft = ph.get("map", {}), ph.get("fetch", {})
    cb, bk = ph.get("codebook", {}), ph.get("bank", {})
    stale = {(s["coll"], s["level"]): s for s in cb.get("stale", [])}
    broke = {(c, l) for c, l, _, why in ft.get("nostart", [])
             if "did not download" in why}

    # One row per level rather than per post: Sokoban-I 236 is four parts and
    # four filenames apiece, and the question they ask is one question.
    mis = collections.OrderedDict()
    for coll, lvl, url, names in mp.get("misnamed", []):
        r = mis.setdefault((coll, lvl), {"url": url, "names": [], "claims": []})
        for name in names:
            r["names"].append(name)
            # The prefix as the author wrote it, not as `claim` folds it for
            # comparison: the row is read next to the filenames themselves.
            m = PIC.fullmatch(name.rsplit(".", 1)[0].rstrip("."))
            w = ("%s %d" % (m.group(1) or coll.replace("-", ""),
                            int(m.group(2))) if m else name)
            if w not in r["claims"]:
                r["claims"].append(w)
    for (coll, lvl), r in mis.items():
        n = len(r["names"])
        add("misnamed", coll, lvl, "-",
            "%d picture%s dropped: the filenames name %s, the title says "
            "%s %d (%s)"
            % (n, "" if n == 1 else "s", ", ".join(r["claims"]), coll, lvl,
                      ", ".join(r["names"][:3])
                      + (", ..." if n > 3 else "")),
            "%s: if they are this level's frames after all, one image_level "
            "line in bench/post-fixups.json -- a wrong number is a typo, a "
            "wrong collection is another level's screenshot, and only playing "
            "it tells them apart" % CLARIFY, r["url"], True)
    for coll, lvl, name, url in mp.get("nocorpus", []):
        add("nocorpus", coll, lvl, "-",
            "the post names a level this corpus does not ship",
            "%s: retarget the post in bench/post-fixups.json, or ship the "
            "collection" % CLARIFY, url, True)
    for r in man:
        if r["start"]:
            continue
        # A post with no start screenshot is the blog's shape and costs only
        # the staleness check; a download that failed is this tool's problem.
        bad = (r["coll"], r["level"]) in broke
        add("nostart", r["coll"], r["level"], "-",
            "the start screenshot did not download" if bad
            else "the post carries no start screenshot",
            "%s: the fetch failed, not the blog" % CLARIFY if bad
            else "intentional: nothing to label from, and no staleness check "
                 "for this level", r["url"], bad)
    for coll, lvl, why, url in cb.get("notstart", []):
        add("notstart", coll, lvl, "-",
            "start frame is not a start position (%s)" % why,
            "intentional: a play state teaches wrong labels, so it is not "
            "learned from -- and no staleness check for this level", url)
    for coll, lvl, tag, why, url in bk.get("nogoal", []):
        add("nogoal", coll, lvl, tag, why,
            "intentional: there is no board to bank", url)
    for coll, lvl, tag, what, url in bk.get("answered", []):
        add("answered", coll, lvl, tag,
            "occluded cell, unreadable from any pixels",
            "intentional: banked using your line in bench/post-fixups.json "
            "(%s)" % what, url)
    for coll, lvl, tag, why, url in bk.get("refused", []):
        if why.startswith("stale"):
            s = stale.get((coll, lvl), {})
            add("stale", coll, lvl, tag,
                "the .lvl no longer matches the picture",
                "intentional: level re-authored after the post, %d cell%s, "
                "sprites.py blames the .lvl at every one -- the goal is a "
                "target this level cannot reach"
                % (s.get("cells", 0), "" if s.get("cells") == 1 else "s"),
                url)
        elif "undecoded cell" in why:
            add("undecoded", coll, lvl, tag, why,
                "%s: state the PF symbol for %s in bench/post-fixups.json"
                % (CLARIFY, why.split("(", 1)[1].rstrip(")")), url, True)
        elif "no tank" in why:
            add("notank", coll, lvl, tag, why,
                "%s: state the tank's cell and facing in "
                "bench/post-fixups.json" % CLARIFY, url, True)
        else:
            add("unreadable", coll, lvl, tag, why,
                "%s: the decoder could not read this image" % CLARIFY, url,
                True)
    # A conflict the sheet cannot arbitrate is the one thing here that fails
    # the gate rather than reporting a finding -- see cmd_codebook.
    for coll, lvl, cx, cy, got, true, hs, who in cb.get("unarbitrated", []):
        add("unarbitrated", coll, lvl, "-",
            "start board disagrees with the .lvl at %s%d (codebook %s, corpus "
            "%s) and the sprite sheet blames %s"
            % (chr(65 + cx), cy + 1, got, true, who),
            "%s: the derivation and the corpus disagree -- a gate failure, "
            "not a finding" % CLARIFY, "", True)

    # A post Michal has already looked at keeps its row -- the tool still
    # cannot see what he saw, so it finds the same thing every run -- but it
    # stops asking.  `confirmed` in bench/post-fixups.json is that word: it
    # corrects nothing, it closes the question, and it is what a *correct*
    # refusal needs, since the only other way to stop a row asking forever is
    # to fix something that is not broken.  It is per post rather than per
    # finding, so a post that later grows a second finding is closed by the
    # same line -- which is why the note is printed next to whatever the row
    # says, for the reader to check it still covers it.
    for r in rows.values():
        ok = fixup(r["url"]).get("confirmed")
        if ok:
            # The question goes and the finding stays: a CLARIFY line that
            # survives its own answer is the report asking twice.
            r["status"] = [t for t in r["status"] if not t.startswith(CLARIFY)]
            r["status"].append("checked: %s" % ok)
            r["open"] = False
    for r in rows.values():
        r["why"] = "; ".join(r["why"])
        r["status"] = "; ".join(r["status"])
    return sorted(rows.values(),
                  key=lambda r: (not r["open"], r["kind"], r["coll"],
                                 r["level"], r["tag"]))


# Two of the categories are the *blog's shape* rather than findings -- a post
# with no start screenshot, a post with no goal screenshot -- and at corpus
# scale they are 79 of the 80 rows.  Listing them by name buries the one row
# that needs reading, so they collapse to a line unless --all asks for them.
# Nothing with an open row in it ever collapses.
COLLAPSE = {
    "nostart": "carry no start screenshot -- nothing to label from, and no "
               "staleness check for those levels",
    "nogoal": "carry no goal screenshot -- there is no board to bank",
}


def complete_report(rep, expand=False):
    """The funnel and the table.  Reads what a run collected, so `--report`
    re-prints it without re-running anything."""
    f, rows = rep["funnel"], rep["rows"]

    def line(n, what, note=""):
        print(("  %7s  %-46s %s"
               % ("{:,}".format(n) if n is not None else "?",
                  what, note)).rstrip())

    print("\n=== harvest complete === %s" % rep.get("when", ""))
    line(f.get("posts"), "posts in the blog feed")
    line(f.get("solution posts"), "carry a level solution",
         "(%d do not: sidebar and index posts)" % f.get("not a solution", 0))
    line(f.get("covered"), "distinct levels named by those posts")
    line(f.get("with a goal frame"), "have a goal screenshot to decode",
         "(%d carry none)" % f.get("no goal frame", 0))
    dest, was = f.get("dest") or "?", f.get("was")
    try:
        dest = str(pathlib.Path(dest).relative_to(ROOT))
    except (ValueError, TypeError):
        pass
    # The bank it replaced, when the count moved: bench/goal-boards.json is
    # committed, so a run that banks fewer levels than the last one is a
    # regression and the diff is where it shows.
    delta = ""
    if was and was[0] != f.get("banked levels"):
        delta = ", was %s%s" % ("{:,}".format(was[0]),
                                " *** FEWER NOW ***"
                                if f.get("banked levels", 0) < was[0] else "")
    line(f.get("banked levels"), "banked -> %s" % dest,
         "(%s goal boards%s)"
         % ("{:,}".format(f.get("banked boards", 0)), delta))
    line(f.get("start boards"),
         "start boards also read, to label the tile codebook",
         "(%d not usable as one)" % f.get("start rejected", 0))

    op = [r for r in rows if r["open"]]
    hide = {k for k, rs in
            [(k, [r for r in rows if r["kind"] == k]) for k in COLLAPSE]
            if not expand and len(rs) > 3 and not any(r["open"] for r in rs)}
    print("\n%d post%s the decode did not finish on its own, %d waiting on "
          "you:" % (len(rows), "" if len(rows) == 1 else "s", len(op)))
    i = 0
    for r in rows:
        if r["kind"] in hide:
            continue
        i += 1
        print("  %3d  %-14s %5s %-2s  %s"
              % (i, r["coll"], r["level"], r["tag"], r["why"]))
        # Every listed row prints its post url, not only the open ones:
        # the rows that are *not* waiting on Michal are the ones a
        # reader most often wants to open -- to confirm for himself
        # that a refusal really is the blog's shape -- and a row
        # without its url makes that a search.
        print("       %s%s" % (r["status"],
                               "\n       %s" % r["url"]
                               if r["url"] else ""))
    for k in sorted(hide):
        n = [r for r in rows if r["kind"] == k]
        print("  %3s  %d posts %s" % ("+", len(n), COLLAPSE[k]))
        print("       intentional: %s ... (--all lists them)"
              % ", ".join("%s %d" % (r["coll"], r["level"]) for r in n[:4]))
    print("\n= %s"
          % ("nothing left to clarify" if not op
             else "%d left to clarify -- each needs one line in "
                  "bench/post-fixups.json" % len(op)))
    # A gate that does not run has to say so, in the report and not only in
    # the log -- this one was silent through a whole corpus-scale run.
    if f.get("residual gate") is False:
        print("note: the goal-residual gate did not run -- no "
              "build/harvest/residual.json; `python tools/harvest.py sheet` "
              "writes it")
    if f.get("residual open"):
        print("note: %d goal sprite%s nothing derives and nobody has stated -- "
              "`python tools/harvest.py sheet` draws them, then one \"pf\" "
              "each in build/harvest/residual.json:"
              % (len(f["residual open"]),
                 "" if len(f["residual open"]) == 1 else "s"))
        for w in f["residual open"]:
            print("      %s" % w)
    if f.get("clash"):
        print("note: %d tile%s where the derivation and the .lvl-labelled "
              "codebook disagree -- `tiles` is the gate and it failed"
              % (f["clash"], "" if f["clash"] == 1 else "s"))
    return 0


def cmd_complete(args):
    """The whole chain, one command, and one table of what it dropped.

    Six phases run today and each prints its own several hundred lines, so the
    question the reader actually has -- *which posts are not in the bank, and
    is that on purpose* -- was answered by reading five phase reports side by
    side and cross-referencing them by hand.  That is what this command is:
    the same phase functions, their output to a log, and the findings they
    collect assembled into one funnel and one table whose last column says who
    can resolve the row.  **The count that matters is the last line**: how many
    rows are waiting on a human, which is 1 today.

    **It also stops paying for the same decode three times.**  `codebook
    --goals`, `tiles` and `bank` each walk all 7,484 goal boards -- **37
    minutes apiece**, measured on the session 40 run, whose 2h40 is mostly
    those three -- and the only difference between them is what they report.
    So `complete` runs `tiles` as its two cheap gates (`--gate-only`, seconds)
    and takes the per-board findings from the bank pass, which is the pass that
    has the fixups and the refusals in it anyway; the residual sizing is not in
    the chain at all, since it sized an item that is closed and `codebook
    --goals` still runs it on demand.  **~65 minutes for the same artefacts and
    strictly more reporting.**  The phases stay separate commands: they are the
    instruments to reach for when one of them is what is being worked on.
    """
    OUT.mkdir(parents=True, exist_ok=True)
    rep = OUT / "complete.json"
    if args.report:
        if not rep.exists():
            raise SystemExit("no %s -- run: python %s complete"
                             % (rep, pathlib.Path(__file__).name))
        return complete_report(json.loads(rep.read_text()), args.all)

    log = OUT / "complete.log"
    ph, rc = {}, 0
    t0 = time.time()
    with open(log, "w", encoding="utf-8", errors="replace") as fh:
        def run(name, fn, a):
            """One phase: its findings kept, its output to the log."""
            nonlocal rc
            d = ph.setdefault(name, {})
            print("\n=== %s ===" % name, file=fh, flush=True)
            t = time.time()
            sys.stderr.write("%s ...\n" % name)
            if args.verbose:
                r = fn(a, out=d)
            else:
                stdout, sys.stdout = sys.stdout, fh
                try:
                    r = fn(a, out=d)
                finally:
                    sys.stdout = stdout
            d["seconds"] = time.time() - t
            rc = max(rc, r or 0)
            return r

        if not args.offline:
            run("index", cmd_index, _phase_args())
        run("map", cmd_map, _phase_args())
        if not args.offline:
            run("fetch", cmd_fetch, _phase_args(
                levels=None, collection=None, limit=0, seed=7, goals=True,
                refetch=False))
        else:
            ph["fetch"] = {}
        run("codebook", cmd_codebook,
            _phase_args(limit=0, extend=False, goals=False))
        run("tiles", cmd_tiles, _phase_args(out=None, gate_only=True))
        # **The whole-run bank is a committed artefact and lives in bench/.**
        # A full `complete` is the only thing that produces it, so it is the
        # only thing that writes it: a scoped `bank --levels X` keeps the
        # gitignored build/harvest/goals.json default rather than replacing
        # 5,975 levels with one.  Re-deriving the bank needs nine years of
        # blog, which is the whole reason it is committed -- bench/README.md.
        run("bank", cmd_bank, _phase_args(
            levels=None, out=args.out or str(BANK), allow_unknown=False,
            allow_stale=False))

    mp, ft = ph.get("map", {}), ph.get("fetch", {})
    cb, bk = ph.get("codebook", {}), ph.get("bank", {})
    man = manifest()
    funnel = {
        "posts": mp.get("posts"),
        "solution posts": mp.get("solution posts"),
        "not a solution": mp.get("not a solution post", 0),
        "covered": mp.get("covered"),
        "with a goal frame": sum(1 for r in man if r["goals"]),
        "no goal frame": bk.get("stat", {}).get("no goal image in the post", 0),
        "banked levels": bk.get("levels"),
        "banked boards": bk.get("boards"),
        "dest": bk.get("dest"),
        "was": bk.get("was"),
        "start boards": cb.get("start boards"),
        "start rejected": len(cb.get("notstart", []))
                          + sum(1 for r in man if not r["start"]),
        "residual gate": ph.get("tiles", {}).get("residual gate"),
        "residual open": ph.get("tiles", {}).get("residual open"),
        "clash": ph.get("tiles", {}).get("clash"),
    }
    out = {"when": time.strftime("%Y-%m-%d %H:%M"),
           "seconds": round(time.time() - t0),
           "phases": {k: round(v.get("seconds", 0)) for k, v in ph.items()},
           "funnel": funnel, "rows": complete_rows(ph, man)}
    write_json(rep, out)
    complete_report(out, args.all)
    print("\nphases: %s;  %s in all"
          % (", ".join("%s %s" % (k, hms(v))
                       for k, v in out["phases"].items()), hms(out["seconds"])))
    print("every phase's own output -> %s" % log)
    print("this table -> %s  (re-print it with: python %s complete --report)"
          % (rep, pathlib.Path(__file__).name))
    # The gate is the gate: a phase that failed is a failure whatever the table
    # says.  A row waiting on a human is *not* a failure -- it is the tool
    # working, so it does not touch the exit code; the last line reports it.
    return rc


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)

    sub.add_parser("index", help="build the post index from the Blogger feed")
    sub.add_parser("map", help="check (collection, level) -> name against the corpus")

    f = sub.add_parser("fetch", help="download start/goal screenshots")
    f.add_argument("--levels", action="append", metavar="COLL:N[,N...]",
                   help="e.g. --levels LaserTank:6,10 --levels Sokoban-I:2")
    f.add_argument("--collection")
    f.add_argument("--limit", type=int, default=0)
    f.add_argument("--seed", type=int, default=7)
    f.add_argument("--goals", action="store_true")
    f.add_argument("--refetch", action="store_true",
                   help="re-download even if the file is already on disk -- "
                        "for replacing an image an earlier run got as a "
                        "downscale; see fetch_one")

    c = sub.add_parser("codebook", help="build the tile codebook from start boards")
    c.add_argument("--limit", type=int, default=0)
    c.add_argument("--extend", action="store_true",
                   help="add to the codebook on disk instead of starting empty")
    c.add_argument("--goals", action="store_true",
                   help="also report what the codebook does not cover on the "
                        "goal boards -- the remaining cost of this item")

    s = sub.add_parser("sheet", help="contact sheet of the sprites still unlabelled")
    s.add_argument("--cols", type=int, default=12)
    s.add_argument("--scale", type=int, default=3)

    sub.add_parser("label", help="merge the filled-in sidecar into bench/goal-tiles.json")

    t = sub.add_parser("tiles", help="the sprite-sheet derivation, and its gate")
    t.add_argument("--out", help="write the derived hash -> PF table here")
    t.add_argument("--gate-only", action="store_true",
                   help="the two cheap gates without the 35-minute goal-board "
                        "pass -- what `complete` runs, since `bank` decodes "
                        "the same boards")

    k = sub.add_parser("bank", help="decode the goal screenshots into the "
                                    "bank --goal-board reads")
    k.add_argument("--levels", action="append", metavar="COLL:N[,N...]",
                   help="bank only these, e.g. --levels LaserTank:10")
    k.add_argument("--out", help="default build/harvest/goals.json")
    k.add_argument("--allow-unknown", action="store_true",
                   help="bank a board with undecoded cells, or with no tank "
                        "in the frame -- for looking at, never for solving")
    k.add_argument("--allow-stale", action="store_true",
                   help="bank the levels codebook found re-authored since "
                        "their post -- unreachable targets, so for looking "
                        "at, never for solving")

    p = sub.add_parser("complete", help="the whole chain in one command: one "
                                        "funnel, one table of what it dropped")
    p.add_argument("--out", help="the bank's destination, default "
                                 "bench/goal-boards.json (committed: a full "
                                 "run's bank cannot be re-derived offline)")
    p.add_argument("--offline", action="store_true",
                   help="skip index and fetch -- work from what is on disk")
    p.add_argument("--verbose", action="store_true",
                   help="every phase's own output on stdout as well as in "
                        "build/harvest/complete.log")
    p.add_argument("--report", action="store_true",
                   help="re-print the last run's table from "
                        "build/harvest/complete.json, running nothing")
    p.add_argument("--all", action="store_true",
                   help="name every post in the two collapsed categories -- "
                        "the ones that carry no start or no goal screenshot")

    d = sub.add_parser("decode", help="a screenshot -> a 16x16 board")
    d.add_argument("images", nargs="+")
    d.add_argument("--check", action="store_true",
                   help="diff against the corpus board (a gate on an 'a' image)")
    d.add_argument("--collection")

    args = ap.parse_args()
    return {"index": cmd_index, "map": cmd_map, "fetch": cmd_fetch,
            "codebook": cmd_codebook, "sheet": cmd_sheet, "label": cmd_label,
            "tiles": cmd_tiles, "bank": cmd_bank, "complete": cmd_complete,
            "decode": cmd_decode}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
