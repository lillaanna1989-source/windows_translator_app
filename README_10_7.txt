WWA 10.7 — Screen To Translate: dialogue-only filter

Changes:
- Filters OCR lines before translation requests and before creating grey captions.
- Skips single-word names, usernames/handles, common interface controls,
  short unpunctuated labels, and short title-cased headings.
- Keeps sentence-like dialogue and prose, including short punctuated phrases.
- Status reports number of ignored names/labels.

IMPORTANT: This is a heuristic filter. It cannot reliably tell every name or
UI element from dialogue, and OCR may split sentences into multiple lines.
Future improvement: group adjacent OCR lines into paragraphs and use app-aware
text-region detection. Existing Windows OCR and translation limitations remain.

No Windows build/test performed in this environment.
