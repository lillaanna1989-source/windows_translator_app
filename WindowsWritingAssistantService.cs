using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace WindowsWritingAssistant;

public sealed class AssistantOptions {
    public bool Translate {get;set;}
    public bool Grammar {get;set;}
    public bool FixAll {get;set;}
    public string TargetLanguage {get;set;} = "English";
}

public sealed class WindowsWritingAssistantService : IDisposable
{
    const int WH_KEYBOARD_LL=13, WM_KEYDOWN=0x0100, WM_SYSKEYDOWN=0x0104;
    const int VK_SHIFT=0x10, VK_CONTROL=0x11, VK_A=0x41;
    IntPtr hook=IntPtr.Zero;
    readonly HookProc proc;
    readonly StringBuilder sentence=new();
    AssistantOptions options=new();
    SuggestionWindow? popup;
    StatusPopup? statusPopup;
    bool enabled;
    int requestVersion;
    string popupSourceText = "";
    int translationTone = 2; // 1 Normal, 2 Polite (default), 3 Welcoming
    bool replacementInProgress;
    // Compatibility mode is opt-in, bound to one foreground window, and never
    // performs destructive keyboard replacement in an unrecognized control.
    IntPtr compatibilityWindow=IntPtr.Zero;
    bool compatibilityMode;
    bool compatibilitySession;


    // Fallback for custom-rendered editors (for example game/launcher search boxes)
    // that do not expose a normal Windows/UIA text control.
    readonly StringBuilder typingProbe=new();
    long typingProbeTick;
    IntPtr typingProbeWindow=IntPtr.Zero;
    long inferredTypingUntil;
    IntPtr inferredTypingWindow=IntPtr.Zero;

    // Field-level state. Never promote arbitrary keystrokes into a typing session.
    string lastFieldIdentity = "";
    long lastTypingTick;
    long lastFocusCheckTick;
    IntPtr lastFocusWindow = IntPtr.Zero;
    bool lastFocusEditable;

    // Cache the expensive UI Automation lookup briefly, but invalidate immediately
    // when the foreground/focused HWND changes.
    bool IsEditableTextFocusedCached() {
        IntPtr foreground = GetForegroundWindow();
        IntPtr focused = TryGetFocusedWindow(out var hwnd) ? hwnd : foreground;
        long now = Environment.TickCount64;
        if (focused == lastFocusWindow && now - lastFocusCheckTick < 120)
            return lastFocusEditable;
        lastFocusWindow = focused;
        lastFocusCheckTick = now;
        lastFocusEditable = IsEditableTextFocused();
        return lastFocusEditable;
    }

    string GetCurrentFieldIdentity() {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return "";
        IntPtr focused = TryGetFocusedWindow(out var hwnd) ? hwnd : foreground;
        // HWND alone cannot distinguish two browser inputs in the same window.
        // The focused UIA element's runtime ID distinguishes those where available.
        try {
            var element = AutomationElement.FocusedElement;
            if (element != null && element.Current.HasKeyboardFocus) {
                var ids = element.GetRuntimeId();
                if (ids != null && ids.Length > 0)
                    return foreground.ToInt64() + ":" + focused.ToInt64() + ":" + string.Join(".", ids);
            }
        } catch { }
        return foreground.ToInt64() + ":" + focused.ToInt64();
    }

    void ResetTypingContext() {
        if (sentence.Length == 0 && typingProbe.Length == 0 && popupSourceText.Length == 0)
            return;
        sentence.Clear();
        typingProbe.Clear();
        inferredTypingUntil = 0;
        inferredTypingWindow = IntPtr.Zero;
        popupSourceText = "";
        requestVersion++;
        Application.Current.Dispatcher.BeginInvoke(() => popup?.Hide());
    }

    public WindowsWritingAssistantService(){ proc=HookCallback; }

    public void UpdateOptions(AssistantOptions o) {
        options=o;
        sentence.Clear();
        requestVersion++;
        Application.Current.Dispatcher.BeginInvoke(()=>popup?.Hide());
    }

    // Editing/viewing a page in the main window must NOT switch the running mode.
    // This updates only the translation destination while preserving whether
    // Translate or Grammar is actually enabled.
    public void UpdateTargetLanguage(string language) {
        options.TargetLanguage=String.IsNullOrWhiteSpace(language) ? "English" : language.Trim();
        sentence.Clear();
        requestVersion++;
        Application.Current.Dispatcher.BeginInvoke(()=>popup?.Hide());
    }

    public void Enable(AssistantOptions o) {
        options=o;
        enabled=true;
        Application.Current.Dispatcher.BeginInvoke(()=>{
            statusPopup ??= new StatusPopup();
            statusPopup.Flash(true);
        });
        if(hook!=IntPtr.Zero) return;
        hook=SetWindowsHookEx(WH_KEYBOARD_LL,proc,IntPtr.Zero,0);
        if(hook==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Disable() {
        // Keep the keyboard listener alive so Ctrl+A+2 can enable Windows Writing Assistant again.
        enabled=false;
        compatibilityMode=false;
        compatibilityWindow=IntPtr.Zero;
        sentence.Clear();
        requestVersion++;
        Application.Current.Dispatcher.BeginInvoke(()=>{
            popup?.Hide();
            statusPopup ??= new StatusPopup();
            statusPopup.Flash(false);
        });
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
        try {
            if(nCode>=0 && (wParam==(IntPtr)WM_KEYDOWN || wParam==(IntPtr)WM_SYSKEYDOWN)) {
                // Ignore keystrokes generated by Windows Writing Assistant itself.
                var keyInfo=Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if((keyInfo.flags & 0x10u)!=0 || replacementInProgress)
                    return CallNextHookEx(hook,nCode,wParam,lParam);

                int vk=(int)keyInfo.vkCode;

                // Ctrl + Alt + O = open/show the Windows Writing Assistant window.
                // This is handled before the enabled check, so it also works while Windows Writing Assistant is disabled.
                bool ctrl=Down(VK_CONTROL), alt=Down(0x12);
                if(ctrl && alt && vk==0x4F) {
                    Application.Current.Dispatcher.BeginInvoke(()=>{
                        var main=Application.Current.MainWindow;
                        if(main!=null) {
                            if(main.WindowState==WindowState.Minimized) main.WindowState=WindowState.Normal;
                            main.Show();
                            main.Activate();
                            main.Topmost=true;
                            main.Topmost=false;
                            main.Focus();
                        }
                    });
                    return (IntPtr)1;
                }

                // Ctrl + Alt + L = open Windows Writing Assistant directly at the translation language picker.
                // This works while Windows Writing Assistant is enabled, disabled, or simply hidden.
                if(ctrl && alt && vk==0x4C) {
                    Application.Current.Dispatcher.BeginInvoke(()=>{
                        if(Application.Current.MainWindow is MainWindow main)
                            main.ShowTranslationLanguagePicker();
                    });
                    return (IntPtr)1;
                }

                // Ctrl+T+R: activate live SmartReplace selection / stop.
                // Browsers may process Ctrl+T before R; use Live page if shortcut conflicts.
                if(ctrl && !alt && Down(0x54) && vk==0x52) {
                    Application.Current.Dispatcher.BeginInvoke(()=>{
                        if(Application.Current.MainWindow is MainWindow main)main.ToggleLiveSelection();
                    });
                    return (IntPtr)1;
                }

                // Ctrl + Alt + C = Capture2Text-style screen OCR. Drag around text;
                // it is translated automatically to the current Windows display language.
                if(ctrl && alt && vk==0x43) {
                    Application.Current.Dispatcher.BeginInvoke(async ()=> await ScreenOcrTranslator.CaptureAndTranslateAsync());
                    return (IntPtr)1;
                }

                // Ctrl + Alt + 1 = Translate, Ctrl + Alt + 2 = Grammar.
                // These switch the RUNNING Windows Writing Assistant immediately.
                if(ctrl && alt && vk==0x31) {
                    options.Translate=true; options.Grammar=false;
                    sentence.Clear(); typingProbe.Clear(); requestVersion++;
                    Application.Current.Dispatcher.BeginInvoke(()=>popup?.Hide());
                    return (IntPtr)1;
                }
                if(ctrl && alt && vk==0x32) {
                    options.Translate=false; options.Grammar=true;
                    sentence.Clear(); typingProbe.Clear(); requestVersion++;
                    Application.Current.Dispatcher.BeginInvoke(()=>popup?.Hide());
                    return (IntPtr)1;
                }

                // Shift + A + 1 = disable, Shift + A + 2 = enable.
                bool shift=Down(VK_SHIFT), a=Down(VK_A);
                if(shift && a && vk==0x31) {
                    enabled=false;
                    sentence.Clear();
                    requestVersion++;
                    Application.Current.Dispatcher.BeginInvoke(()=>{
                        popup?.Hide();
                        statusPopup ??= new StatusPopup();
                        statusPopup.Flash(false);
                    });
                    return (IntPtr)1;
                }
                if(shift && a && vk==0x32) {
                    enabled=true;
                    sentence.Clear();
                    requestVersion++;
                    Application.Current.Dispatcher.BeginInvoke(()=>{
                        statusPopup ??= new StatusPopup();
                        statusPopup.Flash(true);
                    });
                    return (IntPtr)1;
                }

                // Ctrl+Alt+Shift+T: opt in to a custom text field (e.g. Roblox chat).
                // Activate this while the intended chat box has focus. This mode is
                // deliberately clipboard-only because blindly deleting simulated
                // characters in a game can destroy unrelated text or trigger actions.
                if(enabled && ctrl && alt && Down(VK_SHIFT) && vk==0x54) {
                    IntPtr fg=GetForegroundWindow();
                    if(compatibilityMode && compatibilityWindow==fg) {
                        compatibilityMode=false;
                        compatibilityWindow=IntPtr.Zero;
                    } else if(fg!=IntPtr.Zero && !IsSensitiveFieldFocused()) {
                        compatibilityMode=true;
                        compatibilityWindow=fg;
                    }
                    ResetTypingContext();
                    lastFieldIdentity="";
                    Application.Current.Dispatcher.BeginInvoke(()=>{
                        statusPopup ??= new StatusPopup();
                        statusPopup.Flash(compatibilityMode);
                    });
                    return (IntPtr)1;
                }

                if(!enabled) return CallNextHookEx(hook,nCode,wParam,lParam);
                if(compatibilityMode && GetForegroundWindow()!=compatibilityWindow) {
                    compatibilityMode=false;
                    compatibilityWindow=IntPtr.Zero;
                    ResetTypingContext();
                }
                // Esc closes Roblox chat and menus. Do not silently disarm the
                // explicitly opted-in window; only discard the unfinished text.
                // The mode still disarms on window switch or manual hotkey toggle.
                if(compatibilityMode && vk==0x1B) {
                    ResetTypingContext();
                    return CallNextHookEx(hook,nCode,wParam,lParam);
                }

                // Helper must only react while the keyboard focus is in an actual
                // editable text control. This prevents ordinary page/app keyboard
                // navigation from being collected as a sentence.
                                 // Fail closed: never infer editable fields from arbitrary typing.
                 // This also prevents keys pressed outside a field from leaking into a later field.
                 // Explicit compatibility mode takes precedence even if Roblox
                 // exposes an unstable/partially detected editable control.
                 compatibilitySession=compatibilityMode &&
                     compatibilityWindow==GetForegroundWindow() && !IsSensitiveFieldFocused();
                 bool standardField=!compatibilitySession && IsEditableTextFocusedCached();
                 if(!standardField && !compatibilitySession) {
                     ResetTypingContext();
                     return CallNextHookEx(hook,nCode,wParam,lParam);
                 }
                 // Game chat widgets can recreate their UIA element after every key.
                 // Within an explicitly opted-in compatibility window, use the
                 // stable top-level HWND instead of a volatile UIA runtime ID.
                 // This keeps repeated chat messages working after the first one.
                 var currentField = compatibilitySession
                     ? "compat:" + compatibilityWindow.ToInt64()
                     : GetCurrentFieldIdentity();
                 long nowTick=Environment.TickCount64;
                 long idleLimit=compatibilitySession ? 30000 : 15000;
                 if(currentField != lastFieldIdentity ||
                    nowTick - lastTypingTick > idleLimit) {
                     ResetTypingContext();
                     lastFieldIdentity = currentField;
                 }
                 lastTypingTick = nowTick;
// Translation popup modes: 1 Normal, 2 Polite, 3 Welcoming.
                // The default is Polite, so Shift+Enter by itself accepts Polite.
                if(options.Translate && popup?.IsVisible==true && !Down(VK_CONTROL) && !Down(0x12) &&
                   (vk==0x31 || vk==0x32 || vk==0x33 || vk==0x61 || vk==0x62 || vk==0x63)) {
                    translationTone = (vk==0x31 || vk==0x61) ? 1 : (vk==0x32 || vk==0x62) ? 2 : 3;
                    Application.Current.Dispatcher.BeginInvoke(()=>popup?.SelectMode(translationTone));
                    return (IntPtr)1;
                }

                // Shift+Enter accepts. Normal Enter is never stolen.
                if(vk==13 && Down(VK_SHIFT) && popup?.IsVisible==true) {
                    // Never paste a stale translation over newer text.
                    if(!String.Equals(popupSourceText,sentence.ToString(),StringComparison.Ordinal)) {
                        popup.Dispatcher.BeginInvoke(()=>popup.Hide());
                        popupSourceText="";
                        return CallNextHookEx(hook,nCode,wParam,lParam);
                    }
                    string replacement=popup.Suggestion;
                    // Clipboard-only compatibility fallback. No deletion or
                    // simulated paste into unrecognized game text fields.
                    if(compatibilitySession) {
                        if(!String.IsNullOrWhiteSpace(replacement)) {
                            try { Clipboard.SetText(replacement); } catch { }
                        }
                        sentence.Clear();
                        requestVersion++;
                        popupSourceText="";
                        // Keep compatibility armed for the NEXT message.
                        // Only the current sentence is cleared, not the mode.
                        lastFieldIdentity="compat:" + compatibilityWindow.ToInt64();
                        lastTypingTick=Environment.TickCount64;
                        popup.Dispatcher.BeginInvoke(()=>popup.Hide());
                        return (IntPtr)1;
                    }
                     string sourceField=GetCurrentFieldIdentity();
                    int oldLength=sentence.Length;
                    popup.Dispatcher.BeginInvoke(()=>popup.Hide());
                    sentence.Clear();
                    requestVersion++;

                    Application.Current.Dispatcher.BeginInvoke(async ()=>{
                        // Shift+Enter's keydown is intercepted above, but the physical Shift
                        // key may still be down. Sending Backspace or Ctrl+V while Shift
                        // is held selects text / invokes Ctrl+Shift+V in many apps.
                        // Wait for key release rather than injecting during the shortcut.
                        try {
                            for(int wait=0; wait<100 && Down(VK_SHIFT); wait++)
                                await Task.Delay(10);
                            if(Down(VK_SHIFT) || Down(VK_CONTROL) || Down(0x12)) return;
                            if(!enabled || sourceField!=GetCurrentFieldIdentity()) return;
                            if(oldLength<=0 || String.IsNullOrEmpty(replacement)) return;
                            replacementInProgress=true;
                            try {
                                // Keyboard injection is imperfect in protected/custom fields.
                                // If a batch fails, do not proceed to paste over unknown text.
                                if(!SendBackspaces(oldLength)) return;
                                await Task.Delay(65);
                                if(sourceField!=GetCurrentFieldIdentity()) return;
                                Clipboard.SetText(replacement);
                                await Task.Delay(55); // allow clipboard-aware editors to observe the update
                                if(sourceField!=GetCurrentFieldIdentity()) return;
                                SendPaste();
                            } finally { replacementInProgress=false; }
                        } catch { replacementInProgress=false; }
                    });
                    return (IntPtr)1;
                }

                // Don't treat Ctrl/Alt shortcuts as typed prose.
                if(Down(VK_CONTROL) || Down(0x12)) return CallNextHookEx(hook,nCode,wParam,lParam);

                if(vk==8) {
                    if(sentence.Length>0) sentence.Length--;
                    QueueRefresh();
                } else if(vk==13) {
                    // A normal Enter starts a new sentence/line and remains normal.
                    sentence.Clear(); requestVersion++;
                    Application.Current.Dispatcher.BeginInvoke(()=>popup?.Hide());
                } else {
                    var s=KeyToText(vk,keyInfo.scanCode);
                    if(s!=null) {
                        sentence.Append(s);
                        // Keep only the current sentence after sentence-ending punctuation.
                        TrimToCurrentSentence();
                        QueueRefresh();
                    }
                }
            }
        } catch {}
        return CallNextHookEx(hook,nCode,wParam,lParam);
    }

    void TrimToCurrentSentence() {
        if(sentence.Length<2) return;
        // If the character BEFORE the newest content ended a sentence, begin fresh.
        string x=sentence.ToString();
        int last=-1;
        for(int i=0;i<x.Length-1;i++) {
            if((x[i]=='.'||x[i]=='!'||x[i]=='?') && i+1<x.Length && Char.IsWhiteSpace(x[i+1]))
                last=i+1;
        }
        if(last>=0 && last<x.Length) {
            string rest=x[(last+1)..].TrimStart();
            sentence.Clear();
            sentence.Append(rest);
        }
    }

    void QueueRefresh() {
        string input=sentence.ToString();
        int myVersion=++requestVersion;

        // Freeze the active mode for THIS request. The shared options object can be
        // changed by navigation/hotkeys while the debounce or web request is running.
        // A translation request must therefore never drift into the grammar pipeline.
        var requestOptions = new AssistantOptions {
            Translate = options.Translate,
            Grammar = options.Grammar,
            FixAll = options.FixAll,
            TargetLanguage = options.TargetLanguage
        };
        // Modes are intentionally exclusive. If an old/corrupt state ever has both
        // flags set, Translate wins for a request that started as Translate.
        if(requestOptions.Translate) requestOptions.Grammar=false;

        if(!enabled || input.Trim().Length<3) {
            Application.Current.Dispatcher.BeginInvoke(()=>popup?.Hide());
            return;
        }

        // IMPORTANT: show the popup BEFORE any web request.
        Application.Current.Dispatcher.BeginInvoke(async ()=>{
            try {
                if(!enabled || myVersion!=requestVersion) return;

                // Debounce rapid typing BEFORE doing any network/UI work. This keeps typing
                // responsive and means the temporary “Windows Writing Assistant is checking…” text can never be
                // accepted/copied as a suggestion.
                await Task.Delay(420);
                if(!enabled || myVersion!=requestVersion) return;

                // Let the user know WWA is actually working during network/local processing.
                // This is a separate no-activate window and is NEVER inserted into the text field.
                statusPopup ??= new StatusPopup();
                statusPopup.ShowWorking(requestOptions.Translate);

                string suggestion=await TextAssistant.SuggestAsync(input,requestOptions);
                if(!enabled || myVersion!=requestVersion) { statusPopup?.Dismiss(); return; }

                if(String.IsNullOrWhiteSpace(suggestion) || suggestion==input) {
                    popup?.Hide();
                    statusPopup?.ShowNoChanges();
                    return;
                }

                // Bind this popup to the exact text snapshot that produced it.
                // An older web request must never become the suggestion for newer typing.
                if(myVersion!=requestVersion || !String.Equals(input,sentence.ToString(),StringComparison.Ordinal)) return;
                popupSourceText=input;
                popup ??= new SuggestionWindow();
                if(requestOptions.Translate) {
                    var variants=TextAssistant.CreateToneVariants(suggestion, requestOptions.TargetLanguage);
                    translationTone=2; // Shift+Enter alone always means Polite.
                    popup.SetSuggestions(variants.normal,variants.polite,variants.welcoming,translationTone);
                } else {
                    popup.SetSuggestion(suggestion);
                }
                PositionPopup();
                statusPopup?.Dismiss();
                if(!popup.IsVisible) popup.Show();
            }
            catch {
                // Keep failures from killing the global keyboard listener.
                popup?.Hide();
                statusPopup?.Dismiss();
            }
        });
    }

    void PositionPopup() {
        if(popup==null) return;
        popup.UpdateLayout();
        if(TryGetCaretScreenPoint(out POINT p)) {
            popup.Left=Math.Max(0,p.X);
            popup.Top=Math.Max(0,p.Y-popup.ActualHeight-6);
        } else {
            GetCursorPos(out p);
            popup.Left=Math.Max(0,p.X);
            popup.Top=Math.Max(0,p.Y-popup.ActualHeight-18);
        }
    }

    static bool TryGetCaretScreenPoint(out POINT p) {
        p=new POINT();
        IntPtr fg=GetForegroundWindow();
        if(fg==IntPtr.Zero) return false;
        uint tid=GetWindowThreadProcessId(fg,IntPtr.Zero);
        GUITHREADINFO gi=new(){cbSize=(uint)Marshal.SizeOf<GUITHREADINFO>()};
        if(!GetGUIThreadInfo(tid,ref gi) || gi.hwndCaret==IntPtr.Zero) return false;
        p.X=gi.rcCaret.Left;
        p.Y=gi.rcCaret.Top;
        return ClientToScreen(gi.hwndCaret,ref p);
    }


    bool TryCustomTypingFallback(int vk,out bool promoted) {
        promoted=false;
        if(Down(VK_CONTROL) || Down(0x12)) return false;
        IntPtr fg=GetForegroundWindow();
        long now=Environment.TickCount64;
        if(fg!=IntPtr.Zero && fg==inferredTypingWindow && now<=inferredTypingUntil) {
            inferredTypingUntil=now+5000;
            return true;
        }
        string? ch=KeyToText(vk);
        if(ch==null || ch.Length!=1 || Char.IsControl(ch[0])) {
            if(vk==8 && fg==typingProbeWindow && typingProbe.Length>0) { typingProbe.Length--; return false; }
            typingProbe.Clear(); return false;
        }
        if(fg!=typingProbeWindow || now-typingProbeTick>1800) {
            typingProbe.Clear(); typingProbeWindow=fg;
        }
        typingProbeTick=now;
        typingProbe.Append(ch);
        if(typingProbe.Length<3) return false;
        string probe=typingProbe.ToString();
        const string gameKeys="wasdqerf1234567890 ";
        bool hasNonGame=false;
        var distinct=new HashSet<char>();
        foreach(char c0 in probe) {
            char c=Char.ToLowerInvariant(c0);
            if(Char.IsLetter(c)) distinct.Add(c);
            if(gameKeys.IndexOf(c)<0) hasNonGame=true;
        }
        if(!hasNonGame || distinct.Count<2) return false;
        sentence.Clear(); sentence.Append(probe);
        typingProbe.Clear();
        inferredTypingWindow=fg; inferredTypingUntil=now+5000;
        promoted=true;
        return true;
    }

    // A separate fail-closed check prevents manual compatibility mode from
    // recording ordinary Windows password/credential fields when identifiable.
    // Some games expose no accessibility information: use only after explicitly
    // focusing their chat input; never use in password or private-data fields.
    static bool IsSensitiveFieldFocused() {
        try {
            var el=AutomationElement.FocusedElement;
            if(el!=null && el.Current.IsPassword) return true;
            if(TryGetFocusedWindow(out var hwnd)) {
                var sb=new StringBuilder(128);
                if(GetClassName(hwnd,sb,sb.Capacity)>0 &&
                   sb.ToString().Equals("Edit",StringComparison.OrdinalIgnoreCase)) {
                    // ES_PASSWORD = 0x20 in the low 16 bits of GWL_STYLE.
                    long style=GetWindowLongPtr(hwnd,-16).ToInt64();
                    if((style & 0x20)!=0) return true;
                }
            }
        } catch { return true; }
        return false;
    }

    static bool IsEditableTextFocused() {
        try {
            // 1) Ask UI Automation for the element that ACTUALLY owns keyboard focus.
            // Do not identify apps by process name: Windows Writing Assistant should follow the active field.
            var el=AutomationElement.FocusedElement;
            if(el!=null && el.Current.IsEnabled && el.Current.HasKeyboardFocus) {
                 if(el.Current.IsPassword) return false;
                if(IsWritableAutomationElement(el)) return true;
            }

            // 2) Some programs expose the focused child window to Win32 but UIA reports only
            // a parent/container. Inspect that exact focused HWND as a second view of the same
            // keyboard target.
            if(TryGetFocusedWindow(out IntPtr focusHwnd)) {
                try {
                    var hwndEl=AutomationElement.FromHandle(focusHwnd);
                    if(hwndEl!=null && hwndEl.Current.IsEnabled && !hwndEl.Current.IsPassword && IsWritableAutomationElement(hwndEl))
                        return true;
                } catch { }

                // 3) Classic/native editors may expose no useful UIA patterns at all.
                var sb=new StringBuilder(256);
                if(GetClassName(focusHwnd,sb,sb.Capacity)>0) {
                    string cls=sb.ToString();
                    if(cls.Equals("Edit",StringComparison.OrdinalIgnoreCase) ||
                       cls.StartsWith("RichEdit",StringComparison.OrdinalIgnoreCase) ||
                       cls.Contains("RICHEDIT",StringComparison.OrdinalIgnoreCase) ||
                       cls.Contains("TextBox",StringComparison.OrdinalIgnoreCase) ||
                       cls.Contains("Scintilla",StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            // 4) Last strong signal: Windows says the foreground thread currently has a caret.
            // A caret belongs to the active typing position, so this catches custom/native text
            // controls without turning an ordinary window, menu, desktop, or game into a field.
            if(TryGetCaretScreenPoint(out _)) return true;

            return false;
        } catch { return false; }
    }

    static bool IsWritableAutomationElement(AutomationElement el) {
        try {
            if(el.TryGetCurrentPattern(ValuePattern.Pattern,out object valueObj)) {
                var value=(ValuePattern)valueObj;
                if(!value.Current.IsReadOnly && !el.Current.IsPassword) return true;
            }

            var type=el.Current.ControlType;
            if(type==ControlType.Edit && !el.Current.IsPassword) return true;

            // Browser/Electron rich editors/contenteditable surfaces generally expose TextPattern
            // rather than ValuePattern. Only accept it on the element that owns keyboard focus.
            if((type==ControlType.Document || type==ControlType.Custom || type==ControlType.Text) &&
               el.Current.IsKeyboardFocusable &&
               el.TryGetCurrentPattern(TextPattern.Pattern,out _))
                return true;

            // Custom frameworks often expose their editor as Custom but give it an editor-ish
            // automation/class name. This is still field-level detection, never an app whitelist.
            string hint=((el.Current.ClassName??"")+" "+(el.Current.AutomationId??"")+" "+(el.Current.Name??"")).ToLowerInvariant();
            if(el.Current.IsKeyboardFocusable &&
               (hint.Contains("textbox") || hint.Contains("text box") || hint.Contains("textinput") ||
                hint.Contains("text input") || hint.Contains("editor") || hint.Contains("compose") ||
                hint.Contains("message input") || hint.Contains("searchbox") || hint.Contains("search box")))
                return true;
        } catch { }
        return false;
    }

    static bool TryGetFocusedWindow(out IntPtr hwnd) {
        hwnd=IntPtr.Zero;
        try {
            IntPtr fg=GetForegroundWindow();
            if(fg==IntPtr.Zero) return false;
            uint tid=GetWindowThreadProcessId(fg,IntPtr.Zero);
            var gi=new GUITHREADINFO{cbSize=(uint)Marshal.SizeOf<GUITHREADINFO>()};
            if(!GetGUIThreadInfo(tid,ref gi) || gi.hwndFocus==IntPtr.Zero) return false;
            hwnd=gi.hwndFocus;
            return true;
        } catch { return false; }
    }

    static bool TryGetFocusedWindowClass(out string className) {
        className="";
        try {
            IntPtr fg=GetForegroundWindow();
            if(fg==IntPtr.Zero) return false;
            uint tid=GetWindowThreadProcessId(fg,IntPtr.Zero);
            var gi=new GUITHREADINFO{cbSize=(uint)Marshal.SizeOf<GUITHREADINFO>()};
            if(!GetGUIThreadInfo(tid,ref gi) || gi.hwndFocus==IntPtr.Zero) return false;
            var sb=new StringBuilder(256);
            if(GetClassName(gi.hwndFocus,sb,sb.Capacity)<=0) return false;
            className=sb.ToString();
            return className.Length>0;
        } catch { return false; }
    }

    static bool Down(int vk)=>(GetAsyncKeyState(vk)&0x8000)!=0;

    static string? KeyToText(int vk) => KeyToText(vk,0);

    static string? KeyToText(int vk, uint physicalScanCode) {
        // Build the key state explicitly. A low-level keyboard hook runs before
        // GetKeyboardState is guaranteed to contain the key that triggered this
        // callback. That was the remaining punctuation bug: on Shift+number/OEM
        // combinations Windows Writing Assistant could see the base digit instead of the symbol.
        try {
            byte[] state=new byte[256];
            GetKeyboardState(state);

            // Force the current key and the physical modifier state into the
            // snapshot used by ToUnicodeEx. Preserve toggle bits (Caps/Num/etc.).
            state[vk]=(byte)(state[vk] | 0x80);
            SetDownState(state,VK_SHIFT,Down(VK_SHIFT));
            SetDownState(state,VK_CONTROL,Down(VK_CONTROL));
            SetDownState(state,0x12,Down(0x12)); // Alt
            SetDownState(state,0xA0,Down(0xA0)); // LShift
            SetDownState(state,0xA1,Down(0xA1)); // RShift
            SetDownState(state,0xA2,Down(0xA2)); // LCtrl
            SetDownState(state,0xA3,Down(0xA3)); // RCtrl
            SetDownState(state,0xA4,Down(0xA4)); // LAlt
            SetDownState(state,0xA5,Down(0xA5)); // RAlt / AltGr

            IntPtr fg=GetForegroundWindow();
            uint threadId=fg==IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg,IntPtr.Zero);
            IntPtr layout=GetKeyboardLayout(threadId);
            uint scan=physicalScanCode!=0 ? physicalScanCode : MapVirtualKeyEx((uint)vk,0,layout);
            var text=new StringBuilder(8);
            int count=ToUnicodeEx((uint)vk,scan,state,text,text.Capacity,0,layout);

            if(count>0) return text.ToString(0,Math.Min(count,text.Length));

            if(count<0) {
                // Clear a dead-key state without letting it poison the next key.
                var clear=new StringBuilder(8);
                ToUnicodeEx((uint)vk,scan,state,clear,clear.Capacity,0,layout);
            }
        } catch { }
        return null;
    }

    static void SetDownState(byte[] state,int vk,bool down) {
        if(down) state[vk]=(byte)(state[vk] | 0x80);
        else state[vk]=(byte)(state[vk] & 0x7F);
    }

    static bool SendBackspaces(int count) {
        // Large key batches can overwhelm editors. SendInput reports failures,
        // unlike the old keybd_event approach; stop on the first failed batch.
        if(count<1 || count>10000) return false;
        for(int i=0;i<count;i++) {
            INPUT[] keys={ KeyboardInput(0x08,false), KeyboardInput(0x08,true) };
            if(SendInput((uint)keys.Length,keys,Marshal.SizeOf<INPUT>())!=(uint)keys.Length)
                return false;
        }
        return true;
    }
    [StructLayout(LayoutKind.Sequential)] struct INPUT {
        public uint type; public InputUnion data;
    }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT {
        public ushort wVk,wScan; public uint dwFlags,time; public UIntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT {
        public int dx,dy; public uint mouseData,dwFlags,time; public UIntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT {
        public uint uMsg; public ushort wParamL,wParamH;
    }
    static INPUT KeyboardInput(ushort vk,bool up) => new(){type=1,data=new InputUnion{ki=new KEYBDINPUT{wVk=vk,dwFlags=up?2u:0u}}};
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint nInputs,[In] INPUT[] pInputs,int cbSize);
    static void SendPaste() {
        INPUT[] keys={KeyboardInput(0x11,false),KeyboardInput(0x56,false),KeyboardInput(0x56,true),KeyboardInput(0x11,true)};
        SendInput((uint)keys.Length,keys,Marshal.SizeOf<INPUT>());
    }
    public void Dispose() {
        enabled=false;
        var h=hook; hook=IntPtr.Zero;
        if(h!=IntPtr.Zero) UnhookWindowsHookEx(h);
        popup?.Close();
    }

    delegate IntPtr HookProc(int nCode,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode,scanCode,flags,time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct POINT {public int X;public int Y;}
    [StructLayout(LayoutKind.Sequential)] struct RECT {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO {
        public uint cbSize,flags; public IntPtr hwndActive,hwndFocus,hwndCapture,hwndMenuOwner,hwndMoveSize,hwndCaret;
        public RECT rcCaret;
    }
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc fn,IntPtr mod,uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int n,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern short GetKeyState(int k);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int k);
    [DllImport("user32.dll")] static extern bool GetKeyboardState([Out] byte[] lpKeyState);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyEx(uint uCode,uint uMapType,IntPtr dwhkl);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int ToUnicodeEx(uint wVirtKey,uint wScanCode,byte[] lpKeyState,[Out] StringBuilder pwszBuff,int cchBuff,uint wFlags,IntPtr dwhkl);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,IntPtr pid);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint tid,ref GUITHREADINFO info);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd,StringBuilder lpClassName,int nMaxCount);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW", SetLastError=true)]
    static extern IntPtr GetWindowLongPtr64(IntPtr hwnd,int index);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW", SetLastError=true)]
    static extern int GetWindowLong32(IntPtr hwnd,int index);
    static IntPtr GetWindowLongPtr(IntPtr hwnd,int index) => IntPtr.Size==8 ?
        GetWindowLongPtr64(hwnd,index) : new IntPtr(GetWindowLong32(hwnd,index));
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h,ref POINT p);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
}

public static class TextAssistant
{
    static readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(5)};
    static readonly Dictionary<string,string> Codes=new(StringComparer.OrdinalIgnoreCase) {
      ["Afrikaans"]="af",["Albanian"]="sq",["Amharic"]="am",["Arabic"]="ar",["Armenian"]="hy",
      ["Azerbaijani"]="az",["Basque"]="eu",["Belarusian"]="be",["Bengali"]="bn",["Bosnian"]="bs",
      ["Bulgarian"]="bg",["Catalan"]="ca",["Chinese"]="zh-CN",["Simplified Chinese"]="zh-CN",
      ["Traditional Chinese"]="zh-TW",["Mandarin"]="zh-CN",["Cantonese"]="zh-TW",["Croatian"]="hr",
      ["Czech"]="cs",["Danish"]="da",["Dutch"]="nl",["English"]="en",["Estonian"]="et",
      ["Filipino"]="tl",["Tagalog"]="tl",["Finnish"]="fi",["French"]="fr",["Galician"]="gl",
      ["Georgian"]="ka",["German"]="de",["Greek"]="el",["Gujarati"]="gu",["Hebrew"]="he",
      ["Hindi"]="hi",["Hungarian"]="hu",["Icelandic"]="is",["Indonesian"]="id",["Irish"]="ga",
      ["Italian"]="it",["Japanese"]="ja",["Kannada"]="kn",["Kazakh"]="kk",["Khmer"]="km",
      ["Korean"]="ko",["Lao"]="lo",["Latvian"]="lv",["Lithuanian"]="lt",["Macedonian"]="mk",
      ["Malay"]="ms",["Malayalam"]="ml",["Marathi"]="mr",["Mongolian"]="mn",["Nepali"]="ne",
      ["Norwegian"]="no",["Persian"]="fa",["Farsi"]="fa",["Polish"]="pl",["Portuguese"]="pt",
      ["Punjabi"]="pa",["Romanian"]="ro",["Russian"]="ru",["Serbian"]="sr",["Slovak"]="sk",
      ["Slovenian"]="sl",["Spanish"]="es",["Swahili"]="sw",["Swedish"]="sv",["Tamil"]="ta",
      ["Telugu"]="te",["Thai"]="th",["Turkish"]="tr",["Ukrainian"]="uk",["Urdu"]="ur",
      ["Uzbek"]="uz",["Vietnamese"]="vi",["Welsh"]="cy",["Yiddish"]="yi",["Zulu"]="zu",
      ["Esperanto"]="eo",["Haitian Creole"]="ht",["Hmong"]="hmn",["Latin"]="la",["Maori"]="mi",
      ["Cebuano"]="ceb",["Corsican"]="co",["Frisian"]="fy",["Hawaiian"]="haw",["Kurdish"]="ku",
      ["Kyrgyz"]="ky",["Luxembourgish"]="lb",["Malagasy"]="mg",["Samoan"]="sm",["Scots Gaelic"]="gd",
      ["Scottish Gaelic"]="gd",["Sesotho"]="st",["Shona"]="sn",["Sindhi"]="sd",["Somali"]="so",
      ["Sundanese"]="su",["Tajik"]="tg",["Xhosa"]="xh",["Burmese"]="my",["Myanmar"]="my",
      ["Chichewa"]="ny",["Igbo"]="ig",["Javanese"]="jw",["Kinyarwanda"]="rw",["Hausa"]="ha",
      ["Yoruba"]="yo",["Odia"]="or",["Oriya"]="or",["Turkmen"]="tk",["Tatar"]="tt",
      ["Uyghur"]="ug",["Uighur"]="ug",["Pashto"]="ps",
      ["Aymara"]="ay",["Bambara"]="bm",["Bhojpuri"]="bho",["Dhivehi"]="dv",["Dogri"]="doi",
      ["Ewe"]="ee",["Guarani"]="gn",["Ilocano"]="ilo",["Konkani"]="gom",["Krio"]="kri",
      ["Lingala"]="ln",["Luganda"]="lg",["Maithili"]="mai",["Meiteilon"]="mni-Mtei",["Mizo"]="lus",
      ["Oromo"]="om",["Quechua"]="qu",["Sanskrit"]="sa",["Sepedi"]="nso",["Tigrinya"]="ti",
      ["Tsonga"]="ts",["Twi"]="ak",["Assamese"]="as"
    };

    public static bool TryResolveLanguage(string input,out string displayName,out string code,bool allowCorrection=true) {
        displayName=""; code="";
        string x=(input??"").Trim();
        if(String.IsNullOrWhiteSpace(x)) return false;
        foreach(var kv in Codes) {
            if(kv.Key.Equals(x,StringComparison.OrdinalIgnoreCase)) { displayName=kv.Key; code=kv.Value; return true; }
        }
        foreach(var kv in Codes) {
            if(kv.Value.Equals(x,StringComparison.OrdinalIgnoreCase)) { displayName=kv.Key; code=kv.Value; return true; }
        }
        // Advanced escape hatch: accept a valid ISO/BCP-47-looking code directly. This lets
        // newly-supported translation languages work even before their friendly name is added.
        if(System.Text.RegularExpressions.Regex.IsMatch(x,@"^[A-Za-z]{2,3}(?:-[A-Za-z]{2,8})?$")) {
            displayName=x; code=x; return true;
        }
        if(!allowCorrection) return false;
        int best=int.MaxValue; KeyValuePair<string,string> hit=default;
        foreach(var kv in Codes) {
            int d=Levenshtein(x.ToLowerInvariant(),kv.Key.ToLowerInvariant());
            if(d<best) { best=d; hit=kv; }
        }
        // Dynamic fuzzy matching: once the user has typed at least 3 characters,
        // accept a clear prefix ("Jap" -> Japanese) or a reasonably close typo
        // ("Japen" -> Japanese). Keep very short/ambiguous input uncommitted.
        if(x.Length>=3) {
            foreach(var kv in Codes) {
                if(kv.Key.StartsWith(x,StringComparison.OrdinalIgnoreCase)) {
                    displayName=kv.Key; code=kv.Value; return true;
                }
            }
        }
        int limit=x.Length>=4 ? Math.Max(2,(int)Math.Ceiling(Math.Max(x.Length,hit.Key?.Length ?? 0)*0.45)) : 1;
        if(best<=limit && !String.IsNullOrEmpty(hit.Key)) { displayName=hit.Key; code=hit.Value; return true; }
        return false;
    }

    public static string FriendlyShortCode(string displayName,string apiCode) {
        // User-facing shorthand. Translation still uses the real API language code.
        if(displayName.Equals("Japanese",StringComparison.OrdinalIgnoreCase)) return "JP";
        if(displayName.Equals("Chinese",StringComparison.OrdinalIgnoreCase) ||
           displayName.Contains("Chinese",StringComparison.OrdinalIgnoreCase) ||
           displayName.Equals("Mandarin",StringComparison.OrdinalIgnoreCase)) return "CN";
        return (apiCode??"").Split('-')[0].ToUpperInvariant();
    }

    static int Levenshtein(string a,string b) {
        var prev=new int[b.Length+1]; var cur=new int[b.Length+1];
        for(int j=0;j<=b.Length;j++) prev[j]=j;
        for(int i=1;i<=a.Length;i++) {
            cur[0]=i;
            for(int j=1;j<=b.Length;j++) cur[j]=Math.Min(Math.Min(cur[j-1]+1,prev[j]+1),prev[j-1]+(a[i-1]==b[j-1]?0:1));
            (prev,cur)=(cur,prev);
        }
        return prev[b.Length];
    }

    public static event Action<string,string,string>? TranslationCompleted;

    public static async Task<string> SuggestAsync(string text,AssistantOptions o) {
        string current=text;
        // Translate and Grammar are separate modes. Never grammar-correct text on its
        // way into Translate, even if a stale UI state accidentally contains both flags.
        if(!o.Translate && o.Grammar) {
            // Run reliable local corrections FIRST so obvious errors do not depend on the network.
            current=OfflineGrammar(current);

            // Then let LanguageTool improve anything more complex.
            var f=await GrammarAsync(current);
            if(!String.IsNullOrWhiteSpace(f)) current=f;
        }
        if(o.Translate&&!String.IsNullOrWhiteSpace(o.TargetLanguage)) {
            var t=await TranslateAsync(current,o.TargetLanguage);
            if(!String.IsNullOrWhiteSpace(t)) {
                if(!String.Equals(t,current,StringComparison.Ordinal)) TranslationCompleted?.Invoke(current,t,o.TargetLanguage);
                current=t;
            }
        }
        return current;
    }
    public static (string normal,string polite,string welcoming) CreateToneVariants(string translated,string language) {
        string normal=translated.Trim();
        string polite=normal;
        string welcoming=normal;
        string code=ResolveLanguageCode(language).ToLowerInvariant();

        // Lightweight, local tone shaping. It deliberately keeps the translation itself intact
        // and only adds conventional courtesy/warmth markers where that is reasonably safe.
        if(code.StartsWith("en")) {
            if(!normal.StartsWith("please ",StringComparison.OrdinalIgnoreCase) &&
               !normal.StartsWith("thank",StringComparison.OrdinalIgnoreCase))
                polite="Please, "+Char.ToLowerInvariant(normal[0])+normal[1..];
            welcoming="Glad to have you here — "+Char.ToLowerInvariant(normal[0])+normal[1..];
        } else if(code.StartsWith("ja")) {
            // Do not append a Japanese full stop after an existing question/exclamation.
            // The old rule turned e.g. "...？" into "...？。" and "...！！" into "...！！。".
            // Only add 。 when the translation has no sentence-ending punctuation at all.
            bool hasJapaneseTerminator = normal.EndsWith("。") || normal.EndsWith("｡") ||
                                         normal.EndsWith("？") || normal.EndsWith("?") ||
                                         normal.EndsWith("！") || normal.EndsWith("!");
            if(!hasJapaneseTerminator) normal += "。";
            polite=normal.Replace("だ。","です。").Replace("だよ。","ですよ。");
            welcoming="ようこそ。"+normal;
        } else if(code.StartsWith("hu")) {
            polite="Kérem, "+Char.ToLowerInvariant(normal[0])+normal[1..];
            welcoming="Örömmel mondom: "+Char.ToLowerInvariant(normal[0])+normal[1..];
        } else {
            // For languages without a safe local tone rule, preserve the translation rather
            // than inventing words or corrupting grammar. The mode UI still remains available.
            polite=normal; welcoming=normal;
        }
        return (normal,polite,welcoming);
    }

    static string OfflineGrammar(string text) {
        string s=text;

        // Word-boundary replacements work at the START of a sentence too.
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bdont\b", "don't",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bcant\b", "can't",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bdoesnt\b", "doesn't",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bwont\b", "won't",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bdidnt\b", "didn't",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bisnt\b", "isn't",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\barent\b", "aren't",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bim\b", "I'm",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bi\b", "I");

        // Very common subject/verb agreement mistakes.
        s=System.Text.RegularExpressions.Regex.Replace(s,
            @"\b(I|you|we|they)\s+(don't|do not)\s+([A-Za-z]+)s\b",
            m => m.Groups[1].Value+" "+m.Groups[2].Value+" "+
                 RemoveThirdPersonS(m.Groups[3].Value),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bthis\s+are\b", "this is",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bthat\s+are\b", "that is",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bthese\s+is\b", "these are",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bthose\s+is\b", "those are",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bI\s+is\b", "I am",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\byou\s+is\b", "you are",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s, @"\bthey\s+is\b", "they are",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if(s.Length>0 && Char.IsLetter(s[0]))
            s=Char.ToUpper(s[0])+s[1..];

        return s;
    }

    static string RemoveThirdPersonS(string word) {
        if(word.EndsWith("ies",StringComparison.OrdinalIgnoreCase) && word.Length>3)
            return word[..^3]+"y";
        if(word.EndsWith("es",StringComparison.OrdinalIgnoreCase) &&
           (word.EndsWith("ches",StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("shes",StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("xes",StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("zes",StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("oes",StringComparison.OrdinalIgnoreCase)))
            return word[..^2];
        if(word.EndsWith("s",StringComparison.OrdinalIgnoreCase) &&
           !word.EndsWith("ss",StringComparison.OrdinalIgnoreCase))
            return word[..^1];
        return word;
    }

    static async Task<string> GrammarAsync(string text) {
        try {
            using var content=new FormUrlEncodedContent(new Dictionary<string,string>{{"text",text},{"language","auto"}});
            using var resp=await http.PostAsync("https://api.languagetool.org/v2/check",content);
            if(!resp.IsSuccessStatusCode)return text;
            using var doc=JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var edits=new List<(int off,int len,string rep)>();
            foreach(var m in doc.RootElement.GetProperty("matches").EnumerateArray()) {
                var r=m.GetProperty("replacements"); if(r.GetArrayLength()==0)continue;
                edits.Add((m.GetProperty("offset").GetInt32(),m.GetProperty("length").GetInt32(),r[0].GetProperty("value").GetString()??""));
            }
            edits.Sort((a,b)=>b.off.CompareTo(a.off));
            var s=text;
            foreach(var e in edits) if(e.off>=0&&e.off+e.len<=s.Length)s=s.Remove(e.off,e.len).Insert(e.off,e.rep);
            return s;
        } catch{return text;}
    }
    static string ResolveLanguageCode(string language) {
        return TryResolveLanguage(language,out _,out var code,true) ? code : "";
    }

    public static Task<string> TranslateForOcrAsync(string text,string windowsLanguageCode) => TranslateAsync(text,windowsLanguageCode);

    static async Task<string> TranslateAsync(string text,string language) {
        string target=ResolveLanguageCode(language);
        if(String.IsNullOrWhiteSpace(target)) return text;

        // Google supports automatic source-language detection (sl=auto).
        // MyMemory does NOT accept AUTO in langpair, so never send auto to it.
        // Try both Google translation hosts. The first host can occasionally reject/
        // throttle desktop requests even though the same endpoint worked moments earlier.
        // A normal browser User-Agent also avoids the empty/403 response seen in v9.8.x.
        foreach(string host in new[]{"https://translate.googleapis.com","https://translate.google.com"}) {
            try {
                string url=host+"/translate_a/single?client=gtx&sl=auto&tl="+
                    Uri.EscapeDataString(target)+"&dt=t&q="+Uri.EscapeDataString(text);
                using var req=new HttpRequestMessage(HttpMethod.Get,url);
                req.Headers.TryAddWithoutValidation("User-Agent","Mozilla/5.0 WindowsWritingAssistant/9.8.6");
                req.Headers.TryAddWithoutValidation("Accept","application/json,text/plain,*/*");
                using var resp=await http.SendAsync(req);
                if(!resp.IsSuccessStatusCode) continue;
                string body=await resp.Content.ReadAsStringAsync();
                if(String.IsNullOrWhiteSpace(body)) continue;
                using var doc=JsonDocument.Parse(body);
                var root=doc.RootElement;
                if(root.ValueKind==JsonValueKind.Array && root.GetArrayLength()>0 && root[0].ValueKind==JsonValueKind.Array) {
                    var sb=new StringBuilder();
                    foreach(var part in root[0].EnumerateArray()) {
                        if(part.ValueKind==JsonValueKind.Array && part.GetArrayLength()>0 &&
                           part[0].ValueKind==JsonValueKind.String)
                            sb.Append(part[0].GetString());
                    }
                    string result=sb.ToString().Trim();
                    if(!String.IsNullOrWhiteSpace(result)) return PreserveTrailingSymbols(text,result);
                }
            } catch {}
        }

        // Network fallback: MyMemory needs an explicit source language, so infer the
        // writing system (and common Hungarian characters) instead of sending AUTO.
        // This keeps ordinary Translate mode useful when Google's unofficial endpoint
        // is blocked or throttled on a particular PC/network.
        try {
            string source=GuessSourceLanguage(text,target);
            if(!String.IsNullOrWhiteSpace(source) && !source.Equals(target,StringComparison.OrdinalIgnoreCase)) {
                string pair=source+"|"+target;
                string url="https://api.mymemory.translated.net/get?q="+Uri.EscapeDataString(text)+
                           "&langpair="+Uri.EscapeDataString(pair);
                using var req=new HttpRequestMessage(HttpMethod.Get,url);
                req.Headers.TryAddWithoutValidation("User-Agent","Mozilla/5.0 WindowsWritingAssistant/9.8.8");
                using var resp=await http.SendAsync(req);
                if(resp.IsSuccessStatusCode) {
                    using var doc=JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if(doc.RootElement.TryGetProperty("responseData",out var data) &&
                       data.TryGetProperty("translatedText",out var tr) && tr.ValueKind==JsonValueKind.String) {
                        string result=System.Net.WebUtility.HtmlDecode(tr.GetString()??"").Trim();
                        if(!String.IsNullOrWhiteSpace(result) &&
                           !result.Contains("INVALID SOURCE LANGUAGE",StringComparison.OrdinalIgnoreCase))
                            return PreserveTrailingSymbols(text,result);
                    }
                }
            }
        } catch {}

        // Never insert an API error into the user's document.
        return text;
    }

    static string PreserveTrailingSymbols(string source,string translated) {
        // Preserve the *meaning* of the user's ending punctuation instead of blindly
        // gluing the original characters after the translator's own punctuation.
        // Example: "It works!!" -> Japanese "動作します！！", never "動作します。!!".
        if(String.IsNullOrEmpty(source) || String.IsNullOrEmpty(translated)) return translated;

        int i=source.Length-1;
        while(i>=0 && !Char.IsLetterOrDigit(source[i]) && !Char.IsWhiteSpace(source[i])) i--;
        string tail=source[(i+1)..];
        if(String.IsNullOrEmpty(tail)) return translated;

        int bangs=tail.Count(c=>c=='!' || c=='！');
        int questions=tail.Count(c=>c=='?' || c=='？');
        bool hasPeriod=tail.Any(c=>c=='.' || c=='。' || c=='｡');

        // Keep non-sentence symbols/emoji separately.
        string extras=new string(tail.Where(c=>c!='!'&&c!='！'&&c!='?'&&c!='？'&&c!='.'&&c!='。'&&c!='｡').ToArray());

        // If the source explicitly ends in !/?/period, replace the translator's terminal
        // sentence punctuation with the same intent. Japanese/Chinese use full-width marks.
        if(bangs>0 || questions>0 || hasPeriod) {
            string core=translated.TrimEnd();
            while(core.Length>0 && (core[^1]=='!' || core[^1]=='！' || core[^1]=='?' || core[^1]=='？' ||
                                    core[^1]=='.' || core[^1]=='。' || core[^1]=='｡'))
                core=core[..^1].TrimEnd();

            bool cjk = translated.Any(c => (c>=0x3040&&c<=0x30FF) || (c>=0x3400&&c<=0x9FFF));
            var ending=new StringBuilder();
            if(questions>0) for(int n=0;n<questions;n++) ending.Append(cjk?'？':'?');
            if(bangs>0) for(int n=0;n<bangs;n++) ending.Append(cjk?'！':'!');
            if(questions==0 && bangs==0 && hasPeriod) ending.Append(cjk?'。':'.');

            if(!String.IsNullOrEmpty(extras) && !core.EndsWith(extras,StringComparison.Ordinal)) ending.Append(extras);
            return core+ending.ToString();
        }

        if(!String.IsNullOrEmpty(extras) && !translated.EndsWith(extras,StringComparison.Ordinal)) return translated+extras;
        return translated;
    }

    static string GuessSourceLanguage(string text,string target) {
        foreach(char c in text) {
            int u=c;
            if((u>=0x3040&&u<=0x30FF)) return "ja";
            if(u>=0xAC00&&u<=0xD7AF) return "ko";
            if(u>=0x4E00&&u<=0x9FFF) return target.StartsWith("ja",StringComparison.OrdinalIgnoreCase)?"zh-CN":"zh-CN";
            if(u>=0x0400&&u<=0x04FF) return "ru";
            if(u>=0x0370&&u<=0x03FF) return "el";
            if(u>=0x0590&&u<=0x05FF) return "he";
            if(u>=0x0600&&u<=0x06FF) return "ar";
            if(u>=0x0E00&&u<=0x0E7F) return "th";
        }
        if(text.IndexOfAny(new[]{'ő','Ő','ű','Ű','á','Á','é','É','í','Í','ó','Ó','ö','Ö','ü','Ü'})>=0) return "hu";
        return "en";
    }}
