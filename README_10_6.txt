WWA 10.6 — Screen To Translate

Renamed the on-screen translation feature from SmartReplace to Screen To Translate.
The captions now obtain translated text through TextAssistant.TranslateForOcrAsync, the same translation method used by the existing Text to Translate mode.
The live engine still attempts to detect the source language separately to respect known-language settings. If detection is unavailable, it falls back to the shared translator and marks the source unknown.
Note: the shared translator can return original text on network failure; the live status now explains when it receives unchanged text after a failed source-detection request.

This is source code; Windows build/runtime verification is still required.
