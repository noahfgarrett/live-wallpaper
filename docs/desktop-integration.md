# Desktop integration

How Live Wallpaper puts a window behind the desktop icons, and what can go wrong. None of the desktop window structure is documented by Microsoft as a supported API: it is Explorer internals, it changed in Windows 11 24H2, and it may change again. Everything here was researched from public sources in November 2026 (listed at the end) and still has to be confirmed on real hardware. Run `LiveWallpaper.Engine.exe --dump-desktop desktop.json` to capture the tree on a given machine.

## Two layouts

### Legacy (Windows 10, Windows 11 up to 23H2)

After Explorer is asked to split the desktop (see below), the top-level windows include:

```
WorkerW                 top-level, hosts the icons
└── SHELLDLL_DefView
    └── SysListView32   the icons
WorkerW                 top-level, directly behind the one above, empty: the wallpaper layer
Progman                 top-level, "Program Manager"
```

The wallpaper window becomes a `WS_CHILD` of the **empty WorkerW**, the next top-level `WorkerW` after the window that hosts `SHELLDLL_DefView`. Before the split, `SHELLDLL_DefView` is a child of `Progman` and there is no empty `WorkerW`.

### Raised (Windows 11 24H2 and later)

```
Progman                 top-level, created with WS_EX_NOREDIRECTIONBITMAP
├── SHELLDLL_DefView    WS_EX_LAYERED, the icons (topmost child)
│   └── SysListView32
├── <our window>        inserted here
└── WorkerW             Explorer's wallpaper layer (bottom child)
```

The wallpaper window becomes a child of **Progman**, z-ordered directly below `SHELLDLL_DefView` and above Explorer's `WorkerW`.

- Because `Progman` has no redirection surface, a plain child is not composited. Sources disagree, but the ones that verified it make the child **layered** with full opacity: `WS_EX_LAYERED` plus `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)`.
- The layering is applied **before** `SetParent`.
- Layered child windows require an application manifest that declares Windows 8 or later in `supportedOS`. The engine's manifest declares Windows 10/11.

## Splitting the desktop

Send the undocumented message `0x052C` to `Progman`:
1. `SendMessageTimeout(progman, 0x052C, 0xD, 0x1, SMTO_NORMAL, 1000)`.
2. Re-read the tree. If it is still not split, send once more with `(0, 0)`.

Explorer reference-counts the split, so the engine does not send variants repeatedly. One project reported that doing so undid the layer.

## Classification (`DesktopLayoutClassifier`)

The rules are applied in this order:
1. **No top-level `Progman`:** unsupported.
2. **`Progman` has a direct `SHELLDLL_DefView` child and a direct `WorkerW` child:** raised layout, attach to Progman. If there are several `WorkerW` children, take the one with the largest area, so tiny helper windows are ignored. This rule wins over any stale legacy `WorkerW`.
3. **`Progman` hosts `SHELLDLL_DefView` but has no `WorkerW` child:** not split, so unsupported. The engine retries the split once before giving up.
4. **A top-level window hosts `SHELLDLL_DefView`, and a top-level `WorkerW` sits after it in z-order:** legacy layout, attach to that `WorkerW`.
5. **Anything else:** unsupported.

`Progman`'s `WS_EX_NOREDIRECTIONBITMAP` ex-style is recorded in the snapshot as a secondary signal for diagnostics, but it is not required by the rules.

## Attaching

Steps for both layouts:
1. Create the window hidden as `WS_POPUP` with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, so it never appears in Alt+Tab or the taskbar and never takes focus.
2. Swap `WS_POPUP` for `WS_CHILD` (`SetWindowLongPtr(GWL_STYLE)`).
3. Raised layout only: add `WS_EX_LAYERED | WS_EX_TRANSPARENT`, then call `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)`.
   - `WS_EX_TRANSPARENT` makes the window click-through. On 24H2 `SHELLDLL_DefView` is itself layered and may pass clicks through where it's transparent, and without this flag those clicks would land on our window instead of the desktop.
4. `SetParent(hwnd, parent)`, then check that `GetParent(hwnd) == parent`.
5. `SetWindowPos` to the monitor's rectangle in the parent's client coordinates: monitor bounds minus the parent client origin (`ClientToScreen(parent, {0,0})`), using `SWP_NOACTIVATE`.
   - Raised layout: `hWndInsertAfter = SHELLDLL_DefView`, which places us directly below it.
   - Legacy layout: `SWP_NOZORDER`.
6. Check that `SetWindowPos` succeeded, then re-run the attachment check:
   - correct parent;
   - the window rectangle equals the monitor;
   - on the raised layout, the window sits below `SHELLDLL_DefView` and above every `WorkerW` sibling.

   If any check fails, the window is destroyed instead of shown. `SetParent` initially links the child at the top of Progman's children, above the icons, so showing it unchecked could cover them.
7. `ShowWindow(SW_SHOWNA)`.

All image decoding happens before any window is parented to Explorer. A cross-process parent/child pair shares input queues, so a slow decode while attached could stall desktop input.

The two layouts also use different swap chains:
- Legacy: a DXGI flip-model swap chain (`FLIP_DISCARD`).
- Raised: a bit-block-transfer swap chain (`DISCARD`), because Microsoft's guidance for layered children speaks of "DX blt presents" and no source confirms that flip-model works on a layered child. **Unverified. Milestone 1's Windows test settles this.**

## Keeping it attached

- **Explorer restart** (`TaskbarCreated` broadcast): our child windows are destroyed with Explorer's windows, so the engine tears down and attaches again.
- **Display change** (`WM_DISPLAYCHANGE`, `WM_DPICHANGED`, work-area change): rebuild.
- **Explorer re-ordering or moving:** theme changes, wallpaper transitions and virtual-desktop switches have been reported to recreate or raise Explorer's `WorkerW` above ours, and display changes can move the parent after we've positioned the windows.
  - A 5-second timer re-runs the attachment check. On the raised layout, the walk stops at any `WorkerW`, not just the one recorded earlier.
  - If a window is lost, the engine rebuilds.
  - If attaching itself fails, it retries with growing gaps (5 s up to 60 s).
  - If there's simply nothing to show (missing images, monitors not connected), it waits for a reload or display change instead of retrying.
- **Splitting:** `0x052C` is only sent when the desktop is actually unsplit, never on retries against an already split or half-started desktop.
- **Enumeration:** windows are enumerated with `EnumWindows` / `EnumChildWindows`, not `GetWindow` loops, which can loop forever while windows are being destroyed (for example during an Explorer restart).

## Restoring the desktop

This only happens when the engine actually split the desktop or attached a window.
1. On exit, the engine destroys its windows.
2. It asks `Progman` and every top-level `WorkerW` to repaint (`RedrawWindow` with invalidate, erase, all children and update now). This changes nothing.
3. Only when `IDesktopWallpaper::GetStatus` reports that no slideshow is running does it also re-apply the current wallpaper path, with `SystemParametersInfo(SPI_SETDESKWALLPAPER, …, 0)` and no `SPIF_UPDATEINIFILE`. If the status can't be read, it skips this step rather than risk turning a slideshow into a fixed picture.

On a crash, the handler tears down and restores the same way, but leaves the marker file in place, so the next start restores again before attaching.

## Known limitations

- Windows Spotlight isn't reported as a slideshow. Re-applying its current image could switch the background type to "Picture" for the session. This is unverified, and covered by checklist item 15.

- Everything above relies on undocumented Explorer behaviour.
- Some early 24H2 builds reportedly hid live wallpapers unless "Show desktop icons" was enabled.
- When Windows' own slideshow or HDR is active, `SHELLDLL_DefView` may not be opaque, which could affect how the icon layer blends.
- If neither layout is detected, the engine refuses to attach, shows a message, and never places a window above the icons.

## Sources

- [lively-community/lively#2074](https://github.com/lively-community/lively/issues/2074): 24H2 discussion, including relayed Microsoft guidance on the layered child, `WS_EX_NOREDIRECTIONBITMAP` and z-order. Read for understanding only; no Lively code is used.
- [youllook/VibingWrapper#7](https://github.com/youllook/VibingWrapper/pull/7): raised-layout attach sequence and the `0x052C (0xD, 1)` / `(0, 0)` fallback.
- [p4yy/Intonaco#5](https://github.com/p4yy/Intonaco/pull/5): classification by tree, preferring the raised layout, filtering tiny WorkerW windows, and a periodic z-order self-check.
- [UnhingedSoftware/kirie#11](https://github.com/UnhingedSoftware/kirie/pull/11): unsplit raised state, layered holder, manifest requirement.
- [UYSAUIYO/Desktop-Sticker#2](https://github.com/UYSAUIYO/Desktop-Sticker/pull/2): Progman-owned WorkerW variant.
- Microsoft Learn: [Window Features — Layered Windows](https://learn.microsoft.com/windows/win32/winmsg/window-features#layered-windows), [SetParent](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setparent), [SetWindowPos](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos), [SendMessageTimeoutW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw).
