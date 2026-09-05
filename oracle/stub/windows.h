/* ------------------------------------------------------------------------
 * LaserTank headless oracle -- minimal Win32 substitute.
 *
 * This header shadows the real <windows.h> so that LTANK2.C (and the tick
 * loop lifted from LTANK.C) compile and link with no GUI, no GDI and no
 * Windows dependency at all.
 *
 * RULE: nothing in here may change game logic.  Everything is either a
 * faithful type/constant, a no-op, or a real implementation (memory, files,
 * message dispatch).  The logic-carrying side effects live inside LTANK2.C
 * itself and are compiled verbatim -- notably UpDateLaserBounce(), a paint
 * function that sets LaserBounceOnIce and thereby alters MoveLaser()'s
 * control flow.  Because we stub only the *API*, that quirk survives intact.
 * ---------------------------------------------------------------------- */
#ifndef LT_STUB_WINDOWS_H
#define LT_STUB_WINDOWS_H

#include <stddef.h>
#include <stdint.h>

/* ---- basic types ---- */
typedef int                 BOOL;
typedef unsigned char       BYTE;
typedef unsigned short      WORD;
typedef uint32_t            DWORD;
typedef int32_t             LONG;
typedef unsigned int        UINT;
typedef char                TCHAR;
typedef char               *LPSTR, *LPTSTR;
typedef const char         *LPCSTR, *LPCTSTR;
typedef void               *LPVOID;
typedef uintptr_t           WPARAM;
typedef intptr_t            LPARAM;
typedef intptr_t            LRESULT;

typedef void *HANDLE;
typedef void *HWND;
typedef void *HDC;
typedef void *HBRUSH;
typedef void *HPEN;
typedef void *HBITMAP;
typedef void *HMENU;
typedef void *HFONT;
typedef void *HGDIOBJ;
typedef void *HGLOBAL;
typedef void *HINSTANCE;
typedef void *HLOCAL;

#define WINAPI
#define CALLBACK
#define APIENTRY

typedef LRESULT (CALLBACK *DLGPROC)(HWND, UINT, WPARAM, LPARAM);
typedef LRESULT (CALLBACK *WNDPROC)(HWND, UINT, WPARAM, LPARAM);

#ifndef NULL
#define NULL ((void*)0)
#endif
#define TRUE  1
#define FALSE 0
#define MAX_PATH 260

/* ---- bitmap structures (referenced by graphics loaders we never call) ---- */
#pragma pack(push,2)
typedef struct tagBITMAPFILEHEADER {
    WORD  bfType;
    DWORD bfSize;
    WORD  bfReserved1;
    WORD  bfReserved2;
    DWORD bfOffBits;
} BITMAPFILEHEADER, *LPBITMAPFILEHEADER, *PBITMAPFILEHEADER;
#pragma pack(pop)

typedef struct tagBITMAPINFOHEADER {
    DWORD biSize;
    LONG  biWidth;
    LONG  biHeight;
    WORD  biPlanes;
    WORD  biBitCount;
    DWORD biCompression;
    DWORD biSizeImage;
    LONG  biXPelsPerMeter;
    LONG  biYPelsPerMeter;
    DWORD biClrUsed;
    DWORD biClrImportant;
} BITMAPINFOHEADER, *LPBITMAPINFOHEADER, *PBITMAPINFOHEADER;

typedef struct tagRGBQUAD { BYTE rgbBlue, rgbGreen, rgbRed, rgbReserved; } RGBQUAD;

typedef struct tagBITMAPINFO {
    BITMAPINFOHEADER bmiHeader;
    RGBQUAD          bmiColors[1];
} BITMAPINFO, *LPBITMAPINFO, *PBITMAPINFO;

/* ---- constants ---- */
#define RGB(r,g,b) ((DWORD)(((BYTE)(r))|(((WORD)((BYTE)(g)))<<8)|((DWORD)((BYTE)(b))<<16)))
#define MAKELANGID(p,s) ((WORD)((((WORD)(s))<<10)|(WORD)(p)))
#define LANG_NEUTRAL     0x00
#define SUBLANG_DEFAULT  0x01

#define WM_USER      0x0400
#define WM_COMMAND   0x0111
#define WM_SETTEXT   0x000C
#define WM_TIMER     0x0113
#define WM_KEYDOWN   0x0100

#define VK_SPACE 0x20
#define VK_LEFT  0x25
#define VK_UP    0x26
#define VK_RIGHT 0x27
#define VK_DOWN  0x28

/* GDI raster ops / stock objects */
#define SRCCOPY   0x00CC0020UL
#define SRCPAINT  0x00EE0086UL
#define SRCAND    0x008800C6UL
#define NULL_PEN     8
#define PS_SOLID     0
#define OPAQUE       2
#define TRANSPARENT  1
#define TA_LEFT      0
#define TA_CENTER    6
#define DIB_RGB_COLORS 0
#define CBM_INIT     0x04

/* memory */
#define GMEM_FIXED    0x0000
#define GMEM_MOVEABLE 0x0002

/* files */
#define GENERIC_READ  0x80000000UL
#define GENERIC_WRITE 0x40000000UL
#define FILE_SHARE_READ  0x00000001UL
#define FILE_SHARE_WRITE 0x00000002UL
#define CREATE_ALWAYS 2
#define OPEN_EXISTING 3
#define OPEN_ALWAYS   4
#define FILE_FLAG_SEQUENTIAL_SCAN 0x08000000UL
#define FILE_FLAG_RANDOM_ACCESS   0x10000000UL
#define FILE_BEGIN   0
#define FILE_CURRENT 1
#define FILE_END     2
#define INVALID_HANDLE_VALUE ((HANDLE)(intptr_t)-1)
#define INVALID_SET_FILE_POINTER ((DWORD)-1)

/* message boxes / dialogs */
#define MB_OK                0x0000
#define MB_YESNO             0x0004
#define MB_YESNOCANCEL       0x0003
#define MB_ICONERROR         0x0010
#define MB_ICONQUESTION      0x0020
#define MB_ICONINFORMATION   0x0040
#define IDOK     1
#define IDCANCEL 2
#define IDYES    6
#define IDNO     7

/* menus / windows */
#define MF_BYCOMMAND 0x0000
#define MF_CHECKED   0x0008
#define MF_UNCHECKED 0x0000
#define MF_ENABLED   0x0000
#define MF_GRAYED    0x0001
#define SW_SHOWNA    8
#define SWP_NOSIZE   0x0001
#define SWP_NOMOVE   0x0002
#define SWP_NOZORDER 0x0004

#define FORMAT_MESSAGE_ALLOCATE_BUFFER 0x00000100UL
#define FORMAT_MESSAGE_FROM_SYSTEM     0x00001000UL

/* ---- API surface referenced by LTANK2.C; see oracle/win32_stub.c ---- */

/* memory -- real */
HGLOBAL GlobalAlloc(UINT flags, size_t bytes);
HGLOBAL GlobalReAlloc(HGLOBAL p, size_t bytes, UINT flags);
HGLOBAL GlobalFree(HGLOBAL p);
HLOCAL  LocalFree(HLOCAL p);

/* files -- real */
HANDLE CreateFileA(LPCSTR name, DWORD access, DWORD share, void *sa,
                   DWORD disp, DWORD flags, HANDLE tmpl);
BOOL   ReadFile(HANDLE h, void *buf, DWORD n, DWORD *got, void *ov);
BOOL   WriteFile(HANDLE h, const void *buf, DWORD n, DWORD *put, void *ov);
DWORD  SetFilePointer(HANDLE h, LONG dist, LONG *distHigh, DWORD method);
BOOL   CloseHandle(HANDLE h);
DWORD  GetCurrentDirectoryA(DWORD n, LPSTR buf);
BOOL   SetCurrentDirectoryA(LPCSTR dir);
DWORD  lt_GetLastError(void);
#define GetLastError lt_GetLastError
DWORD  FormatMessageA(DWORD flags, const void *src, DWORD msgId, DWORD langId,
                      LPSTR buf, DWORD size, void *args);

/* messages -- real dispatch into LT_WndProc (oracle/driver.c) */
LRESULT SendMessageA(HWND h, UINT msg, WPARAM wp, LPARAM lp);
BOOL    PostMessageA(HWND h, UINT msg, WPARAM wp, LPARAM lp);

/* ini -- hands back the supplied defaults */
UINT  GetPrivateProfileIntA(LPCSTR sec, LPCSTR key, int def, LPCSTR file);
DWORD GetPrivateProfileStringA(LPCSTR sec, LPCSTR key, LPCSTR def,
                               LPSTR buf, DWORD size, LPCSTR file);
BOOL  WritePrivateProfileStringA(LPCSTR sec, LPCSTR key, LPCSTR val, LPCSTR file);

/* everything below is a no-op */
HDC     GetDC(HWND h);
int     ReleaseDC(HWND h, HDC dc);
HDC     CreateCompatibleDC(HDC dc);
BOOL    DeleteDC(HDC dc);
HBITMAP CreateCompatibleBitmap(HDC dc, int w, int h);
HBITMAP CreateDIBitmap(HDC dc, const BITMAPINFOHEADER *hdr, DWORD init,
                       const void *bits, const BITMAPINFO *info, UINT usage);
HBITMAP LoadBitmapA(HINSTANCE inst, LPCSTR name);
HGDIOBJ SelectObject(HDC dc, HGDIOBJ obj);
BOOL    DeleteObject(HGDIOBJ obj);
HGDIOBJ GetStockObject(int i);
HBRUSH  CreateSolidBrush(DWORD color);
HPEN    CreatePen(int style, int width, DWORD color);
BOOL    BitBlt(HDC d, int x, int y, int w, int h, HDC s, int sx, int sy, DWORD rop);
BOOL    StretchBlt(HDC d, int x, int y, int w, int h,
                   HDC s, int sx, int sy, int sw, int sh, DWORD rop);
BOOL    Rectangle(HDC dc, int l, int t, int r, int b);
BOOL    MoveToEx(HDC dc, int x, int y, void *pt);
BOOL    LineTo(HDC dc, int x, int y);
BOOL    TextOutA(HDC dc, int x, int y, LPCSTR s, int n);
DWORD   SetTextColor(HDC dc, DWORD c);
DWORD   SetBkColor(HDC dc, DWORD c);
int     SetBkMode(HDC dc, int mode);
UINT    SetTextAlign(HDC dc, UINT mode);
int     MessageBoxA(HWND h, LPCSTR text, LPCSTR cap, UINT type);
intptr_t DialogBoxParamA(HINSTANCE inst, LPCSTR tmpl, HWND parent,
                         DLGPROC proc, LPARAM init);
BOOL    EnableWindow(HWND h, BOOL enable);
BOOL    EnableMenuItem(HMENU m, UINT id, UINT flags);
DWORD   CheckMenuItem(HMENU m, UINT id, UINT flags);
BOOL    InvalidateRect(HWND h, const void *rect, BOOL erase);
BOOL    SetWindowPos(HWND h, HWND after, int x, int y, int cx, int cy, UINT flags);
UINT    SetTimer(HWND h, UINT id, UINT elapse, void *proc);
BOOL    KillTimer(HWND h, UINT id);

/* ANSI aliases, exactly as the real header does them */
#define MessageBox      MessageBoxA
#define DialogBoxParam  DialogBoxParamA
#define DialogBox(i,t,p,f) DialogBoxParamA(i,t,p,f,0)
#define SendMessage     SendMessageA
#define PostMessage     PostMessageA
#define CreateFile      CreateFileA
#define TextOut         TextOutA
#define GetPrivateProfileInt      GetPrivateProfileIntA
#define GetPrivateProfileString   GetPrivateProfileStringA
#define WritePrivateProfileString WritePrivateProfileStringA
#define GetCurrentDirectory       GetCurrentDirectoryA
#define SetCurrentDirectory       SetCurrentDirectoryA
#define FormatMessage             FormatMessageA
#define LoadBitmap                LoadBitmapA

#endif /* LT_STUB_WINDOWS_H */
