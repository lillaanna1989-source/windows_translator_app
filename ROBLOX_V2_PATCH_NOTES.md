# Roblox typing-detection fix V2 (experimental)

Changes compared with the previous ZIP:
- Escape no longer silently turns compatibility mode off. It only clears the current captured message.
- Explicit compatibility mode takes precedence over unreliable text-field detection in the same window.
- The compatibility toggle now flashes a status indicator, so you can see that it was toggled.
- Switching to a different window still turns compatibility off to limit accidental collection.
- Replacement remains clipboard-only in compatibility mode; WWA will not attempt to inject text into Roblox.

How to test: Start WWA, enable Translate using Ctrl+Alt+1. Open Roblox, click chat, press Ctrl+Alt+Shift+T (once). Type at least 3 characters and pause for about one second. If a suggestion appears, Shift+Enter copies it. Clear original text and paste with Ctrl+V if Roblox permits. Esc should no longer disable compatibility mode. If the status popup or the shortcut does not fire, Roblox may not deliver the hotkey or its input may be inaccessible from the WWA hook.

This is an uncompiled source patch. It is not proven to work on Roblox. It does not bypass anti-cheat or restricted input handling. Avoid using compatibility mode on password fields.
