# Repeated translation fix (experimental)

Issue reported: WWA translates one Roblox/custom-chat message and then stops translating subsequent messages.

Changes:
- Compatibility-mode typing uses the stable opted-in top-level window ID rather than a potentially recreated Windows UI Automation element ID.
- After Shift+Enter copies a suggestion, compatibility mode stays armed for the next message and only sentence state is cleared.
- The idle timeout is raised to 30 seconds for compatibility mode and 15 seconds for standard fields to avoid losing a sentence on a brief pause.

Usage: enable Translate (Ctrl+Alt+1), focus Roblox chat, toggle Ctrl+Alt+Shift+T, type, Shift+Enter to copy, clear the old message and paste manually when supported. Type a second message without toggling compatibility mode again.

Limits: switches to other top-level windows still disable compatibility for privacy. Roblox may block clipboard paste. No guarantee of automatic translation in every game. Tested by source inspection only (no Windows .NET runtime here).

This change targets the repeat-translation state reset. It does not add universal keyboard monitoring or bypass Roblox's input protections.
