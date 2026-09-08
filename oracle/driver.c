/* ------------------------------------------------------------------------
 * LaserTank headless oracle -- driver.
 *
 * Supplies the globals that LTANK2.C expects from LTANK.C / LTANK_D.C,
 * a message handler standing in for the window proc, and a transliteration
 * of the WM_TIMER tick loop (LTANK.C:579-694) -- which *is* the game's
 * specification.  Emits a per-tick state trace.
 *
 * Nothing in LTANK2.C is modified.  It compiles verbatim against the stub
 * <windows.h>, so its logic-carrying "paint" side effects survive intact.
 * ---------------------------------------------------------------------- */
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "ltank.h"
#include "ltank_d.h"
#include "lt_sfx.h"

/* From win32_stub.c */
int  lt_stub_pump(void);
void lt_stub_pump_clear(void);
extern int lt_stub_dialogs;
extern intptr_t lt_stub_dialog_result;    /* ChangeGO's LoadTID answer */

/* Defined in LTANK2.C but missing from LTANK.H's extern block. */
extern TTANKREC laser;
extern int AniLevel;
extern int LaserBounceOnIce;

/* ===================== globals owned by LTANK.C / LTANK_D.C ===================== */

HINSTANCE hInst;
HWND MainH, Ed1, Ed2, BT1, BT2, BT3, BT4, BT5, BT6, BT7, BT8, BT9;
HWND PlayH, PBCountH;
HMENU MMenu, EMenu;
int   RB_TOS;
int   VHSOn = FALSE;
int   PBHold = FALSE;
int   FileHand, HSClear;
int   QHELP = FALSE;
int   EditorOn = FALSE;
HFONT MyFont;
DWORD DEBUG_Time, DEBUG_Frames;
char  LANGText[SIZE_ALL][MAX_LANG_SIZE];
char  LANGFile[MAX_PATH];
char  HelpFile[MAX_PATH];
TCHAR szFilterOFN[MAX_PATH];
TCHAR szFilterPBfn[MAX_PATH];
int   Sound_On = FALSE;

/* ---- SoundPlay, recorded rather than discarded (Phase 5, step 3) ---------
 *
 * lt_sfx.c is not compiled here -- these two are the whole sound unit as far
 * as the oracle is concerned, and the real one would want a wave device.  What
 * the port needs from the oracle is not audio but the *sequence*: which sound
 * id LTANK2.C asks for, in which order, on which tick.  That sequence is a
 * pure consequence of the logic, so recording it turns "does the port play the
 * right sounds" into the same kind of question as every other one in this
 * project -- a trace diff.  --sound puts it in the trace; without it nothing
 * here is printed at all.
 *
 * This ignores Sound_On, exactly as the trace ignores whether a window is
 * open: muting is the player's business.  The real SoundPlay returns early
 * when !Sound_On, so a *muted* original makes no PlaySound call -- but it
 * makes the same decisions, and the decisions are what is being compared.
 */
#define LT_MAX_SF 256                 /* per tick; SF prints a trailing + past it */
static int sf_log[LT_MAX_SF];
static int sf_n = 0;                  /* calls this tick, may exceed LT_MAX_SF */

void SoundPlay(int s)
{
    if (sf_n < LT_MAX_SF) sf_log[sf_n] = s;
    sf_n++;
}
void SFxInit(void)    { }

LRESULT CALLBACK LoadTID(HWND h, UINT m, WPARAM w, LPARAM l)
{ (void)h; (void)m; (void)w; (void)l; return 0; }

/* ===================== message handler ===================== */

/* The real WM_Dead handler runs GameOn(FALSE) and then puts up a modal
 * dialog.  Headless there is nobody to answer it, so we keep the GameOn
 * (which the real handler does first, and which MoveLaser() observes via
 * `Game_On || VHSOn`) and record the death; the run then halts because the
 * timer is dead -- exactly the state the real game sits in while the dialog
 * is up.  SendMessage(WM_Dead) from CheckLLoc therefore lands mid-tick and
 * PostMessage(WM_Dead) from drowning/black holes lands after it, preserving
 * quirk #8. */
int lt_dead = 0, lt_gameover = 0, lt_newhs = 0, lt_saverec = 0;

LRESULT LT_WndProc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    (void)h; (void)wp; (void)lp;
    switch (msg) {
    /* LTANK.C:718 plays S_Die here: after GameOn(FALSE), and after the VHS
     * arm has already returned.  VHSOn is always FALSE headless, but the test
     * is kept so the shape matches the port's SendDead(). */
    case WM_Dead:     GameOn(FALSE);
                      if (!VHSOn) SoundPlay(S_Die);
                      lt_dead++;                   return 0;
    case WM_GameOver: lt_gameover++;                return 0;
    case WM_NewHS:    lt_newhs++;                   return 0;
    case WM_SaveRec:  lt_saverec++;                 return 0;
    default:                                        return 0;
    }
}

/* ===================== tracing ===================== */

static FILE *trace_fp    = NULL;
static int   trace_field = 0;   /* also dump PF / PF2 */
static int   trace_bmf   = 0;   /* also dump BMF / BMF2 (cosmetic, see below) */
static int   trace_sound = 0;   /* also dump the tick's SoundPlay ids */

static unsigned long fnv1a(const void *p, size_t n)
{
    const unsigned char *b = (const unsigned char *)p;
    unsigned long h = 2166136261UL;
    while (n--) { h ^= *b++; h *= 16777619UL; h &= 0xFFFFFFFFUL; }
    return h;
}

static void put_field(const char *tag, const char *f)
{
    int i;
    fprintf(trace_fp, " %s=", tag);
    for (i = 0; i < 256; i++) fprintf(trace_fp, "%02x", (unsigned char)f[i]);
}

static void trace_tick(long tick)
{
    int i;
    if (!trace_fp) return;

    fprintf(trace_fp,
        "t=%ld T=%d,%d,%d,%d,%d L=%d,%d,%d,%d,%d "
        "S=%u,%u P=%lu C=%d SlT=%d,%d,%d,%d,%d SlO=%d,%d,%d,%d,%d N=%d "
        "A=%d,%d D=%d G=%d H=%08lx,%08lx",
        tick,
        Game.Tank.X, Game.Tank.Y, Game.Tank.Dir, Game.Tank.Firing, Game.Tank.Good,
        laser.X, laser.Y, laser.Dir, laser.Firing, laser.Good,
        (unsigned)Game.ScoreMove, (unsigned)Game.ScoreShot,
        (unsigned long)Game.RecP, ConvMoving,
        SlideT.x, SlideT.y, SlideT.dx, SlideT.dy, SlideT.s,
        SlideO.x, SlideO.y, SlideO.dx, SlideO.dy, SlideO.s,
        SlideMem.count,
        AniLevel, AniCount, lt_dead, Game_On,
        fnv1a(Game.PF, sizeof(TPLAYFIELD)), fnv1a(Game.PF2, sizeof(TPLAYFIELD)));

    /* The sliding stack is game state: IceMoveO walks it top-down and mutates
     * it while iterating (quirk #6).  Entries are 1-based. */
    for (i = 1; i <= SlideMem.count && i < MAX_TICEMEM; i++)
        fprintf(trace_fp, " M%d=%d,%d,%d,%d,%d", i,
                SlideMem.Objects[i].x,  SlideMem.Objects[i].y,
                SlideMem.Objects[i].dx, SlideMem.Objects[i].dy,
                SlideMem.Objects[i].s);

    /* The tick's SoundPlay ids, in call order; "-" for a silent tick.  Before
     * the grids, so the line stays readable when those are on. */
    if (trace_sound) {
        int n = sf_n < LT_MAX_SF ? sf_n : LT_MAX_SF;
        fprintf(trace_fp, " SF=");
        if (n == 0) fputc('-', trace_fp);
        for (i = 0; i < n; i++) fprintf(trace_fp, "%s%d", i ? "," : "", sf_log[i]);
        if (sf_n > LT_MAX_SF) fputc('+', trace_fp);
    }
    if (trace_field) { put_field("PF", Game.PF[0]); put_field("PF2", Game.PF2[0]); }
    if (trace_bmf)   { put_field("BMF", Game.BMF[0]); put_field("BMF2", Game.BMF2[0]); }
    fputc('\n', trace_fp);
}

/* ===================== the tick (LTANK.C:579-694) ===================== */

static void LT_Tick(void)
{
    char temps[30];

    gDC = GetDC(MainH);
    SelectObject(gDC, MyFont);
    if (FindTank)
    {
        FindTank = FALSE;
        PutLevel();
        SetTimer(MainH, 1, GameDelay, NULL);
    }
    if (Ani_On) AniCount++;
    if (AniCount == ani_delay) Animate();     /* Do Animation */
    if (Game.Tank.Firing)
        MoveLaser();                          /* Move laser if one was fired */

    if (PBOpen)
    {
        if (Speed == 2)
        {
            SlowPB++;
            if (SlowPB == SlowPBSet) SlowPB = 1;
        }
        if (PlayBack && (!( ConvMoving || SlideO.s || SlideT.s))
            && ((Speed != 2) || ((Speed == 2) && (SlowPB == 1))))
        {
            PBHold = FALSE;
            itoa(Game.RecP, temps, 10);
            SendMessage(PBCountH, WM_SETTEXT, 0, (LPARAM)(temps));
            if (Speed == 3) SendMessage(PlayH, WM_COMMAND, ID_PLAYBOX_02, 0);
        }
        else PBHold = TRUE;
    }
    /* Check Key Press */
    if ((Game.RecP < (DWORD)RB_TOS) &&
        (!(Game.Tank.Firing || ConvMoving || SlideO.s || SlideT.s || PBHold)))
    {
        switch (RecBuffer[Game.RecP])
        {
        case VK_UP:
            MoveTank(1);                      /* Move tank Up one */
            break;
        case VK_RIGHT:
            MoveTank(2);
            break;
        case VK_DOWN:
            MoveTank(3);
            break;
        case VK_LEFT:
            MoveTank(4);
            break;
        case VK_SPACE:
            {
                UpdateUndo();
                Game.ScoreShot++;             /* do here Not in FireLaser */
                FireLaser(Game.Tank.X, Game.Tank.Y, Game.Tank.Dir, S_Fire);
            }
        }
        Game.RecP++;                          /* Point to next charecter */
        AntiTank();                           /* give the Anti-Tanks a turn to play */
    }
    if (SlideO.s) IceMoveO();
    if (SlideT.s) IceMoveT();
    if (TankDirty) UpDateTank();
    ConvMoving = FALSE;                       /* used to disable Laser on the conveyor */
    switch (Game.PF[Game.Tank.X][Game.Tank.Y])
    {
    case 2:
        if (Game_On)                          /* Reached the Flag */
        {
            GameOn(FALSE);
            SoundPlay(S_EndLev);
            /* PBOpen is TRUE for the oracle, exactly as it is during a real
             * .lpb playback, so the original skips CheckHighScore() and
             * LoadNextLevel() here.  The corpus is never written to. */
        }
        break;
    case 3:
        PostMessage(MainH, WM_Dead, 0, 0);    /* Water */
        break;
    case 15:
        if (CheckLoc(Game.Tank.X, Game.Tank.Y - 1))   /* Conveyor Up */
            ConvMoveTank(0, -1, TRUE);
        break;
    case 16:
        if (CheckLoc(Game.Tank.X + 1, Game.Tank.Y))
            ConvMoveTank(1, 0, TRUE);
        break;
    case 17:
        if (CheckLoc(Game.Tank.X, Game.Tank.Y + 1))
            ConvMoveTank(0, 1, TRUE);
        break;
    case 18:
        if (CheckLoc(Game.Tank.X - 1, Game.Tank.Y))
            ConvMoveTank(-1, 0, TRUE);
    }

    /* Check the mouse Buffer */
    if ((Game.RecP == (DWORD)RB_TOS) && (MB_TOS != MB_SP) &&
        (!(Game.Tank.Firing || ConvMoving || SlideO.s || SlideT.s)))
    {
        if (MouseOperation(MB_SP))
        {
            MB_SP++;
            if (MB_SP == MaxMBuffer) MB_SP = 0;
        } else {
            MB_SP = MB_TOS;
        }
    }
    if (TankDirty) UpDateTank();
    ReleaseDC(MainH, gDC);
}

/* ===================== setup ===================== */

/* Quiescent means "the world has settled and the next key would be taken".
 * Same condition the tick loop uses to consume a key (LTANK.C:613). */
static int quiescent(void)
{
    return !(Game.Tank.Firing || ConvMoving || SlideO.s || SlideT.s);
}

static void oracle_init(void)
{
    /* Replay configuration.  PBOpen/PlayBack/Speed=1 reproduce exactly what
     * the real program does when a .lpb is played back: PBHold ends up equal
     * to (ConvMoving || SlideO.s || SlideT.s), which the key-consume test
     * already covers, so live play and playback share one code path. */
    Ani_On     = TRUE;      /* the tutor pack requires animation on */
    PBOpen     = TRUE;
    PlayBack   = TRUE;
    Speed      = 1;
    SlowPB     = 1;
    Recording  = FALSE;
    ARecord    = FALSE;
    RLL        = FALSE;
    SkipCL     = FALSE;
    DWarn      = TRUE;      /* suppress the "save your game?" prompt */
    Difficulty = 0x1F;      /* non-zero: skip the difficulty dialog */
    GraphM     = 0;
    Sound_On   = FALSE;
    HFileName[0] = 0;       /* no .hs file -> F2 is INVALID_HANDLE_VALUE */

    InitBuffers();
}

static long file_size(const char *p)
{
    long n;
    FILE *f = fopen(p, "rb");
    if (!f) return -1;
    fseek(f, 0, SEEK_END);
    n = ftell(f);
    fclose(f);
    return n;
}

/* Load a .lpb: 66-byte TRECORDREC header then raw VK bytes. */
static int load_playback(const char *path)
{
    FILE *f = fopen(path, "rb");
    if (!f) { fprintf(stderr, "oracle: cannot open %s\n", path); return 0; }
    if (fread(&PBRec, 1, sizeof(PBRec), f) != sizeof(PBRec)) {
        fprintf(stderr, "oracle: %s: short header\n", path);
        fclose(f); return 0;
    }
    if (RecBufSize <= PBRec.Size) {
        RecBufSize = PBRec.Size + 1;
        RecBuffer  = GlobalReAlloc(RecBuffer, RecBufSize, GMEM_MOVEABLE);
    }
    if (fread(RecBuffer, 1, PBRec.Size, f) != PBRec.Size) {
        fprintf(stderr, "oracle: %s: short keystream\n", path);
        fclose(f); return 0;
    }
    fclose(f);
    return 1;
}

/* ===================== the script driver (Phase 5, step 4) =====================
 *
 * --keys installs a whole keystream up front, which is what a .lpb playback
 * does and all Phases 1-3 ever needed.  It cannot express a *command*: Undo,
 * Save Position and Restore Position are WM_COMMAND cases, not bytes in
 * RecBuffer, so no keystream reaches them and Phase 2 left UndoStep unported
 * for exactly that reason.
 *
 * A script can.  It is a sequence of tokens consumed **at most one per tick**,
 * at the top of the tick, before LT_Tick():
 *
 *   u d l r f   press a game key -- AddKBuff, but only when the buffer has
 *               drained (RB_TOS == RecP).  Otherwise the token waits and this
 *               tick consumes nothing, which is the pending-key rule the real
 *               WM_KEYDOWN filter enforces (LTANK.C:573) and the rule that
 *               makes one-key-at-a-time identical to a preloaded buffer: the
 *               tick takes a key only when the world is quiescent anyway.
 *   .           let one tick pass.  This is what makes the *timing* of a
 *               command expressible -- "f..z" undoes with the laser still in
 *               flight, which restores Game.Tank.Firing from the snapshot while
 *               the laser global keeps flying.
 *   z           command 110, Undo (LTANK.C:946).
 *   Z           the DeadBox's "Undo Last Move" (LTANK.C:727): command 110 and
 *               then GameOn(TRUE), the one path that resumes a dead game.  It
 *               clears the oracle's own dead counter with it, because that
 *               counter is instrumentation and the original has no such flag.
 *   c v         commands 111 and 112, Save / Restore Position (LTANK.C:955).
 *
 * A dead or finished game gets no ticks -- GameOn() is literally KillTimer
 * (LTANK2.C:881) -- but it still consumes tokens, so `Z` can bring it back.
 */
static const char *script = NULL;
static size_t script_at = 0;
/* EnableMenuItem(MMenu,112,...): Restore Position is grayed until Save Position
 * has been used (LTANK.C:957) and grayed again by every LoadLevel
 * (LTANK2.C:1028).  Windows enforces that, not LTANK.C, so a driver standing in
 * for the window proc has to carry it -- and it matters rather than being
 * politeness: `SaveGame` is a zero-initialised global, so restoring before ever
 * saving copies a blank record over the live game, dropping the tank at 0,0 on
 * an empty board.  The copy is defined, but the tank then keeps whatever ice
 * slide was running (command 112 does not stop sliding, unlike UndoStep), and
 * ConvMoveTank walks off the end of Game.PF.  That is out-of-bounds in C and a
 * thrown IndexOutOfRangeException in the port -- a state the original cannot
 * reach, so neither driver offers it.  tools/undo_check.py found it on
 * Tutor.LVL level 85 with the script "llv". */
static int can_restore = 0;

static int script_key(char c)
{
    switch (c) {
    case 'u': case 'U': return VK_UP;
    case 'd': case 'D': return VK_DOWN;
    case 'l': case 'L': return VK_LEFT;
    case 'r': case 'R': return VK_RIGHT;
    case 'f': case 'F': return VK_SPACE;
    default:            return 0;
    }
}

/* One hex digit, 0-15, or -1.  The click tokens spell a board coordinate with
 * two of them because 16 columns need more than a decimal digit. */
static int script_hex(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

/* `mXY` / `nXY`: WM_LBUTTONDOWN / WM_RBUTTONDOWN's non-editor arm (LTANK.C:785,
 * :825), which is a ring-buffer push and nothing else.  Step 5 added them for
 * the same reason step 4 added z/Z/c/v: MouseOperation is reachable only from
 * this buffer, so without a way to fill it the C could not be asked what it
 * does.  The push happens whatever the key buffer is doing -- a click in the
 * original is a window message, not a keystroke, and it is queued even while
 * the tank is mid-slide.  A truncated token at the end of the script consumes
 * the rest and posts nothing, in both drivers. */
static void script_click(int z)
{
    int x, y;

    if (script_at + 2 >= strlen(script)) { script_at = strlen(script); return; }
    x = script_hex(script[script_at + 1]);
    y = script_hex(script[script_at + 2]);
    script_at += 3;
    if (x < 0 || y < 0) return;
    if ((x < 0) || (x > 15) || (y < 0) || (y > 15)) return;
    MBuffer[MB_TOS].X = x;
    MBuffer[MB_TOS].Y = y;
    MBuffer[MB_TOS].Z = z;
    MB_TOS++;
    if (MB_TOS == MaxMBuffer) MB_TOS = 0;
}

/* -> 1 if this tick should run, 0 if the script is spent and the game is over. */
static void script_feed(void)
{
    char c;
    int vk;

    if (script_at >= strlen(script)) return;
    c  = script[script_at];
    if (c == 'm') { script_click(1); return; }
    if (c == 'n') { script_click(2); return; }
    vk = script_key(c);
    if (vk) {
        if ((DWORD)RB_TOS != Game.RecP) return;    /* still pending: wait */
        if (RB_TOS >= RecBufSize) {
            RecBufSize += 1024;
            RecBuffer = GlobalReAlloc(RecBuffer, RecBufSize, GMEM_MOVEABLE);
        }
        RecBuffer[RB_TOS++] = (char)vk;
        script_at++;
        return;
    }
    script_at++;
    switch (c) {
    case '.': break;                                        /* idle one tick */
    case 'z': UndoStep();                        break;     /* command 110   */
    case 'Z': UndoStep(); GameOn(TRUE); lt_dead = 0; break; /* DeadBox Undo  */
    case 'c': SaveGame = Game;                              /* command 111   */
              can_restore = 1;                   break;
    case 'v': if (!can_restore) break;                      /* command 112   */
              Game = SaveGame;
              RB_TOS = Game.RecP;
              MB_TOS = MB_SP = 0;                break;
    default:  break;                             /* unknown: skipped, as --keys does */
    }
}

static int script_done(void)
{
    return script_at >= strlen(script);
}

/* ===================== the editor (Phase 5, step 5) =====================
 *
 * `--edit STR` runs an edit script over a loaded level and traces the board
 * after every token.  No tick runs: the editor calls GameOn(FALSE) first thing
 * (LTANK.C:1086), so an edited board is a still picture and every difference
 * between two engines is a difference in the *edit*, not in the clock.
 *
 * The one function here that is really the C is `ChangeGO` -- LTANK2.C:809,
 * compiled verbatim by oracle/build.sh, and the reason this mode exists.
 * Everything around it (Clear Field, the four Shifts, the tunnel-wait strip on
 * the way in) is an LTANK.C window-proc case, so it is written twice on
 * purpose, here and in LaserTank.Core.Editor, exactly as step 4 wrote undo's
 * three commands twice.
 *
 * Tokens.  Two hex digits spell a cell, one spells a small number.
 *
 *   <oo   select the left  object (00..1b; 1b = 27 is reachable, see below)
 *   >oo   select the right object
 *   tN    the tunnel id ChangeGO's dialog will answer with (0..7)
 *   lXY   left  click        rXY  right click       sXY  Shift+left = rotate
 *   pXY   left  drag         qXY  right drag        PXY  Shift+drag (a no-op)
 *   R L U D   shift the board right / left / up / down   (710/711/712/713)
 *   C     Clear Field (601)          E   re-enter the editor (201's board half)
 *
 * `<1b` is not a typo: the palette's own bound is `i > MaxObjects+1`, so 27 is
 * selectable by clicking one slot past the last sprite, and `GetOBM(27)` falls
 * through its range test to bitmap 1.  It is in the token set because it is in
 * the original.
 */
static const char *edit_script = NULL;   /* EditorOn is already a global above */
static int CurSelBM_L_drv = 3;     /* LTANK2.C:42, but the driver's own copy:  */
static int CurSelBM_R_drv = 0;     /* the editor's selectors are set by clicks */
                                   /* on a window this driver does not have.   */
static int edit_tunnel = 0;

/* LTANK.C:18 -- 27 entries, of which the initialiser gives 25, so the last two
 * are the zeroes C fills in.  Rotating thin ice or the tunnel selector really
 * does turn the cell into dirt. */
static const int GetNextBM[MaxObjects + 1] =
    {0,1,2,3,4,5,6,8,9,10,7,12,13,14,11,16,17,18,15,19,21,22,23,20,24};

static int edit_hex(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

/* Command 201's board half (LTANK.C:1110): strip the tunnel wait bits. */
static void edit_enter(void)
{
    int x, y;
    EditorOn = TRUE;
    GameOn(FALSE);
    for (x = 0; x < 16; x++) for (y = 0; y < 16; y++)
        if (ISTunnel(x, y)) Game.PF[x][y] &= 0xFE;
}

/* Command 601, "Clear Field" (LTANK.C:1135). */
static void edit_clear(void)
{
    int x, y;
    for (x = 0; x < 16; x++) for (y = 0; y < 16; y++)
    {
        Game.PF[x][y]   = 0;
        Game.BMF[x][y]  = 1;
        Game.BMF2[x][y] = 1;
        Game.PF2[x][y]  = 0;
    }
    Game.Tank.X = 7;
    Game.Tank.Y = 15;
    Game.Tank.Dir = 1;
    Game.Tank.Firing = FALSE;
}

/* Commands 710/711/712/713 (LTANK.C:1281..1348).  PF and BMF both move and both
 * wrap; PF2 and BMF2 do not move at all; the tank wraps by increment. */
static void edit_shift(int dx, int dy)
{
    TPLAYFIELD sPF, sBMF;
    int x, y, sx, sy;

    for (x = 0; x < 16; x++) for (y = 0; y < 16; y++)
    {
        sx = ((x - dx) % 16 + 16) % 16;
        sy = ((y - dy) % 16 + 16) % 16;
        sPF[x][y]  = Game.PF[sx][sy];
        sBMF[x][y] = Game.BMF[sx][sy];
    }
    memcpy(Game.PF,  sPF,  sizeof(TPLAYFIELD));
    memcpy(Game.BMF, sBMF, sizeof(TPLAYFIELD));

    Game.Tank.X += dx;
    if (Game.Tank.X == 16) Game.Tank.X = 0;
    if (Game.Tank.X < 0)   Game.Tank.X = 15;
    Game.Tank.Y += dy;
    if (Game.Tank.Y == 16) Game.Tank.Y = 0;
    if (Game.Tank.Y < 0)   Game.Tank.Y = 15;
}

/* One token.  -> the number of characters consumed, or 0 at the end. */
static int edit_token(const char *p)
{
    int x, y, v;
    char c = p[0];

    switch (c) {
    case '<': case '>':
        if (!p[1] || !p[2]) return (int)strlen(p);
        v = edit_hex(p[1]) * 16 + edit_hex(p[2]);
        if (v >= 0) { if (c == '<') CurSelBM_L_drv = v; else CurSelBM_R_drv = v; }
        return 3;
    case 't':
        if (!p[1]) return 1;
        v = edit_hex(p[1]);
        if (v >= 0) edit_tunnel = v & 7;
        return 2;
    case 'l': case 'r': case 's': case 'p': case 'q': case 'P':
        if (!p[1] || !p[2]) return (int)strlen(p);
        x = edit_hex(p[1]); y = edit_hex(p[2]);
        if (x < 0 || y < 0 || x > 15 || y > 15) return 3;
        lt_stub_dialog_result = edit_tunnel;
        switch (c) {
        case 'l':                                   /* WM_LBUTTONDOWN        */
            if (Game.PF[x][y] != CurSelBM_L_drv) ChangeGO(x, y, CurSelBM_L_drv);
            break;
        case 's':                                   /* ... with Shift: rotate */
            if (Game.PF[x][y] <= MaxObjects) ChangeGO(x, y, GetNextBM[Game.PF[x][y]]);
            break;
        case 'r':                                   /* WM_RBUTTONDOWN        */
            if (Game.PF[x][y] != CurSelBM_R_drv) ChangeGO(x, y, CurSelBM_R_drv);
            break;
        case 'p':                                   /* WM_MOUSEMOVE, left    */
            if ((Game.PF[x][y] != CurSelBM_L_drv) && (CurSelBM_L_drv != MaxObjects))
                ChangeGO(x, y, CurSelBM_L_drv);
            break;
        case 'q':                                   /* WM_MOUSEMOVE, right   */
            if ((Game.PF[x][y] != CurSelBM_R_drv) && (CurSelBM_R_drv != MaxObjects))
                ChangeGO(x, y, CurSelBM_R_drv);
            break;
        case 'P':                                   /* ... with Shift: return */
            break;
        }
        lt_stub_dialog_result = IDCANCEL;
        return 3;
    case 'R': edit_shift( 1,  0); return 1;
    case 'L': edit_shift(-1,  0); return 1;
    case 'U': edit_shift( 0, -1); return 1;
    case 'D': edit_shift( 0,  1); return 1;
    case 'C': edit_clear();       return 1;
    case 'E': edit_enter();       return 1;
    default:  return 1;                             /* unknown: skipped      */
    }
}

/* `s` above is the one arm whose guard is *not* the click's own: LTANK.C:806
 * writes `ChangeGO(x,y,GetNextBMArray[Game.PF[x][y]])` with no range test at
 * all, so a tunnel cell (0x40 | id<<1) indexes past a 27-int array.  That read
 * has no defined value, so both engines skip it and this comment is the record
 * -- the same call `GFXInit`'s missing parentheses got in step 2. */

static long edit_run(void)
{
    long step = 0;
    const char *p = edit_script;

    edit_enter();
    trace_tick(0);
    while (*p) {
        int n = edit_token(p);
        if (n <= 0) break;
        p += n;
        trace_tick(++step);
    }
    return step;
}

static void usage(void)
{
    fprintf(stderr,
      "usage: oracle --levels FILE.lvl (--lpb FILE.lpb | --level N (--keys | --script) STR)\n"
      "              [--trace FILE] [--field] [--bmf] [--sound] [--max-ticks N]\n"
      "              [--quiet]\n"
      "\n"
      "  --lpb FILE     replay a recorded solution; level number comes from its header\n"
      "  --level N      1-based level number (with --keys / --script)\n"
      "  --keys STR     keystream as characters: u d l r f  (or raw decimal VK codes\n"
      "                 separated by commas)\n"
      "  --script STR   one token per tick: u d l r f press, . idles, z undoes,\n"
      "                 Z undoes a death and resumes, c/v save/restore position,\n"
      "                 mXY / nXY left/right click cell XY (two hex digits)\n"
      "  --field        include full PF / PF2 hex in the trace\n"
      "  --bmf          include BMF / BMF2 (cosmetic: nothing in the logic reads them)\n"
      "  --sound        include SF, the SoundPlay ids the tick asked for\n"
      "  --edit STR     level editor, one token per traced step; no tick runs:\n"
      "                 <oo />oo pick the left/right object, tN the tunnel id,\n"
      "                 lXY rXY click, sXY Shift+click (rotate), pXY qXY drag,\n"
      "                 PXY Shift+drag, R L U D shift the board, C clear, E enter\n");
}

int main(int argc, char **argv)
{
    const char *levels = NULL, *lpb = NULL, *keys = NULL, *tracepath = NULL;
    const char *result;
    int level = 0, quiet = 0;
    long max_ticks = 200000, tick = 0;
    int i, won = 0;

    for (i = 1; i < argc; i++) {
        if      (!strcmp(argv[i], "--levels") && i + 1 < argc) levels    = argv[++i];
        else if (!strcmp(argv[i], "--lpb")    && i + 1 < argc) lpb       = argv[++i];
        else if (!strcmp(argv[i], "--keys")   && i + 1 < argc) keys      = argv[++i];
        else if (!strcmp(argv[i], "--script") && i + 1 < argc) script    = argv[++i];
        else if (!strcmp(argv[i], "--edit")   && i + 1 < argc) edit_script = argv[++i];
        else if (!strcmp(argv[i], "--trace")  && i + 1 < argc) tracepath = argv[++i];
        else if (!strcmp(argv[i], "--level")  && i + 1 < argc) level     = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--max-ticks") && i + 1 < argc) max_ticks = atol(argv[++i]);
        else if (!strcmp(argv[i], "--field")) trace_field = 1;
        else if (!strcmp(argv[i], "--bmf"))   trace_bmf   = 1;
        else if (!strcmp(argv[i], "--sound")) trace_sound = 1;
        else if (!strcmp(argv[i], "--quiet")) quiet       = 1;
        else { usage(); return 2; }
    }
    if (!levels || (!lpb && !keys && !script && !edit_script)) { usage(); return 2; }
    if (script && (lpb || keys)) {
        fprintf(stderr, "oracle: --script cannot be combined with --lpb or --keys\n");
        return 2;
    }
    if (edit_script && (lpb || keys || script)) {
        fprintf(stderr, "oracle: --edit is a mode of its own; it runs no ticks\n");
        return 2;
    }

    if (file_size(levels) < 0) {
        fprintf(stderr, "oracle: cannot open %s\n", levels);
        return 2;
    }
    strncpy(FileName, levels, MAX_PATH - 1);

    oracle_init();

    if (lpb) {
        if (!load_playback(lpb)) return 2;
        level = PBRec.Level;
    }

    /* LoadNextLevel reads level CurLevel then post-increments it. */
    CurLevel = level - 1;
    if (!LoadNextLevel(TRUE, TRUE)) {
        fprintf(stderr, "oracle: failed to load level %d from %s\n", level, levels);
        return 2;
    }

    if (lpb && strcmp(CurRecData.LName, PBRec.LName) != 0) {
        fprintf(stderr, "oracle: level name mismatch: lpb says \"%s\", lvl %d is \"%s\"\n",
                PBRec.LName, level, CurRecData.LName);
        return 3;
    }

    /* Install the keystream.  LoadNextLevel resets RecP/RB_TOS, so do it after.
     * A script installs nothing: it presses one key at a time, as a player
     * does, so RB_TOS starts at 0 and grows. */
    if (lpb) {
        RB_TOS = PBRec.Size;
    } else if (script || edit_script) {
        RB_TOS = 0;                /* the editor presses nothing either */
    } else {
        int n = 0;
        const char *p;
        for (p = keys; *p; p++) {
            int vk = 0;
            switch (*p) {
            case 'u': case 'U': vk = VK_UP;    break;
            case 'd': case 'D': vk = VK_DOWN;  break;
            case 'l': case 'L': vk = VK_LEFT;  break;
            case 'r': case 'R': vk = VK_RIGHT; break;
            case 'f': case 'F': vk = VK_SPACE; break;
            default: continue;
            }
            if (n >= RecBufSize) { RecBufSize += 1024; RecBuffer = GlobalReAlloc(RecBuffer, RecBufSize, GMEM_MOVEABLE); }
            RecBuffer[n++] = (char)vk;
        }
        RB_TOS = n;
    }
    Game.RecP = 0;

    if (tracepath) {
        trace_fp = fopen(tracepath, "wb");
        if (!trace_fp) { fprintf(stderr, "oracle: cannot write %s\n", tracepath); return 2; }
        fprintf(trace_fp, "# lasertank oracle trace\n");
        /* `keys` is how much input this run was given, which difftrace uses to
         * refuse a diff of two different inputs.  A script has pressed nothing
         * yet, so RB_TOS is 0 and the token count is the honest answer. */
        fprintf(trace_fp, "# levels=%s level=%d name=%s author=%s keys=%d\n",
                levels, level, CurRecData.LName, CurRecData.Author,
                script ? (int)strlen(script)
                       : edit_script ? (int)strlen(edit_script) : RB_TOS);
    }

    /* ---- run ---- */
    lt_stub_pump_clear();
    if (edit_script) {
        /* The editor is not the game: GameOn(FALSE) is the first thing command
         * 201 does, so nothing here ticks and `tick` counts *edits*.  The trace
         * format is the same one difftrace already reads, so an edit diff needs
         * no new tooling -- only a new input language. */
        tick = edit_run();
        goto finish;
    }
    trace_tick(0);
    if (script) {
        /* The script loop.  It differs from the keystream loop below in three
         * places and nowhere else: a token is fed before each tick, a dead or
         * finished game still consumes tokens (so `Z` can resume it) but takes
         * no tick, and "nothing further can happen" also requires the script to
         * be spent.  LT_Tick itself is untouched -- the commands are the window
         * proc's, and that is exactly where they run here. */
        while (tick < max_ticks) {
            size_t before = script_at;
            script_feed();
            if (Game_On && !lt_dead) {
                tick++;
                sf_n = 0;
                LT_Tick();
                lt_stub_pump();
                trace_tick(tick);
            } else if (script_at == before) {
                /* No tick to advance the clock and no token consumed -- a key
                 * waiting on a buffer that a dead game will never drain.  Stop
                 * rather than spin: max_ticks cannot save a loop that does not
                 * tick. */
                break;
            }
            /* ... and the mouse buffer counts as "keys left": a click is
             * drained by the *next* tick, so a script ending in one would
             * otherwise stop before MouseOperation ever ran. */
            if (script_done() && Game.RecP >= (DWORD)RB_TOS && MB_TOS == MB_SP
                && quiescent() && Game_On)
                break;
        }
    } else
    while (Game_On && !lt_dead && tick < max_ticks) {
        tick++;
        sf_n = 0;                /* SF is per tick, and the pump's S_Die counts */
        LT_Tick();
        lt_stub_pump();          /* dispatch anything PostMessage'd this tick */
        trace_tick(tick);
        /* Out of keys and the world has settled: nothing further can happen. */
        if (Game.RecP >= (DWORD)RB_TOS && quiescent() && Game_On) break;
    }

finish:
    won = (!lt_dead) && (Game.PF[Game.Tank.X][Game.Tank.Y] == 2);

    /* An edit run has no outcome to report -- nothing ticked, so "did the tank
     * reach the flag" is a question about a board nobody played.  It says EDIT
     * on both sides instead, which keeps the two footers comparable and keeps
     * replay_all.py from ever reading an edit as a win. */
    result = edit_script ? "EDIT" : won ? "WIN" : (lt_dead ? "DEAD" : "UNFINISHED");

    if (trace_fp) {
        fprintf(trace_fp, "# result=%s ticks=%ld moves=%u shots=%u keys_used=%lu/%d dialogs=%d\n",
                result,
                tick, (unsigned)Game.ScoreMove, (unsigned)Game.ScoreShot,
                (unsigned long)Game.RecP, RB_TOS, lt_stub_dialogs);
        fclose(trace_fp);
    }
    if (!quiet) {
        if (edit_script)
            printf("%-10s level=%-5d edits=%-6ld tank=%d,%d  %s\n",
                   result, level, tick, Game.Tank.X, Game.Tank.Y, CurRecData.LName);
        else
            printf("%-10s level=%-5d ticks=%-6ld moves=%-4u shots=%-4u keys=%lu/%d  %s\n",
                   result,
                   level, tick, (unsigned)Game.ScoreMove, (unsigned)Game.ScoreShot,
                   (unsigned long)Game.RecP, RB_TOS, CurRecData.LName);
    }
    return edit_script ? 0 : (won ? 0 : 1);
}
