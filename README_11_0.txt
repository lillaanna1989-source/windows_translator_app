WWA 11.0 — Reliability and translucent grey subtitles

Changes:
- Grey semi-transparent caption text with a subtle shadow; no caption background.
- Short dialogue without punctuation can pass the filter (names/headings still excluded heuristically).
- The live feature uses TextAssistant.TranslateForOcrAsync, the same translator as Text to Translate.
- Language detection failure no longer blocks successful translations.
- Unchanged/failed translations are not cached, allowing automatic retry on the next scan.
- Up to 60 OCR lines per scan rather than 24.

Caveats: Screen OCR and online translation remain heuristic. No Windows build test was available.
