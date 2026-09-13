# Data formats (decoded and verified)

The byte layouts, decoded against the real files rather than against a description of them, and the
places where reading one is not the same as being able to write it. Constraint 2 —
[`PROGRESS.md`](../../PROGRESS.md#goal--hard-constraints) — is why every one of these is written as
well as read: 25 years of community content depends on them. The write-side traps are
[hazards #16, #17 and #18](quirks.md).

---

**`.lvl`** — flat array of 576-byte records, no header:

```
offset  size  field
0       256   playfield  char[16][16]   (PF[x][y], x = column)
256      31   name
287     256   hint
543      31   author
574       2   difficulty  u16  (1,2,4,8,16 = rated 1-5; 0 = unrated)
```

`data/levels/LaserTank.lvl` = **exactly 2030 levels**; 13 collections total, **20,914 levels**.

**Writing one back is not symmetric with reading it** — see hazards #16, #17 and #18. The editor
rewrites the record it read rather than re-encoding it, its string writes stop one byte short of each
field, saving past the end of a file zero-fills the gap into playable levels, and Clear Field NULs
only the first byte of the hint. `LaserTank.Core.LevelRecord` is where all four live.

**`.ghs` / `.hs`** — flat array of 10-byte records, indexed by `level - 1`:

```
0  2  moves  u16
2  2  shots  u16
4  6  initials
```

**Every entry in all 13 `.ghs` files is non-zero** → every level is known-solvable, with best-known
move/shot targets. Ranking is lexicographic: moves first, then shots.

**A `.hs` is dense and positional, and that gives it a quirk.** Beating level 8 of a collection whose
file reaches level 3 writes records 4..7 in front of it — and `CheckHighScore` pads with its `HS`
global **before** refreshing that global from the file, with only `moves` forced to 0. So the padding
carries the *previous* level's shots and initials. Nothing reads them; every read-back test passes
with them zeroed; they are in every `.hs` the 2010 binary has ever written, and constraint 2 says
these files stay writable and not merely readable. So `HS` is a `ScoreState` the Session owns for its
whole life.

**`.lpb`** — 66-byte header then raw VK bytes:

```
0   31  level name
31  31  author
62   2  level number  u16
64   2  data size     u16
66   ..  keystream
```

Key codes: `37`=Left `38`=Up `39`=Right `40`=Down `32`=Fire. **`.lpb` compatibility is
bidirectional** — the 2010 binary must be able to play what Godot records.

**`.ltg`** — a 324-byte `TLTGREC` header (`Name[40]`, `Author[30]`, `Info[245]`, `ID[5]` = `"LTG1"`,
`MaskOffset` DWORD) followed by two ordinary Windows BMPs: the game bitmap from the end of the header
to `MaskOffset`, the mask from there to EOF (`LoadLTG`, `LTANK2.C:688`). Splitting a `.ltg` at its
`MaskOffset` into `game.bmp` + `mask.bmp` is a **byte copy**, because that is literally what the
container is — which is why external mode (`GraphM == 1`) and `.ltg` mode render identical pixels.
`GetLTGFiles` names a pack by the `Name` field in its header, so `Lasertank_Comix.ltg` lists as
*Lasertank Comix*.

**Objects** — IDs 0–25, table at top of `LTANK.H`. Tunnels are encoded out-of-band as
`0x40 | (id << 1) | waitbit`; see the `GetTunnelID` / `ISTunnel` macros.

**Board coordinates in level hints** — columns `A`–`P` = x 0–15 left to right, rows `1`–`16` = y 0–15
top to bottom. It is not a community convention: the original labels its own board with it
(`LTANK.C:502`, `'@' + i` across and `itoa(i)` down) and this port draws the same four sides. See
[*Finished*](history.md), the coordinate grid.

