## Investigate and fix: editor scroll position bleeds across tabs on tab switch

This is an open investigation. Five rounds of fixes have all failed to resolve it.
Read the history, instrument the code, find the actual root cause, and fix it.

---

## Symptom

When the user scrolls Tab 1's editor to line 200, then switches to Tab 2, Tab 2's
editor is also at line 200. Switching back to Tab 1 shows it at line 200 correctly.
Every tab appears at whatever scroll position the previously-viewed tab was at.

---

## What has already been tried (do not repeat these)

1. Per-tab `SavedEditorScrollY` and `SavedCaretLine` fields on `TabViewModel`
2. `_previousActiveTab` field to correctly identify the outgoing tab before
   `ActiveTab` is updated
3. Deferred restore via `DispatcherPriority.Loaded` post to run after AvaloniaEdit's
   `BringCaretToView` layout pass
4. `SuppressMakeVisible` flag on `CenteringTextView` to block internal scroll
   correction during document replacement
5. Setting `_isSyncingScroll = true` before `Document.Text` assignment to block
   `OnEditorScrollChanged` from firing during the load

All builds succeeded, all were confirmed applied correctly, none fixed the issue.

---

## Investigation steps — do these before writing any fix

### Step 1 — Verify the save is capturing the right value

Add temporary `Debug.WriteLine` output in `SwitchActiveTab` to confirm what is
being saved and what is being restored:

```csharp
// In step 0 (save outgoing):
System.Diagnostics.Debug.WriteLine(
    $"[TAB SAVE] outgoing='{outgoing.DisplayName}' " +
    $"editorScrollY={_editor?.TextArea.TextView.ScrollOffset.Y:F1} " +
    $"caretLine={_editor?.TextArea.Caret.Line}");

// In step 4b (restore incoming), inside the Loaded post, before ScrollToVerticalOffset:
System.Diagnostics.Debug.WriteLine(
    $"[TAB RESTORE] newTab='{newTab.DisplayName}' " +
    $"targetScrollY={targetScrollY:F1} " +
    $"currentScrollY={_editor?.TextArea.TextView.ScrollOffset.Y:F1}");

// After ScrollToVerticalOffset, still inside the Loaded post:
System.Diagnostics.Debug.WriteLine(
    $"[TAB RESTORE AFTER] scrollY={_editor?.TextArea.TextView.ScrollOffset.Y:F1}");
```

Run the app. Switch tabs. Check the Output window. Report what the debug lines show.

### Step 2 — Check if ScrollToVerticalOffset is having any effect at all

If `[TAB RESTORE AFTER]` shows the same value as `[TAB RESTORE]` (the call had no
effect), then `ScrollToVerticalOffset` is being immediately overridden by something
after the Loaded post runs.

If the values differ but the editor still shows the wrong position visually, the
issue is a rendering/layout pass running after our post.

### Step 3 — Try using the reflection path directly

`CenteringTextView` already uses `SetScrollOffset` via reflection for typewriter mode
because it bypasses AvaloniaEdit's public API restrictions. Try the same approach for
the restore:

```csharp
// Inside the Loaded post, instead of _editor.ScrollToVerticalOffset(targetScrollY):
var textView = _editor.TextArea.TextView;
var scrollable = (Avalonia.Controls.Primitives.IScrollable)textView;
var currentOffset = scrollable.Offset;
var newOffset = new Avalonia.Vector(currentOffset.X, targetScrollY);

var setScrollOffset = typeof(AvaloniaEdit.Rendering.TextView)
    .GetMethod("SetScrollOffset",
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Instance);

if (setScrollOffset is not null)
    setScrollOffset.Invoke(textView, new object[] { newOffset });
else
    scrollable.Offset = newOffset;
```

### Step 4 — Try a later dispatcher priority

If the Loaded post is still being overridden, try `DispatcherPriority.ContextIdle`
which runs after all pending layout and render passes:

```csharp
Dispatcher.UIThread.Post(() => { ... }, DispatcherPriority.ContextIdle);
```

### Step 5 — Check for cascading scroll messages from WebView

The preview fires scroll messages back to the host via `OnWebViewMessage`. After
`NavigateWebView` is called in step 6, WebView2 loads the page and fires a scroll
event with `anchorLine`. `HandleScrollSync` then calls `_editor.ScrollToVerticalOffset`
— potentially overriding our restore.

Check whether `HandleScrollSync` is running AFTER our restore completes by adding:

```csharp
// In HandleScrollSync, anchor-based path:
System.Diagnostics.Debug.WriteLine(
    $"[SCROLL SYNC] anchorLine={anchorLine} _isSyncingScroll={_isSyncingScroll}");
```

If `_isSyncingScroll` is `false` when this fires after a tab switch, the WebView
scroll message is overriding our restore.

---

## Fix based on findings

Once you identify the actual cause from the debug output, implement the fix.
The goal is simple: after a tab switch, the editor stays at the incoming tab's
saved scroll position.

If the WebView scroll message is the culprit, the fix is to stamp
`_lastEditorSyncTime = DateTime.UtcNow` inside `SwitchActiveTab` after step 5 so
that the 300ms cooldown in `HandleScrollSync` suppresses the post-navigation
scroll event.

If `ScrollToVerticalOffset` has no effect, use the reflection path from Step 3.

If a later layout pass is overriding our post, use `ContextIdle` priority.

Remove all `Debug.WriteLine` statements before submitting the final fix.

---

## Acceptance criteria
- [ ] Scrolling tab 1 to line 200, switching to tab 2, switching back restores
      tab 1 to line 200
- [ ] New tabs open at the top
- [ ] Preview scroll is preserved independently per tab
- [ ] Normal scroll sync within a tab is unaffected
- [ ] App builds without warnings

## Reporting
1. What did the debug output reveal as the actual root cause?
2. Which step in the investigation identified it?
3. What was the fix and why does it work?
4. Every file changed with before/after