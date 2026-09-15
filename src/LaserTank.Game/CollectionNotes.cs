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
// **The sentences themselves are not here any more.**  This table maps a stem
// to a *key*; the copy lives in `data/language/*.json` under `note.*`, in
// eleven languages, and `tools/strings_check.py` fails if a key here has no
// entry there or an entry there has no stem here.  What stays behind is the
// upstream quotation each rewrite answers to, which is the part a JSON file has
// nowhere to put.
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
// directory implies, keeps its file name as its label and shows no blurb, which
// is the right answer for a level the editor just saved and for whatever
// upstream adds next.
//
// **The names are the one thing here that reaches the rows.**  The shelves and
// the blurbs are a display layer -- drawn around rows that do not know they
// exist -- but `Names` is read by CollectionList.BuildRows, which is what
// tools/collections_check.py rebuilds in Python and diffs row by row and by
// sha256.  So the ten names are written out again there, under the same rule as
// the walk and the column widths: two implementations agreeing is evidence, one
// agreeing with itself is not.
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
        /// A shelf's name and the line under it, as catalogue keys.
        ///
        /// The name is set in caps by Ui.Caps, so `en.json` writes it in the
        /// case it would be read in -- and a language whose caps are not the
        /// English ones gets to spell it its own way.
        public static (string Name, string Blurb) Head(Shelf s) => s switch
        {
            Shelf.Collections => ("shelf.collections", "shelf.collectionsBlurb"),
            Shelf.Tutorials => ("shelf.tutorials", "shelf.tutorialsBlurb"),
            Shelf.Walkthroughs => ("shelf.walkthroughs", "shelf.walkthroughsBlurb"),
            _ => ("shelf.yours", "shelf.yoursBlurb"),
        };

        /// Where a collection sits and what it is, by the stem the game names it
        /// by.  The second field is a **catalogue key**, not a sentence: the
        /// copy itself is in `data/language/*.json` under `note.*`, in eleven
        /// languages, because a picker that describes the corpus in English to a
        /// player reading Czech chrome is only half translated.
        ///
        /// Case-insensitive because the corpus mixes `.lvl` and `.LVL` and there
        /// is no reason to assume the stems are steadier than the extensions.
        ///
        /// **Where the copy comes from** is in this file's header, and the
        /// upstream wording each key is a rewrite *of* is quoted in the comments
        /// below -- which is the thing a stem-to-key table would otherwise have
        /// thrown away, so it stays here beside the stems rather than moving
        /// into the JSON.
        private static readonly Dictionary<string, (Shelf Shelf, string Key)> ByStem =
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
            ["LaserTank"] = (Shelf.Collections, "note.lasertank"),

            ["Challenge-I"] = (Shelf.Collections, "note.challenge1"),
            ["Challenge-II"] = (Shelf.Collections, "note.challenge2"),
            ["Challenge-III"] = (Shelf.Collections, "note.challenge3"),
            ["Challenge-IV"] = (Shelf.Collections, "note.challenge4"),
            ["Challenge-V"] = (Shelf.Collections, "note.challenge5"),

            ["Beginner-I"] = (Shelf.Collections, "note.beginner1"),
            ["Beginner-II"] = (Shelf.Collections, "note.beginner2"),

            ["Sokoban-I"] = (Shelf.Collections, "note.sokoban1"),
            ["Sokoban-II"] = (Shelf.Collections, "note.sokoban2"),

            ["Gary-I"] = (Shelf.Collections, "note.gary1"),
            ["Gary-II"] = (Shelf.Collections, "note.gary2"),

            ["Special-I"] = (Shelf.Collections, "note.special1"),

            // ---- the help page's teaching packs ------------------------------
            // Upstream calls this group "Trainings, Tutorials & Tricks" and
            // says of the two tutor files: "I strongly recommend to all players
            // to study the two tutor levels files.  They'll give you the skill
            // to solve almost all levels available on this web site."
            ["Game-Objects-in-LT"] = (Shelf.Tutorials, "note.objects"),
            ["Tutor-with-Playbacks"] = (Shelf.Tutorials, "note.tutorPlaybacks"),
            ["Tutor"] = (Shelf.Tutorials, "note.tutor"),
            ["Rotary Mirrors-Challenge"] = (Shelf.Tutorials, "note.rotary"),
            ["Tricks"] = (Shelf.Tutorials, "note.tricks"),
            ["Pono's_trick"] = (Shelf.Tutorials, "note.pono"),

            // ---- the help page's hint files ----------------------------------
            // Upstream: "These are levels files that show the level in various
            // stages of completion.  You can use Game, Open data file to load
            // these levels.  Then reload the LaserTank.lvl file..." -- which is
            // exactly the round trip this picker makes cheap.
            ["l40"] = (Shelf.Walkthroughs, "note.l40"),
            ["4triang"] = (Shelf.Walkthroughs, "note.triangles"),
            ["telek-1"] = (Shelf.Walkthroughs, "note.telekinesis"),
            ["inchworm"] = (Shelf.Walkthroughs, "note.inchworm"),
        };

        /// **What to call a collection, where its file name will not do.**  The
        /// label is the stem by default, and on the Collections shelf that is
        /// the right answer: `LaserTank`, `Challenge-IV`, `Sokoban-I` are what
        /// the thirteen are called on laser-tank.com, what their high-score
        /// pages are headed, and what a player asking for help will name.  The
        /// other ten are not like that.  Their stems are what the zip happened
        /// to hold -- `Game-Objects-in-LT` abbreviated to fit, `4triang` and
        /// `telek-1` abbreviated past legibility, `Pono's_trick` with the
        /// underscore a 1996 filesystem wanted -- and none of them is a name a
        /// player would ever say out loud.
        ///
        /// A walkthrough is named for **the level of the original it opens
        /// out**, number first: that is the whole of what it is, it is how the
        /// shelf is already sorted, and `Level 179` is how a player who wants it
        /// arrived at wanting it.  The titles after the colon are the levels'
        /// own `TLEVEL.LName`s, spelled as the game spells them.
        ///
        /// **Untranslated, unlike the blurbs.**  These are not a description of
        /// a collection -- which is copy, and lives in `data/language/*.json` in
        /// eleven languages -- but the name of a particular file, next to twelve
        /// other rows that are file names too; and the four walkthrough titles
        /// are level names the game already draws in English out of the .lvl,
        /// because that is the only place they exist.  Translating the row and
        /// not the level it points at would read as two different levels.
        private static readonly Dictionary<string, string> Names =
            new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Game-Objects-in-LT"] = "Game Objects in LaserTank",
            ["Tutor-with-Playbacks"] = "Tutor with Playbacks",
            ["Tutor"] = "Tutor",
            ["Rotary Mirrors-Challenge"] = "Rotary Mirrors",
            ["Tricks"] = "Tricks",
            ["Pono's_trick"] = "Pono's Trick",

            ["l40"] = "Level 40: Down the Drain",
            ["4triang"] = "Level 149: The 4 Triangles",
            ["telek-1"] = "Level 173: Telekinesis",
            ["inchworm"] = "Level 179: Being an Inchworm",
        };

        /// The label a row is drawn under: the name above, or the file's stem
        /// when nobody has given it one.
        public static string NameOf(Collection c)
            => Names.TryGetValue(c.Label, out string n) ? n : c.Label;

        /// **The sort key inside a shelf, low first.**  Everything not named here
        /// answers 0 and keeps the scan order, which is the sorted walk of the
        /// roots -- right for the twelve Challenge/Beginner/Sokoban/Gary/Special
        /// files, whose names already sort into their series, and right for
        /// whatever the editor saves next.
        ///
        /// `LaserTank` is **-1**: it is the original file, the one the other
        /// twelve are measured against -- upstream's own description of the
        /// Challenge files is *"You're done with the LaserTank.lvl file? You can
        /// now continue with these"* -- and alphabetical order buried it in the
        /// middle of the shelf, between Gary-II and Sokoban-I, reading as the
        /// tenth of thirteen peers.
        ///
        /// The **tutorials run in teaching order**, 1 to 6: what the objects do,
        /// then the beginner's course with its solutions to watch, then the same
        /// course to fight, then the one object big enough for a file of its own,
        /// then the two packs that are built out of what the Tutor files taught.
        /// Alphabetically that is `Game-Objects, Pono's_trick, Rotary Mirrors,
        /// Tricks, Tutor, Tutor-with-Playbacks`, which puts the two tutors last
        /// and the trick pack second -- the exact reverse of the order upstream
        /// tells people to work through them in.
        ///
        /// A **walkthrough's key is the level of the original it opens out** --
        /// 40, 149, 173, 179 -- because that is what it is *about*, it is what
        /// the row is now named after, and the alphabetical run (`4triang,
        /// inchworm, l40, telek-1`) interleaves them meaninglessly.
        ///
        /// All three kinds share one map because they are one thing: a position
        /// in a list.  A stem nobody has placed sorts to the **top** of its
        /// shelf rather than the bottom, 0 being below both 1 and 40; that is
        /// the shelves' existing behaviour and it is the useful one here, since
        /// the only unplaced files are ones somebody has just added.
        private static readonly Dictionary<string, int> Order =
            new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["LaserTank"] = -1,

            ["Game-Objects-in-LT"] = 1,
            ["Tutor-with-Playbacks"] = 2,
            ["Tutor"] = 3,
            ["Rotary Mirrors-Challenge"] = 4,
            ["Tricks"] = 5,
            ["Pono's_trick"] = 6,

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

        /// The catalogue key for the line the picker shows, or null when nobody
        /// has written one.  The caller decides what to put there instead -- see
        /// CollectionList.Draw, which falls back to the path, because a blank
        /// strip reads as a bug and a path is at least true.
        public static string KeyOf(Collection c)
            => ByStem.TryGetValue(c.Label, out var n) ? n.Key : null;
    }
}
