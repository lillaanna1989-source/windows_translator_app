WWA 10.0 LIVE BETA — SOURCE CODE (NOT WINDOWS-BUILD-TESTED)

Install: Extract, then run INSTALL_WINDOWS_WRITING_ASSISTANT.bat on Windows with .NET 8 SDK.
Keep a backup of your existing working WWA.

LIVE SCREEN: Open Live Translation > Select region & start. Select Talkie dialogue area.
Every ~3 seconds WWA captures the chosen region and runs Windows OCR. It only translates
when recognized text changes, caches translations, and hides its own overlay during capture.
Ctrl+T+R attempts to toggle region selection; browsers may intercept Ctrl+T.
Stop from Live Translation page. This is one region, not automatic per-bubble positioning.
Overlay is click-through and cannot be closed by clicking; use Stop.

LIVE MICROPHONE: Start microphone uses Windows PowerShell System.Speech dictation engine.
Requires a working default microphone and an installed compatible Windows speech recognizer.
This is MICROPHONE transcription only, not system audio and not translated subtitles.
No recording is saved by WWA; transcript remains visible in the UI during the session.

LIMITATIONS: OCR language depends on installed Windows OCR packs; translation uses the
existing translation service and may require internet. Already-target-language detection
is approximate (unchanged results skipped). Screens with private data should not be selected.
Screen coordinates may need DPI adjustments on mixed-scale displays.
This package has NOT been compiled on Windows; report compiler errors if any.
