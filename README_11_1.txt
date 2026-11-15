WWA 11.1 — stable paragraph captions

Fixes: joins adjacent wrapped OCR lines into longer messages; keeps captions visible between scans; updates existing overlay positions/text instead of recreating them; keeps last captions on temporary OCR failure.

Limitations: heuristic grouping can still split or join text incorrectly; screenshot/image filtering remains best-effort; no Windows compile/runtime validation performed.
