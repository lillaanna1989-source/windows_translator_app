WWA 10.2 — GREY FOLLOWING CAPTIONS (EXPERIMENTAL SOURCE)

New: Windows OCR word bounding boxes are used to locate text lines on screen.
Each translated line appears in a grey, click-through caption next to its source.
The screen is rescanned every ~2.5 seconds, and caption positions are recomputed
so captions follow text after scrolling. Previously translated text is cached.
WWA overlays are hidden during capture to avoid OCR translation loops.

IMPORTANT: Not compiled or tested on Windows in this environment.
Requires Windows PowerShell 5.1, Windows OCR language packs, and internet.
Translation currently relies on an unofficial Google Translate endpoint, which
may block requests or change; errors appear in the Live Translation status line.
Windows display scaling can cause imperfect caption alignment.
The OCR engine may fail to recognize Spanish without the Spanish OCR pack.
Captions are per OCR line, not yet merged into paragraphs/bubbles.
On very busy screens only the first 24 detected lines are processed per scan.
Keep your previous installation backed up.
