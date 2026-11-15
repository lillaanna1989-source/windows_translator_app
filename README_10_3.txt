WWA 10.3 — Full-screen grey captions (experimental source)

Changes:
- Start LIVE button now starts full-screen OCR immediately, with no rectangle selection.
- Main window remains open so you can see OCR/translation status and errors.
- Captions choose the right or left side depending on available screen space.
- Caption positions are recalculated each scan to follow scrolling text.
- Caption coordinates convert OCR pixels to WPF display units for DPI scaling.

IMPORTANT: Not compiled or tested on Windows. Windows OCR language packs and
an internet connection are required. Online translation uses an unofficial
endpoint and may fail. If captions do not appear, inspect the status text
on the Live Translation page: OCR may not recognize the screen language,
or the translation endpoint may be blocked.

Extract, run INSTALL_WINDOWS_WRITING_ASSISTANT.bat, then open Live Translation
and press Start LIVE full-screen translation (or Start LIVE no rectangle).
