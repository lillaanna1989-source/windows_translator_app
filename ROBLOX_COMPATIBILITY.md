# WWA custom-chat compatibility patch (experimental)

Normal text inputs are detected through Windows UI Automation / Win32 as before.
Some custom fields (including Roblox chat) provide **no reliable Windows signal**
that typing is occurring. No Windows app can guarantee automatic detection in
*every* game or text control without invasive/unsafe input interception.

## Roblox or another custom chat box

1. Start WWA, enable **Translate** (Ctrl+Alt+1).
2. Open the Roblox chat input and click inside it.
3. Press **Ctrl+Alt+Shift+T** to enable compatibility for that window.
4. Type a sentence; wait for the translated suggestion popup.
5. Press **Shift+Enter** to **copy** the suggestion to the clipboard.
6. Paste manually with Ctrl+V **if the game allows it**. Clear your original
   message manually first, if required. Do not hit Enter to send before checking.
7. **Ctrl+Alt+Shift+T** again disables the mode. Esc also disables it.
   Moving to another app automatically disables it.

Compatibility mode deliberately does *not* simulate deletion/pasting: such
operations are unpredictable and sometimes blocked by games. It only collects
keystrokes after you explicitly opt in, for the active window, and it will not
operate in Windows password controls that can be identified. A custom secure
field might not identify itself: **never activate this mode in passwords, 2FA,
private messages with secrets, or sensitive data fields.**

The existing translation engine sends typed text to an external translation
service. Never enter sensitive text while translation is enabled.

## Still needs real-world testing

- Tested by static source review only; Windows/.NET SDK isn't available here.
- Roblox can block Ctrl+V or injected shortcuts.
- Keyboard-based tracking cannot reliably reflect mouse edits, selections,
  IME composition, password masking in some games, or modified text from other
  components. Do not assume full cross-app compatibility.
- The compatibility shortcut may conflict with application shortcuts.
