# Windows Translator App (WWA)

Windows Writing Assistant is a Windows 10/11 WPF desktop app for translation, writing suggestions, OCR and on-screen translated captions.

## Requirements
- Windows 10 or 11 (64-bit)
- .NET 8 SDK to build (plus desktop runtime for a framework-dependent launch)

## Building
```powershell
dotnet build WindowsWritingAssistant.csproj -c Release
```
Or run `INSTALL_WINDOWS_WRITING_ASSISTANT.bat` on Windows. **Caution:** that installer force-closes earlier WWA instances and removes older WWA/Helper installation folders; review it before running.

## Translation replacement
Translate mode shows a suggestion while you type in an editable field. Use **Shift+Enter** to accept the suggestion.

This edition includes an experimental replacement fix: waits for Shift to be released before sending delete/paste keystrokes, checks the focused field before proceeding, and uses SendInput for synthetic input. It does not guarantee compatibility with every editor, elevated application, game, or protected text box. The implementation uses the clipboard and changes its content; save anything important that is currently copied before testing.

If text appears then disappears, try Notepad first and report the target app, its version, whether it runs as administrator, and whether text was replaced/undone.

## Development status
Source-level changes have **not been compiled or tested on Windows** in this environment; validate the build and behavior before publishing a release.
