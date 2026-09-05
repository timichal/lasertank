/* ------------------------------------------------------------------------
 * LaserTank headless oracle -- Win32 stub implementations.
 *
 * Three kinds of function live here:
 *
 *   REAL      memory, file I/O, message dispatch.  These carry game state and
 *             must behave like Windows does.
 *   DEFAULTED ini reads (hand back the caller's default), message boxes and
 *             dialogs (return a fixed choice).
 *   NO-OP     every GDI / menu / window call.
 *
 * The one subtlety is SendMessage vs PostMessage.  LaserTank relies on the
 * difference: CheckLLoc() kills the tank with a *synchronous* SendMessage,
 * while drowning and black holes use a *deferred* PostMessage (quirk #8,
 * deliberately changed in 4.0.6).  So SendMessage re-enters LT_WndProc
 * immediately and PostMessage queues for the driver to drain after the
 * current tick handler returns -- which is exactly what a Windows message
 * pump does.
 * ---------------------------------------------------------------------- */
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <direct.h>

/* Implemented by the driver. */
LRESULT LT_WndProc(HWND h, UINT msg, WPARAM wp, LPARAM lp);

/* ===================== deferred message queue ===================== */

#define LT_POSTQ_MAX 64

typedef struct { HWND h; UINT msg; WPARAM wp; LPARAM lp; } LT_MSG;

static LT_MSG lt_postq[LT_POSTQ_MAX];
static int    lt_postq_head = 0, lt_postq_tail = 0;

LRESULT SendMessageA(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    return LT_WndProc(h, msg, wp, lp);
}

BOOL PostMessageA(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    int next = (lt_postq_tail + 1) % LT_POSTQ_MAX;
    if (next == lt_postq_head) return FALSE;          /* queue full; drop */
    lt_postq[lt_postq_tail].h   = h;
    lt_postq[lt_postq_tail].msg = msg;
    lt_postq[lt_postq_tail].wp  = wp;
    lt_postq[lt_postq_tail].lp  = lp;
    lt_postq_tail = next;
    return TRUE;
}

/* Drain the posted-message queue, the way the real pump would between
 * WM_TIMER handlers.  Returns how many messages were dispatched. */
int lt_stub_pump(void)
{
    int n = 0;
    while (lt_postq_head != lt_postq_tail) {
        LT_MSG m = lt_postq[lt_postq_head];
        lt_postq_head = (lt_postq_head + 1) % LT_POSTQ_MAX;
        LT_WndProc(m.h, m.msg, m.wp, m.lp);
        n++;
    }
    return n;
}

void lt_stub_pump_clear(void) { lt_postq_head = lt_postq_tail = 0; }

/* ===================== memory (real) ===================== */

HGLOBAL GlobalAlloc(UINT flags, size_t bytes)            { (void)flags; return calloc(1, bytes ? bytes : 1); }
HGLOBAL GlobalReAlloc(HGLOBAL p, size_t bytes, UINT f)   { (void)f; return realloc(p, bytes ? bytes : 1); }
HGLOBAL GlobalFree(HGLOBAL p)                            { free(p); return NULL; }
HLOCAL  LocalFree(HLOCAL p)                              { free(p); return NULL; }

/* ===================== files (real) ===================== */

typedef struct { FILE *fp; } LT_FILE;

HANDLE CreateFileA(LPCSTR name, DWORD access, DWORD share, void *sa,
                   DWORD disp, DWORD flags, HANDLE tmpl)
{
    const char *mode;
    LT_FILE *h;
    FILE *fp;

    (void)share; (void)sa; (void)flags; (void)tmpl;

    if (access & GENERIC_WRITE) {
        if (disp == CREATE_ALWAYS)      mode = "wb+";
        else                            mode = "rb+";   /* OPEN_ALWAYS/EXISTING */
    } else {
        mode = "rb";
    }

    fp = fopen(name, mode);
    if (!fp && (access & GENERIC_WRITE) && disp == OPEN_ALWAYS)
        fp = fopen(name, "wb+");                        /* create if missing */
    if (!fp) return INVALID_HANDLE_VALUE;

    h = (LT_FILE *)malloc(sizeof *h);
    if (!h) { fclose(fp); return INVALID_HANDLE_VALUE; }
    h->fp = fp;
    return (HANDLE)h;
}

BOOL ReadFile(HANDLE h, void *buf, DWORD n, DWORD *got, void *ov)
{
    size_t r;
    (void)ov;
    if (h == INVALID_HANDLE_VALUE || !h) { if (got) *got = 0; return FALSE; }
    r = fread(buf, 1, n, ((LT_FILE *)h)->fp);
    if (got) *got = (DWORD)r;
    return TRUE;
}

BOOL WriteFile(HANDLE h, const void *buf, DWORD n, DWORD *put, void *ov)
{
    size_t w;
    (void)ov;
    if (h == INVALID_HANDLE_VALUE || !h) { if (put) *put = 0; return FALSE; }
    w = fwrite(buf, 1, n, ((LT_FILE *)h)->fp);
    if (put) *put = (DWORD)w;
    return TRUE;
}

DWORD SetFilePointer(HANDLE h, LONG dist, LONG *distHigh, DWORD method)
{
    int whence;
    (void)distHigh;
    if (h == INVALID_HANDLE_VALUE || !h) return INVALID_SET_FILE_POINTER;
    whence = (method == FILE_BEGIN)   ? SEEK_SET
           : (method == FILE_CURRENT) ? SEEK_CUR
                                      : SEEK_END;
    if (fseek(((LT_FILE *)h)->fp, dist, whence) != 0) return INVALID_SET_FILE_POINTER;
    return (DWORD)ftell(((LT_FILE *)h)->fp);
}

BOOL CloseHandle(HANDLE h)
{
    if (h == INVALID_HANDLE_VALUE || !h) return FALSE;
    fclose(((LT_FILE *)h)->fp);
    free(h);
    return TRUE;
}

DWORD GetCurrentDirectoryA(DWORD n, LPSTR buf)
{
    if (!getcwd(buf, (int)n)) { if (n) buf[0] = 0; return 0; }
    return (DWORD)strlen(buf);
}

BOOL  SetCurrentDirectoryA(LPCSTR dir) { return chdir(dir) == 0; }
DWORD lt_GetLastError(void)            { return 0; }

DWORD FormatMessageA(DWORD flags, const void *src, DWORD msgId, DWORD langId,
                     LPSTR buf, DWORD size, void *args)
{
    /* FORMAT_MESSAGE_ALLOCATE_BUFFER makes buf an out-param for a pointer. */
    static const char text[] = "stub error";
    (void)src; (void)msgId; (void)langId; (void)size; (void)args;
    if (flags & FORMAT_MESSAGE_ALLOCATE_BUFFER) {
        char *p = (char *)malloc(sizeof text);
        memcpy(p, text, sizeof text);
        *(char **)buf = p;
    } else if (buf && size) {
        snprintf(buf, size, "%s", text);
    }
    return (DWORD)(sizeof text - 1);
}

/* ===================== ini (defaults) ===================== */

UINT GetPrivateProfileIntA(LPCSTR sec, LPCSTR key, int def, LPCSTR file)
{
    (void)sec; (void)key; (void)file;
    return (UINT)def;
}

DWORD GetPrivateProfileStringA(LPCSTR sec, LPCSTR key, LPCSTR def,
                               LPSTR buf, DWORD size, LPCSTR file)
{
    DWORD n;
    (void)sec; (void)key; (void)file;
    if (!buf || !size) return 0;
    if (!def) def = "";
    n = (DWORD)strlen(def);
    if (n > size - 1) n = size - 1;
    memcpy(buf, def, n);
    buf[n] = 0;
    return n;
}

BOOL WritePrivateProfileStringA(LPCSTR sec, LPCSTR key, LPCSTR val, LPCSTR file)
{
    (void)sec; (void)key; (void)val; (void)file;
    return TRUE;
}

/* ===================== dialogs / message boxes ===================== */

/* Never reached during a clean replay.  If one does fire it means the oracle
 * hit a path that would have prompted a human, so make it loud. */
int lt_stub_dialogs = 0;

int MessageBoxA(HWND h, LPCSTR text, LPCSTR cap, UINT type)
{
    (void)h;
    lt_stub_dialogs++;
    fprintf(stderr, "[stub] MessageBox: %s / %s\n", cap ? cap : "", text ? text : "");
    return (type & MB_YESNOCANCEL) ? IDCANCEL : IDOK;
}

intptr_t DialogBoxParamA(HINSTANCE inst, LPCSTR tmpl, HWND parent,
                         DLGPROC proc, LPARAM init)
{
    (void)inst; (void)parent; (void)proc; (void)init;
    lt_stub_dialogs++;
    fprintf(stderr, "[stub] DialogBox: %s\n", tmpl ? tmpl : "?");
    return IDCANCEL;
}

/* ===================== no-ops ===================== */

static char lt_dummy_obj[8];
#define DUMMY ((void *)lt_dummy_obj)

HDC     GetDC(HWND h)                                   { (void)h; return DUMMY; }
int     ReleaseDC(HWND h, HDC dc)                       { (void)h; (void)dc; return 1; }
HDC     CreateCompatibleDC(HDC dc)                      { (void)dc; return DUMMY; }
BOOL    DeleteDC(HDC dc)                                { (void)dc; return TRUE; }
HBITMAP CreateCompatibleBitmap(HDC dc, int w, int h)    { (void)dc; (void)w; (void)h; return DUMMY; }
HBITMAP LoadBitmapA(HINSTANCE i, LPCSTR n)              { (void)i; (void)n; return DUMMY; }
HGDIOBJ SelectObject(HDC dc, HGDIOBJ o)                 { (void)dc; (void)o; return DUMMY; }
BOOL    DeleteObject(HGDIOBJ o)                         { (void)o; return TRUE; }
HGDIOBJ GetStockObject(int i)                           { (void)i; return DUMMY; }
HBRUSH  CreateSolidBrush(DWORD c)                       { (void)c; return DUMMY; }
HPEN    CreatePen(int s, int w, DWORD c)                { (void)s; (void)w; (void)c; return DUMMY; }
BOOL    Rectangle(HDC dc, int l, int t, int r, int b)   { (void)dc; (void)l; (void)t; (void)r; (void)b; return TRUE; }
BOOL    MoveToEx(HDC dc, int x, int y, void *p)         { (void)dc; (void)x; (void)y; (void)p; return TRUE; }
BOOL    LineTo(HDC dc, int x, int y)                    { (void)dc; (void)x; (void)y; return TRUE; }
BOOL    TextOutA(HDC dc, int x, int y, LPCSTR s, int n) { (void)dc; (void)x; (void)y; (void)s; (void)n; return TRUE; }
DWORD   SetTextColor(HDC dc, DWORD c)                   { (void)dc; return c; }
DWORD   SetBkColor(HDC dc, DWORD c)                     { (void)dc; return c; }
int     SetBkMode(HDC dc, int m)                        { (void)dc; return m; }
UINT    SetTextAlign(HDC dc, UINT m)                    { (void)dc; return m; }
BOOL    EnableWindow(HWND h, BOOL e)                    { (void)h; (void)e; return TRUE; }
BOOL    EnableMenuItem(HMENU m, UINT i, UINT f)         { (void)m; (void)i; (void)f; return TRUE; }
DWORD   CheckMenuItem(HMENU m, UINT i, UINT f)          { (void)m; (void)i; (void)f; return 0; }
BOOL    InvalidateRect(HWND h, const void *r, BOOL e)   { (void)h; (void)r; (void)e; return TRUE; }
UINT    SetTimer(HWND h, UINT i, UINT e, void *p)       { (void)h; (void)e; (void)p; return i; }
BOOL    KillTimer(HWND h, UINT i)                       { (void)h; (void)i; return TRUE; }

BOOL BitBlt(HDC d, int x, int y, int w, int h, HDC s, int sx, int sy, DWORD rop)
{ (void)d; (void)x; (void)y; (void)w; (void)h; (void)s; (void)sx; (void)sy; (void)rop; return TRUE; }

BOOL StretchBlt(HDC d, int x, int y, int w, int h,
                HDC s, int sx, int sy, int sw, int sh, DWORD rop)
{ (void)d; (void)x; (void)y; (void)w; (void)h; (void)s;
  (void)sx; (void)sy; (void)sw; (void)sh; (void)rop; return TRUE; }

HBITMAP CreateDIBitmap(HDC dc, const BITMAPINFOHEADER *hdr, DWORD init,
                       const void *bits, const BITMAPINFO *info, UINT usage)
{ (void)dc; (void)hdr; (void)init; (void)bits; (void)info; (void)usage; return DUMMY; }

BOOL SetWindowPos(HWND h, HWND a, int x, int y, int cx, int cy, UINT f)
{ (void)h; (void)a; (void)x; (void)y; (void)cx; (void)cy; (void)f; return TRUE; }
