# SPEC.md — Targeted Changes for v3.0

Apply these changes to `docs/SPEC.md` in the repository.

---

## 1. Update version header (line 5)

**Before:**
```
> Version: 2.9 — BL-24 through BL-29 completed. All planned backlog items complete.
```

**After:**
```
> Version: 3.0 — BL-04/13 tab-based multi-file editing complete. v1.1.0 shipped.
```

---

## 2. Update Project Identity table

**Before:**
```
| Version target | v1.0.0 |
| Repository | TBD — create on GitHub before Phase 1 |
```

**After:**
```
| Version target | v1.1.0 |
| Repository | https://github.com/michaeldhead/MarkDown-CrossPlatform |
```

---

## 3. Update backlog table

Find the Backlog Summary table. Update these rows:

| ID | Change |
|---|---|
| BL-04/13 | `📋 Planned — major architecture, needs planning session before any code` → `✅ Complete` |

---

## 4. Update Layout Architecture diagram and titlebar description

Find the ASCII layout diagram. Update the titlebar row comment:

**Before:**
```
│  Titlebar (38px)                                            │
```

**After:**
```
│  Titlebar (38px) — App name | Tab strip (scrollable) | Controls  │
```

Find the Icon Rail section. After the existing bullet points, add a new section:

### Tab Strip (in titlebar, always visible)
- Horizontally scrollable strip between the app name label and the view mode buttons.
- Each tab: filename label + dirty dot (blue = unsaved) + close button (×).
- Active tab has a 3px accent-color underline.
- New tab (+) button at the right end of the strip.
- Keyboard: Ctrl+T (new tab), Ctrl+W (close active), Ctrl+Tab (next tab).
- Middle-click on a tab closes it.
- Closing a tab with unsaved changes shows a Save / Don't Save / Cancel dialog.

---

## 5. Update F-03 Split View description

Find the F-03 section. Add after the existing bullet points:

- Each tab maintains its own independent editor scroll position, preview scroll
  position, caret line, and active block highlight. Switching tabs restores each
  tab's last position.

---

## 6. Add singleton / IPC note to Architecture Notes section

After the existing architecture notes (or after the Threading Model section), add:

### Singleton Enforcement and IPC (BL-04 T4)

The app enforces a single running instance on Windows via a named mutex
(`Global\GHSMarkdownEditor`). A second launch sends file paths to the running
instance over a named pipe (`\\.\pipe\GHSMarkdownEditor`) then exits. The running
instance opens each received path in a new tab.

On macOS, singleton enforcement is deferred to BL-11 (NSRunningApplication +
Apple Events).

---

## 7. Update AppSettings note

Find any existing note about `AppSettings` fields. Add:

```
- OpenTabPaths (List<string?>, default empty) — paths of open tabs; null = untitled
- ActiveTabIndex (int, default 0) — index of the active tab to restore
- MaxRestoredTabs (int, default 10) — cap on tabs restored at launch
- RightPanelOpenWidth (double, default 200.0) — persisted drag width of right panel
- LeftPanelOpen default: false — left panel closed on first run
```

---

## 8. Update TabViewModel note in Services/ViewModels section

Add a new entry:

**TabViewModel**
- `EditorViewModel` — per-tab, owns document text and caret state
- `PreviewViewModel` — per-tab, owns rendered HTML
- `FileService` — per-tab, owns file path, dirty state, and draft
- `MarkdownParsingService` — per-tab, owns debounced parse pipeline
- `SourceMappingService` — per-tab, owns line-to-selector mapping
- `DisplayName` (computed) — filename without extension, or "Untitled"
- `IsDirty` (computed) — true when document has unsaved changes
- `IsActive` — true when this tab is the active tab
- Per-tab scroll state: `SavedScrollY`, `SavedAnchorLine`, `SavedEditorScrollY`,
  `SavedCaretLine` — saved on tab deactivation, restored on activation
- Two constructors: fresh-services (new tabs) and wrap-existing (initial tab)

---

## 9. Update version footer (last 3 lines of file)

**Before:**
```
*Version: 2.9*
*Phase 1 through Phase 9 — ✅ COMPLETE. Phase 10 — not started.*
*Last updated: BL-24 through BL-29 complete. All planned backlog items resolved.*
```

**After:**
```
*Version: 3.0*
*Phase 1 through Phase 9 — ✅ COMPLETE. Phase 10 — not started.*
*Last updated: BL-04/13 tab-based multi-file editing complete. v1.1.0 shipped.*
```