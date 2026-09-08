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

Nine subcommands, cheapest first:

  index      the whole post index from the Blogger feed -- title, URL, date and
             image URLs for all 6,218 posts in 42 requests, one minute.  No
             HTML scraping: the feed serves the post body, and the image
             filenames are in it
  map        (collection, level) -> name, checked against the corpus .lvl.
             Reads the index; no network, no images
  fetch      download the start and goal screenshots for selected levels
  codebook   build the 24x24 tile codebook from start screenshots, whose boards
             the corpus already knows -- 256 labelled tiles per post, free.
             --goals sizes what this half does *not* cover; that was the item's
             remaining cost until `tiles` derived all of it
  tiles      the goal-only sprites *derived* rather than labelled, from the
             game's own sheet -- tools/sprites.py.  This is the gate on that
             derivation, and it retired `sheet`/`label` as the item's phase 1
  sheet      the sprites the derivation does not cover, as one contact sheet
             plus a sidecar to fill in.  Empty over the whole sample; kept for
             the tiles a screenshot catches mid-blit, which are not game states
  label      merge the filled-in sidecar into bench/goal-tiles.json
  decode     a screenshot -> a 16x16 board *and* where the tank is.  --check
             re-decodes a start screenshot and diffs it against the .lvl,
             which is the gate
  bank       every fetched goal screenshot decoded into one file: the bank
             `lasertank-solve --goal-board` reads.  Refuses a board with an
             undecoded cell in it rather than banking a key with a hole

    python tools/harvest.py index                       # 42 requests, ~46 s
    python tools/harvest.py map                         # no network, no images
    python tools/harvest.py fetch --limit 150 --goals    # ~7 min
    python tools/harvest.py codebook --goals
    python tools/harvest.py tiles                       # the derivation's gate
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

Exit: 0 clean, 1 a decode disagreed with the corpus, 2 environment.
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


def cmd_index(args):
    OUT.mkdir(parents=True, exist_ok=True)
    rows = []
    i = 1
    total = None
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
        sys.stderr.write("\r  %d/%d posts" % (len(rows), total))
        sys.stderr.flush()
        i += len(es)
        if len(rows) >= total:
            break
        time.sleep(0.2)
    sys.stderr.write("\n")
    p = OUT / "index.jsonl"
    with open(p, "w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    print("index: %d posts of %d -> %s" % (len(rows), total, p))
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


def posts_by_level(rows=None):
    """(collection, level) -> [post], in the feed's order."""
    out = collections.defaultdict(list)
    for r in rows if rows is not None else read_index():
        p = parse_title(r["title"])
        if not p:
            continue
        coll, n, name, part = p
        imgs = {}
        for u in r["imgs"]:
            imgs.setdefault(u.rsplit("/", 1)[-1], u)
        out[(coll, n)].append({"url": r["url"], "published": r["published"],
                               "name": name, "part": part, "imgs": imgs})
    return out


def cmd_map(args):
    rows = read_index()
    stat = collections.Counter()
    bad = []
    seen = set()
    for r in rows:
        p = parse_title(r["title"])
        if not p:
            stat["not a solution post"] += 1
            continue
        coll, n, name, _ = p
        got = level_name(coll, n)
        if got is None:
            stat["collection or level not in corpus"] += 1
            bad.append((coll, n, name, None))
            continue
        seen.add((coll, n))
        if norm(got) in (norm(name), norm(PART.sub("", name))):
            stat["exact"] += 1
        else:
            stat["title name differs"] += 1
            bad.append((coll, n, name, got))
    print("posts: %d" % len(rows))
    for k, v in stat.most_common():
        print("  %-38s %5d" % (k, v))
    print("distinct (collection, level) covered: %d" % len(seen))
    if bad:
        print("\nthe residual -- every one a blog-side title typo, not a bad index:")
        for coll, n, name, got in bad:
            print("  %-14s %5d  post %r  corpus %r" % (coll, n, name, got))
    per = collections.Counter(k[0] for k in seen)
    print("\nper collection:")
    for c in sorted(per):
        c_all = collection(c)
        print("  %-14s %5d covered of %5d" % (c, per[c], len(c_all) if c_all else -1))
    return 0


# ---------------------------------------------------------------- the images

def pick_images(n, imgs):
    """-> (start basename, [(tag, goal basename)]).

    Two naming eras, both keyed by the level number.  2016: '10a.png' is the
    start, '10b.png' and '10c.png' the goals.  Later: 'ChallengeI_1901.png' is
    the start and the goals are either 'ChallengeI_1901b.png' or one
    'ChallengeI_1901_G4.png' per flag -- collection, level, and *which flag was
    reached in what order*, in the game's own column-row notation.  So for a
    multi-flag level the subgoal sequence needs no pixel decoding at all.
    """
    start = None
    goals = []
    for bn in imgs:
        b = bn.rsplit(".", 1)[0]
        if re.fullmatch(r"(?i)(?:[a-z]+_?)?%d" % n, b):
            start = bn
        elif re.fullmatch(r"(?i)(?:[a-z]+_?)?%da" % n, b):
            start = bn
        elif re.fullmatch(r"(?i)(?:[a-z]+_?)?%d[b-z]" % n, b):
            goals.append((b[-1].lower(), bn))
        else:
            m = re.fullmatch(r"(?i)[a-z]+_?%d_([A-Za-z]\d+)" % n, b)
            if m:
                goals.append((m.group(1).upper(), bn))
    return start, sorted(goals)


def fetch_one(url, dest):
    """Blogger serves each image under several size directories; ask for the
    original.  Some posts answer 404 or 500 on a rewritten size, so try those
    first and fall back to the URL exactly as the post gave it."""
    if dest.exists():
        return True
    p = url.split("/")
    tries = [url]
    for s in ("s1600", "s16000"):
        if p[-2] != s:
            q = list(p)
            q[-2] = s
            tries.append("/".join(q))
    for u in tries:
        try:
            d = get(u)
        except urllib.error.HTTPError:
            continue
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(d)
        time.sleep(0.05)
        return True
    return False


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


def cmd_fetch(args):
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
    for coll, n in selected(args, by_level):
        post = by_level[(coll, n)][0]
        start, goals = pick_images(n, post["imgs"])
        rec = {"coll": coll, "level": n, "published": post["published"],
               "url": post["url"], "start": None, "goals": []}
        if start and fetch_one(post["imgs"][start], img_path(coll, n, "a")):
            rec["start"] = str(img_path(coll, n, "a").relative_to(ROOT))
            got["start"] += 1
        else:
            got["no start image"] += 1
        if args.goals:
            for tag, bn in goals:
                if fetch_one(post["imgs"][bn], img_path(coll, n, tag)):
                    rec["goals"].append(
                        {"tag": tag,
                         "file": str(img_path(coll, n, tag).relative_to(ROOT))})
                    got["goal"] += 1
        man.append(rec)
        keep[(coll, n)] = rec
    OUT.mkdir(parents=True, exist_ok=True)
    all_recs = [keep[k] for k in sorted(keep)]
    p.write_text(json.dumps(all_recs, indent=1))
    for k, v in got.most_common():
        print("  %-16s %5d" % (k, v))
    print("manifest -> %s  (%d this run, %d in all)"
          % (p, len(man), len(all_recs)))
    return 0


# ---------------------------------------------------------------- the decode

def origin(w, h, px):
    """The board's top-left pixel, from the frame drawn around it.

    The frame's horizontal lines are the only rows carrying a 384-pixel run of
    (128,128,128), and the run's own start is the left frame; the second such
    row is the inner one.  bytes.find does the scan, which is why this costs a
    millisecond rather than a 300k-iteration pixel loop.
    """
    run = GREY * 16 * PITCH
    hits = []
    for y in range(h):
        i = bytes(px[y * w * 3:(y + 1) * w * 3]).find(run)
        if i >= 0:
            hits.append((y, i // 3))
    if len(hits) < 2:
        raise ValueError("no 384-pixel board frame -- not a LaserTank window?")
    return hits[1][1] + 2, hits[1][0] + 1


def tile_hashes(path):
    w, h, px = png.load(path)
    x0, y0 = origin(w, h, px)
    out = {}
    for cy in range(16):
        for cx in range(16):
            b = bytearray()
            for yy in range(PITCH):
                i = ((y0 + cy * PITCH + yy) * w + x0 + cx * PITCH) * 3
                b += px[i:i + PITCH * 3]
            out[(cx, cy)] = hashlib.md5(bytes(b)).hexdigest()[:16]
    return out, (x0, y0)


def show(g):
    print("    " + "".join(str(x % 10) for x in range(16)))
    for y in range(16):
        print(" %2d %s" % (y, "".join(g[y][x] for x in range(16))))


def cbpath():
    """The start-bootstrapped half: derivable, so it lives in gitignored build/."""
    return OUT / "codebook.json"


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
        for hs, (pf, facing, _) in sprites.Cells().table().items():
            cb[hs] = pf
            if facing:
                tank[hs] = facing
        counts["derived"] = len(set(cb) - before)
    return cb, tank, counts


def cmd_codebook(args):
    """Bootstrap the codebook from start screenshots.

    For every post the collection and level are known from its title, so a
    start screenshot is 256 *labelled* tiles for free and no codebook has to be
    built by hand.  Each board is decoded against the codebook built from the
    boards *before* it, so the printed curve is an honest saturation curve and
    a disagreement is a hard error rather than a self-fulfilling one.
    """
    have = [r for r in manifest() if r["start"] and (ROOT / r["start"]).exists()]
    if args.limit:
        have = have[:args.limit]
    cb = json.loads(cbpath().read_text()) if args.extend and cbpath().exists() else {}
    conflicts = []
    exact = noframe = 0
    curve = []
    for k, r in enumerate(have):
        B = board(r["coll"], r["level"])
        if B is None:
            continue
        try:
            t, _ = tile_hashes(ROOT / r["start"])
        except Exception as e:
            noframe += 1
            print("  NOFRAME %s: %s" % (r["start"], e))
            continue
        unk = bad = 0
        for (cx, cy), hs in t.items():
            true = B[cy][cx]
            got = cb.get(hs)
            if got is None:
                unk += 1
            elif got != true:
                bad += 1
                conflicts.append((r["coll"], r["level"], cx, cy, got, true, hs))
        if not unk and not bad:
            exact += 1
        curve.append((len(curve) + 1, len(cb), unk, bad))
        for (cx, cy), hs in t.items():
            cb.setdefault(hs, B[cy][cx])
    print("start boards: %d  (no frame: %d)" % (len(curve), noframe))
    print("  n  codebook  unknown  conflict")
    for n, sz, unk, bad in curve:
        if n <= 10 or unk or bad or n % 25 == 0 or n == len(curve):
            print("%3d  %8d  %7d  %8d" % (n, sz, unk, bad))
    last = max([n for n, _, u, _ in curve if u] or [0])
    print("\ndecoded exactly against earlier boards only: %d of %d" % (exact, len(curve)))
    print("last board that taught the codebook a new tile: %d of %d" % (last, len(curve)))
    print("codebook entries: %d   conflicts: %d" % (len(cb), len(conflicts)))
    for c in conflicts[:20]:
        print("  CONFLICT %s %d (%d,%d) codebook=%s corpus=%s %s" % c)
    cbpath().write_text(json.dumps(cb, indent=0, sort_keys=True))
    print("codebook -> %s" % cbpath())
    if args.goals:
        # count the hand-labelled half as covered: the residual is what is
        # left to do, not what the bootstrap alone happens to reach
        merged = dict(cb)
        if labelpath().exists():
            merged.update({k: v for k, v in
                           json.loads(labelpath().read_text()).items()
                           if not k.startswith("_")})
        goal_residual(merged)
    return 1 if conflicts else 0


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
    files = [(r["coll"], r["level"], g["tag"], ROOT / g["file"])
             for r in manifest() for g in r["goals"]]
    unk = collections.Counter()
    per = collections.Counter()
    n = noframe = 0
    for coll, lvl, tag, f in files:
        if not f.exists():
            continue
        try:
            t, _ = tile_hashes(f)
        except Exception:
            noframe += 1
            continue
        n += 1
        u = sum(1 for hs in t.values() if hs not in cb)
        for hs in t.values():
            if hs not in cb:
                unk[hs] += 1
        per["0" if u == 0 else "1" if u == 1 else "2" if u == 2 else
            "3-5" if u <= 5 else "6-10" if u <= 10 else ">10"] += 1
    if not n:
        print("\nno goal images -- run fetch with --goals")
        return
    print("\ngoal boards decoded: %d  (no frame: %d)" % (n, noframe))
    print("goal-only sprites the start boards never label: %d"
          % len(unk))
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
    """Merge the filled-in sidecar into the committed goal-tile table."""
    side = OUT / "residual.json"
    if not side.exists():
        raise SystemExit("no %s -- run: python tools/harvest.py sheet" % side)
    rec = json.loads(side.read_text())
    filled = [r for r in rec if r.get("pf", "").strip()]
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
    """
    cb, _, n = load_codebook()
    if n["hand"]:
        print("%d sprites already hand-labelled in %s; this sheet is what is left"
              % (n["hand"], labelpath().relative_to(ROOT)))
    files = [(r["coll"], r["level"], g["tag"], ROOT / g["file"])
             for r in manifest() for g in r["goals"]]
    seen = {}
    cnt = collections.Counter()
    for coll, lvl, tag, f in files:
        if not f.exists():
            continue
        try:
            w, h, px = png.load(f)
            x0, y0 = origin(w, h, px)
        except Exception:
            continue
        for cy in range(16):
            for cx in range(16):
                b = bytearray()
                for yy in range(PITCH):
                    i = ((y0 + cy * PITCH + yy) * w + x0 + cx * PITCH) * 3
                    b += px[i:i + PITCH * 3]
                hs = hashlib.md5(bytes(b)).hexdigest()[:16]
                if hs in cb:
                    continue
                cnt[hs] += 1
                if hs not in seen:
                    seen[hs] = (bytes(b), coll, lvl, tag, cx, cy)
    if not seen:
        print("nothing unlabelled -- the codebook covers every goal tile")
        return 0

    order = [h for h, _ in cnt.most_common()]
    s, cols = args.scale, args.cols
    rows = (len(order) + cols - 1) // cols
    W, H = cols * PITCH * s, rows * PITCH * s
    out = bytearray(W * H * 3)
    for k, hs in enumerate(order):
        tile = seen[hs][0]
        gx, gy = (k % cols) * PITCH * s, (k // cols) * PITCH * s
        for yy in range(PITCH * s):
            for xx in range(PITCH * s):
                si = ((yy // s) * PITCH + (xx // s)) * 3
                di = ((gy + yy) * W + gx + xx) * 3
                out[di:di + 3] = tile[si:si + 3]
    sheet = OUT / "residual.png"
    sheet.write_bytes(png.encode(W, H, out))

    side = OUT / "residual.json"
    rec = [{"i": k, "hash": hs, "n": cnt[hs], "pf": "",
            "first_seen": "%s %d %s at (%d,%d)"
                          % (seen[hs][1], seen[hs][2], seen[hs][3],
                             seen[hs][4], seen[hs][5])}
           for k, hs in enumerate(order)]
    side.write_text(json.dumps(rec, indent=1))
    print("%d unlabelled sprites, %d instances" % (len(order), sum(cnt.values())))
    print("sheet   -> %s  (%dx%d, %d per row, index left-to-right top-to-bottom,"
          " scale %dx)" % (sheet, W, H, cols, s))
    print("sidecar -> %s  (fill in each \"pf\", then merge into codebook.json)"
          % side)
    return 0


def decode_board(path, cb, tank=None):
    """One screenshot -> (PF board, unknown tiles, origin, where the tank is).

    The tank is separate from the board on purpose: `PF` does not hold it --
    BuildBMField clears `PF` at the tank's cell on load (`Engine.cs:348`) -- so
    a cell the tank is standing on decodes to the terrain under it, and the
    tank comes back as `(x, y, facing)`.  That is exactly the shape a goal
    board wants, and it is what turned `LaserTank.lvl` 10's one undecoded cell
    into derived data.
    """
    t, org = tile_hashes(path)
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
    return g, unk, org, where


def cmd_tiles(args):
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
    """
    import sprites
    c = sprites.Cells()
    t = c.table()
    print("derived from %s: %d cells with an unambiguous PF, %d dropped as "
          "PF-ambiguous" % (pathlib.Path(sprites.GAME_BMP).name, len(t),
                            len(c.conflicts())))

    cb = json.loads(cbpath().read_text()) if cbpath().exists() else {}
    agree = tankok = clash = absent = 0
    for hs, pf in cb.items():
        v = t.get(hs)
        if v is None:
            absent += 1
            print("  not derived: %s = %r" % (hs, pf))
        elif v[0] == pf:
            agree += 1
        elif pf == "T" and v[1] is not None:
            tankok += 1                     # the tank cell, reconciled
        else:
            clash += 1
            print("  CLASH %s: codebook %r, derived %r (%s)"
                  % (hs, pf, v[0], v[2]))
    if cb:
        print("start-bootstrapped codebook: %d of %d agree, %d tank cells "
              "reconciled, %d clash, %d not derived"
              % (agree, len(cb), tankok, clash, absent))

    side = OUT / "residual.json"
    if side.exists():
        rec = json.loads(side.read_text())
        hit = [r for r in rec if r["hash"] in t]
        print("goal residual: %d of %d sprites derived, %d of %d instances"
              % (len(hit), len(rec), sum(r["n"] for r in hit),
                 sum(r["n"] for r in rec)))
        for r in rec:
            if r["hash"] not in t:
                print("  not derived: n=%-4d %s  %s"
                      % (r["n"], r["hash"], r["first_seen"]))

    cbfull, tank, n = load_codebook()
    print("\ndecoding every goal board against all three halves: %d start-"
          "bootstrapped + %d hand-labelled + %d derived = %d tiles"
          % (n["start"], n["hand"], n["derived"], len(cbfull)))
    files = [(r["coll"], r["level"], g["tag"], ROOT / g["file"])
             for r in manifest() for g in r["goals"]]
    per = collections.Counter()
    unk = collections.Counter()
    nb = notank = 0
    for coll, lvl, tag, f in files:
        if not f.exists():
            continue
        try:
            g, u, _, where = decode_board(f, cbfull, tank)
        except Exception:
            continue
        nb += 1
        notank += where is None
        unk.update(u)
        v = sum(u.values())
        per["0" if v == 0 else "1" if v == 1 else "2" if v == 2 else
            "3-5" if v <= 5 else "6-10" if v <= 10 else ">10"] += 1
    if not nb:
        print("\nno goal images -- run fetch with --goals")
        return 0
    print("\ngoal boards decoded: %d;  unknown tiles %d of %d (%.2f%%);  "
          "%d distinct" % (nb, sum(unk.values()), nb * 256,
                           100.0 * sum(unk.values()) / (nb * 256), len(unk)))
    print("boards with no tank found: %d" % notank)
    print("unknown cells per goal board:")
    for k in ("0", "1", "2", "3-5", "6-10", ">10"):
        if per[k]:
            print("  %-5s %4d boards (%.1f%%)" % (k, per[k],
                                                  100.0 * per[k] / nb))
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


def cmd_bank(args):
    """Decode every fetched goal screenshot into the bank the solver reads.

    This is the artefact `--goal-board` takes: (collection, level, goal PF,
    tank, moves, shots).  Everything in it is derived -- `tiles` labels the
    pixels and the corpus names the level -- except the two panel counters,
    which no graphic in this repo can draw; see counters().

    A board with an unknown cell in it is **refused** rather than banked with a
    hole.  A goal board is a ranking key, and a key with a '?' in it prices
    that cell as "already right" wherever the search happens to be: the one
    failure mode that is worse than having no key at all.  --allow-unknown
    banks them anyway, which is for looking at them, not for solving.
    """
    cb, tank, _ = load_codebook()
    hand = counters()
    want = None
    if args.levels:
        want = set()
        for spec in args.levels:
            coll, _, ns = spec.partition(":")
            for n in ns.split(","):
                want.add((coll, int(n)))

    out = []
    stat = collections.Counter()
    for r in manifest():
        if want is not None and (r["coll"], r["level"]) not in want:
            continue
        if not r["goals"]:
            stat["no goal image in the post"] += 1
            continue
        boards = []
        for g in r["goals"]:
            f = ROOT / g["file"]
            if not f.exists():
                stat["image not fetched"] += 1
                continue
            try:
                grid, unk, _, where = decode_board(f, cb, tank)
            except ValueError:
                stat["not a LaserTank window"] += 1
                continue
            u = sum(unk.values())
            if u and not args.allow_unknown:
                stat["unknown cells -- refused"] += 1
                continue
            stat["banked"] += 1
            boards.append({
                "tag": g["tag"],
                "flags": sum(row.count("F") for row in grid),
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
        out.append({"coll": r["coll"], "level": r["level"],
                    "name": level_name(r["coll"], r["level"]),
                    "url": r["url"],
                    "moves": c.get("moves"), "shots": c.get("shots"),
                    "goals": boards})

    dest = pathlib.Path(args.out) if args.out else OUT / "goals.json"
    write_json(dest, {"_README": BANK_README, "levels": out})
    for k, v in stat.most_common():
        print("  %-28s %5d" % (k, v))
    print("bank -> %s  (%d levels, %d boards, %d with counters)"
          % (dest, len(out), sum(len(r["goals"]) for r in out),
             sum(1 for r in out if r["moves"] is not None)))
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
    rc = 0
    for path in args.images:
        g, unk, org, where = decode_board(path, cb, tank)
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
        print("  %s: %d" % ("mismatches vs the corpus board" if tag == "a"
                            else "cells differing from the corpus board", len(diff)))
        for x, y, a, b in diff:
            print("    (%2d,%2d) corpus %s  image %s" % (x, y, a, b))
        if tag == "a" and diff:
            rc = 1
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

    k = sub.add_parser("bank", help="decode the goal screenshots into the "
                                    "bank --goal-board reads")
    k.add_argument("--levels", action="append", metavar="COLL:N[,N...]",
                   help="bank only these, e.g. --levels LaserTank:10")
    k.add_argument("--out", help="default build/harvest/goals.json")
    k.add_argument("--allow-unknown", action="store_true",
                   help="bank a board with undecoded cells in it -- for "
                        "looking at, never for solving")

    d = sub.add_parser("decode", help="a screenshot -> a 16x16 board")
    d.add_argument("images", nargs="+")
    d.add_argument("--check", action="store_true",
                   help="diff against the corpus board (a gate on an 'a' image)")
    d.add_argument("--collection")

    args = ap.parse_args()
    return {"index": cmd_index, "map": cmd_map, "fetch": cmd_fetch,
            "codebook": cmd_codebook, "sheet": cmd_sheet, "label": cmd_label,
            "tiles": cmd_tiles, "bank": cmd_bank,
            "decode": cmd_decode}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
