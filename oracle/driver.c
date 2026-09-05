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

void SoundPlay(int s) { (void)s; }
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
    case WM_Dead:     GameOn(FALSE); lt_dead++;     return 0;
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

static void usage(void)
{
    fprintf(stderr,
      "usage: oracle --levels FILE.lvl (--lpb FILE.lpb | --level N --keys STR)\n"
      "              [--trace FILE] [--field] [--bmf] [--max-ticks N] [--quiet]\n"
      "\n"
      "  --lpb FILE     replay a recorded solution; level number comes from its header\n"
      "  --level N      1-based level number (with --keys)\n"
      "  --keys STR     keystream as characters: u d l r f  (or raw decimal VK codes\n"
      "                 separated by commas)\n"
      "  --field        include full PF / PF2 hex in the trace\n"
      "  --bmf          include BMF / BMF2 (cosmetic: nothing in the logic reads them)\n");
}

int main(int argc, char **argv)
{
    const char *levels = NULL, *lpb = NULL, *keys = NULL, *tracepath = NULL;
    int level = 0, quiet = 0;
    long max_ticks = 200000, tick = 0;
    int i, won = 0;

    for (i = 1; i < argc; i++) {
        if      (!strcmp(argv[i], "--levels") && i + 1 < argc) levels    = argv[++i];
        else if (!strcmp(argv[i], "--lpb")    && i + 1 < argc) lpb       = argv[++i];
        else if (!strcmp(argv[i], "--keys")   && i + 1 < argc) keys      = argv[++i];
        else if (!strcmp(argv[i], "--trace")  && i + 1 < argc) tracepath = argv[++i];
        else if (!strcmp(argv[i], "--level")  && i + 1 < argc) level     = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--max-ticks") && i + 1 < argc) max_ticks = atol(argv[++i]);
        else if (!strcmp(argv[i], "--field")) trace_field = 1;
        else if (!strcmp(argv[i], "--bmf"))   trace_bmf   = 1;
        else if (!strcmp(argv[i], "--quiet")) quiet       = 1;
        else { usage(); return 2; }
    }
    if (!levels || (!lpb && !keys)) { usage(); return 2; }

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

    /* Install the keystream.  LoadNextLevel resets RecP/RB_TOS, so do it after. */
    if (lpb) {
        RB_TOS = PBRec.Size;
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
        fprintf(trace_fp, "# levels=%s level=%d name=%s author=%s keys=%d\n",
                levels, level, CurRecData.LName, CurRecData.Author, RB_TOS);
    }

    /* ---- run ---- */
    lt_stub_pump_clear();
    trace_tick(0);
    while (Game_On && !lt_dead && tick < max_ticks) {
        tick++;
        LT_Tick();
        lt_stub_pump();          /* dispatch anything PostMessage'd this tick */
        trace_tick(tick);
        /* Out of keys and the world has settled: nothing further can happen. */
        if (Game.RecP >= (DWORD)RB_TOS && quiescent() && Game_On) break;
    }

    won = (!lt_dead) && (Game.PF[Game.Tank.X][Game.Tank.Y] == 2);

    if (trace_fp) {
        fprintf(trace_fp, "# result=%s ticks=%ld moves=%u shots=%u keys_used=%lu/%d dialogs=%d\n",
                won ? "WIN" : (lt_dead ? "DEAD" : "UNFINISHED"),
                tick, (unsigned)Game.ScoreMove, (unsigned)Game.ScoreShot,
                (unsigned long)Game.RecP, RB_TOS, lt_stub_dialogs);
        fclose(trace_fp);
    }
    if (!quiet) {
        printf("%-10s level=%-5d ticks=%-6ld moves=%-4u shots=%-4u keys=%lu/%d  %s\n",
               won ? "WIN" : (lt_dead ? "DEAD" : "UNFINISHED"),
               level, tick, (unsigned)Game.ScoreMove, (unsigned)Game.ScoreShot,
               (unsigned long)Game.RecP, RB_TOS, CurRecData.LName);
    }
    return won ? 0 : 1;
}
