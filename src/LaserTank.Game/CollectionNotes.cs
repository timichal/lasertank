// Phase 5, step 12: what each collection *is*, and which shelf it belongs on.
//
// **Why this is a table and not a field.**  A .lvl carries a name for every
// level and none for itself -- `TLEVEL` is 576 bytes of playfield, hint, author
// and rank, repeated, with no header (see docs/game/formats.md).  So the file
// knows what its levels are called and cannot say what the *collection* is for.
// The original never needed to: its Open Data File was comdlg32 listing a
// directory, and everything a player knew about a collection they read on
// laser-tank.com before downloading it.  The port lists the corpus instead of
// the disk, which means the picker is now the only place that copy can live.
//
// **Where the copy comes from.**  laser-tank.com, rewritten.  Each collection
// has a download page whose one-line blurb is the only description upstream
// publishes, and the help page groups the ten teaching packs under *Trainings,
// Tutorials & Tricks* and *Hint files*.  The facts are theirs; the sentences
// are this port's, because the originals are a decade of accreted HTML written
// by several hands in a second language and read as such.  The upstream wording
// is quoted in the comment above each group so the rewrite can be checked
// against what it is a rewrite *of*, and data/SOURCES.md says how to refetch
// the pages (Cloudflare: curl with a browser User-Agent, the site is a
// frameset, the content is in levels/*.html and help.html).
//
// **The shelves are this port's own naming**, and deliberately not upstream's.
// The site has "More Levels" -- which means *the ones with no high-score page*,
// none of which are in this repo -- and "Trainings, Tutorials & Tricks" and
// "Hint files", which are a heading and a filename respectively.  What actually
// distinguishes the three groups in this repo is what you do with them: play
// the collections, work through the tutorials, and *look* at a walkthrough,
// which is a level file only because that is the only container the game reads.
//
// **A stem that is not in this table still lists.**  It lands on the shelf its
// directory implies and shows no blurb, which is the right answer for a level
// the editor just saved and for whatever upstream adds next.  Nothing here is
// load-bearing: no gate reads this file, and CollectionList.BuildRows -- the
// rows tools/collections_check.py diffs against Python -- does not know it
// exists.
using System.Collections.Generic;

namespace LaserTank.Game
{
    /// The four groups the picker lists under.  The order is the order they are
    /// drawn in, which is also roughly the order a player wants them: the game,
    /// then how to get better at it, then the answers, then your own work.
    public enum Shelf
    {
        Collections,
        Tutorials,
        Walkthroughs,
        Yours,
    }

    public static class CollectionNotes
    {
        /// A shelf's name and the line under it.  The name is set in caps by
        /// Ui.Caps, so it is written here in the case it would be read in.
        public static (string Name, string Blurb) Head(Shelf s) => s switch
        {
            Shelf.Collections =>
                ("Collections",
                 "the game proper — every level with a world best to beat"),
            Shelf.Tutorials =>
                ("Tutorials",
                 "short packs that teach the objects and the tricks"),
            Shelf.Walkthroughs =>
                ("Walkthroughs",
                 "one hard level of the original, shown part-solved"),
            _ =>
                ("Your levels",
                 "whatever the editor saved"),
        };

        /// Where a collection sits and what it is, by the stem the game names it
        /// by.  Case-insensitive because the corpus mixes `.lvl` and `.LVL` and
        /// there is no reason to assume the stems are steadier than the
        /// extensions.
        private static readonly Dictionary<string, (Shelf Shelf, string Text)> ByStem =
            new(System.StringComparer.OrdinalIgnoreCase)
        {
            // ---- the thirteen with high-score pages -------------------------
            // Upstream, one line per download page:
            //   LaserTank      "This is the original Level file"
            //   Challenge-I..V "You're done with the LaserTank.lvl file? You can
            //                   now continue with these."
            //   Beginner-I/II  "Levels that users considered too easy. Ideal for
            //                   young and beginners."
            //   Sokoban-I/II   "Levels which are in the same spirit as the well
            //                   known Sokoban game"
            //   Gary-I/II      "Repetitive series of levels made by Gary. They
            //                   are extracted from the regular levels files."
            //   Special-I      "Levels unwanted in others files because similar
            //                   to others, too long, boring etc..."
            ["LaserTank"] = (Shelf.Collections,
                "The original file — the game as Jim Kindley shipped it, and still where everyone starts."),

            ["Challenge-I"] = (Shelf.Collections,
                "For when the original runs out. Five more full-length files, no gentler and no easier."),
            ["Challenge-II"] = (Shelf.Collections,
                "The second of the five that pick up where the original file ends."),
            ["Challenge-III"] = (Shelf.Collections,
                "The third of the five that pick up where the original file ends."),
            ["Challenge-IV"] = (Shelf.Collections,
                "The fourth of the five that pick up where the original file ends."),
            ["Challenge-V"] = (Shelf.Collections,
                "The last of the five, and the one still filling up — new levels land here first."),

            ["Beginner-I"] = (Shelf.Collections,
                "Levels the other files turned down for being too easy, which makes this the place to learn."),
            ["Beginner-II"] = (Shelf.Collections,
                "More of the same: the gentle end of the corpus, and the one to hand a child."),

            ["Sokoban-I"] = (Shelf.Collections,
                "Push-the-block puzzles in the spirit of Sokoban, with a tank and a laser where the warehouse keeper was."),
            ["Sokoban-II"] = (Shelf.Collections,
                "The second Sokoban file. Same idea, less room to turn around in."),

            ["Gary-I"] = (Shelf.Collections,
                "Gary's series, lifted out of the main files: one idea at a time, turned over until it is finished."),
            ["Gary-II"] = (Shelf.Collections,
                "The rest of Gary's, and the smallest of the thirteen."),

            ["Special-I"] = (Shelf.Collections,
                "The offcuts — levels the other files turned away for being too long, too alike or too strange."),

            // ---- the help page's teaching packs ------------------------------
            // Upstream calls this group "Trainings, Tutorials & Tricks" and
            // says of the two tutor files: "I strongly recommend to all players
            // to study the two tutor levels files.  They'll give you the skill
            // to solve almost all levels available on this web site."
            ["Game-Objects-in-LT"] = (Shelf.Tutorials,
                "One level per object, each with its solution recorded: what every tile in the game does."),
            ["Tutor-with-Playbacks"] = (Shelf.Tutorials,
                "The beginner's course, every level with its solution recorded. Start here if the objects are new."),
            ["Tutor"] = (Shelf.Tutorials,
                "The trick specification: one level per undocumented behaviour, each explained in its own hint. "
                + "Most are bugs the author left in on purpose."),
            ["Rotary Mirrors-Challenge"] = (Shelf.Tutorials,
                "Rotating mirrors and nothing else, every level with its playback — the one object worth a file of its own."),
            ["Tricks"] = (Shelf.Tutorials,
                "Levels built out of the Tutor tricks, made to be watched rather than fought."),
            ["Pono's_trick"] = (Shelf.Tutorials,
                "Mau's levels on Pono's trick: how to cross thin ice without breaking it."),

            // ---- the help page's hint files ----------------------------------
            // Upstream: "These are levels files that show the level in various
            // stages of completion.  You can use Game, Open data file to load
            // these levels.  Then reload the LaserTank.lvl file..." -- which is
            // exactly the round trip this picker makes cheap.
            ["l40"] = (Shelf.Walkthroughs,
                "Level 40 of the original, \"Down the Drain\", opened out stage by stage. "
                + "The author: \"It took me 4 hours to do this one.\""),
            ["4triang"] = (Shelf.Walkthroughs,
                "Level 149 of the original, \"The 4 Triangles\", broken into stages."),
            ["telek-1"] = (Shelf.Walkthroughs,
                "Level 173 of the original, \"Telekinesis\", with the positions that give it away."),
            ["inchworm"] = (Shelf.Walkthroughs,
                "Level 179 of the original, \"Being an Inchworm\", with the position that gives it away."),
        };

        /// **The sort key inside a shelf, low first**, and only two shelves have
        /// one -- which is why this is five entries rather than a field on every
        /// row.  Everything else answers 0 and keeps the scan order, which is the
        /// sorted walk of the roots.
        ///
        /// `LaserTank` is **-1**: it is the original file, the one the other
        /// twelve are measured against -- upstream's own description of the
        /// Challenge files is *"You're done with the LaserTank.lvl file? You can
        /// now continue with these"* -- and alphabetical order buried it in the
        /// middle of the shelf, between Gary-II and Sokoban-I, reading as the
        /// tenth of thirteen peers.
        ///
        /// A hint file's key is **the level of the original it opens out** -- 40,
        /// 149, 173, 179 -- because that is what it is *about*, and the
        /// alphabetical run (`4triang, inchworm, l40, telek-1`) interleaves them
        /// meaninglessly.  The two kinds of entry share one map because they are
        /// one thing: a position in a list.
        private static readonly Dictionary<string, int> Order =
            new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["LaserTank"] = -1,

            ["l40"] = 40,
            ["4triang"] = 149,
            ["telek-1"] = 173,
            ["inchworm"] = 179,
        };

        public static int OrderOf(Collection c)
            => Order.TryGetValue(c.Label, out int n) ? n : 0;

        /// Which shelf a collection is on.  A stem nobody wrote a line for falls
        /// back to what its directory says, which is why out/levels/ needs no
        /// entries at all and why a new file under data/levels/ lists with the
        /// collections rather than in a bin marked *other*.
        public static Shelf ShelfOf(Collection c)
        {
            if (ByStem.TryGetValue(c.Label, out var n)) return n.Shelf;
            if (c.Dir.StartsWith("out/", System.StringComparison.OrdinalIgnoreCase))
                return Shelf.Yours;
            if (c.Dir.StartsWith("data/quirks", System.StringComparison.OrdinalIgnoreCase))
                return Shelf.Tutorials;
            return Shelf.Collections;
        }

        /// The line the picker shows for a collection, or "" when nobody has
        /// written one.  The caller decides what to put there instead -- see
        /// CollectionList.Draw, which falls back to the path, because a blank
        /// strip reads as a bug and a path is at least true.
        public static string TextOf(Collection c)
            => ByStem.TryGetValue(c.Label, out var n) ? n.Text : "";
    }
}
