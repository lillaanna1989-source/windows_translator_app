# Translation replacement patch

Changes in `WindowsWritingAssistantService.cs`:
- Waits for physical Shift release after the Shift+Enter acceptance shortcut, preventing accidental Shift+Backspace or Ctrl+Shift+V.
- Rejects replacement when Shift/Ctrl/Alt are still held, the target text field changes, or the assistant is disabled.
- Suppresses processing of synthetic keyboard events during replacement.
- Uses Win32 SendInput rather than deprecated keybd_event for Backspace and paste; checks Backspace injection results.
- Adds brief delays to let clipboard-driven apps process changes.

Known limitations:
- No build/runtime verification (Windows/.NET SDK unavailable here).
- Some apps block injected input, and some controlled fields can discard pasted text after the framework rerenders.
- Source text tracking is based on keyboard events, not a verified text-range snapshot. A different insertion point or mouse edit can make the tracked sentence inaccurate.
- Clipboard contents are overwritten on accept, as with the prior implementation.
- The focused-field detection intentionally fails closed for inputs that cannot be positively identified, so translation suggestions may not appear there.
