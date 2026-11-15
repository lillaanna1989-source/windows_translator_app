WWA 10.9 — Context-aware caption experiment

Changes:
- Attempts to exclude OCR text contained in images exposed through Windows UI Automation.
- Reduces caption width and avoids caption-to-caption overlap.
- Reports how many lines were excluded as image text.
- Retains text-only captions and the existing translation engine.

LIMITATIONS:
- UI Automation does not reliably expose images inside all browsers, canvases, videos, screenshots, or apps. Therefore embedded text in such images may still be translated. A reliable browser solution requires DOM integration, such as a browser extension.
- The program has not been compiled or tested on Windows in this environment.
- Existing OCR and translation limitations remain.
