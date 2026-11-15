WWA 11.3 - Anchored Full-Text Captions (experimental)

Fixes:
- Caption windows can opt out of Windows capture using WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+). This reduces blinking; fallback still hides overlays on older Windows.
- Existing captions are matched to the same translated text by source column, then repositioned on every scan, even after large vertical scrolling. Duplicate identical dialogue lines no longer overwrite one another.
- Caption sizing no longer clips at 180 pixels; paragraphs that cannot fit alongside the source are omitted instead of being moved to an unrelated place.
- Captions are removed after two successful scans without matching text.

Limitations: OCR paragraph grouping and boundary detection remain heuristic. Long translations with no available gutter may be omitted. Windows-only, not compiled in this environment.
