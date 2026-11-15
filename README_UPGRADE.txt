WWA 9.9.2 — SMARTREPLACE LOOP PROTECTION (SOURCE PREVIEW)

WWA 9.9 PREVIEW — SOURCE PACKAGE

This is a source-code preview, not a tested Windows binary.
Run INSTALL_WINDOWS_WRITING_ASSISTANT.bat on Windows with the .NET 8 SDK installed.

Changes:
- Removed the unsafe heuristic that guessed text fields from arbitrary keystrokes.
- Resets typing buffer when focus changes or typing has been idle for 2.5 seconds.
- Caches editable-field checks briefly to reduce keyboard-hook overhead.
- Rejects UI Automation password fields.
- Adds Ctrl+T+R shortcut: hold Ctrl+T and press R, then select a screen region.
- Translates recognized text into the configured language and shows a dismissible overlay.

Limitations:
- SmartReplace is SELECTED-AREA and one-shot, not full-screen continuous replacement.
- It draws a window over text; it does not alter other applications' content.
- Windows OCR language packs must be installed; online translation requires internet.
- Live audio transcription is NOT implemented in this package.
- A Windows build and hands-on regression test are required.
- This package contains a generated replacement icon because the original icon was not supplied.
- For safety, back up your previous installation before upgrading.

New in 9.9.2:
- Temporarily hides WWA overlays before screen selection/OCR to prevent translating its own translations.
- Caches translated text per target language and source text (up to 150 entries).
- Skips overlay if the translation equals the source text.
- Closes old overlays intersecting the newly selected region.
- Prevents overlapping SmartReplace capture requests.

IMPORTANT: This is not a continuously scanning translator. Language identification is approximate; a translation service can return changed text even for target-language input. No Windows compilation was performed here.
