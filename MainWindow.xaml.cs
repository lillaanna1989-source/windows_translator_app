using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WindowsWritingAssistant;
public partial class MainWindow : Window
{
    private WindowsWritingAssistantService? helper;
    private readonly LiveScreenTranslator liveScreen=new();
    private readonly LiveMicrophoneTranscriber liveMic=new();
    public ObservableCollection<TranslationLogItem> Logs { get; } = new();

    public MainWindow(){ InitializeComponent(); liveScreen.StatusChanged+=message=>Dispatcher.BeginInvoke(()=>{LiveStatus.Text=message;LiveStatusTop.Text=message;}); liveMic.Status+=message=>Dispatcher.BeginInvoke(()=>{LiveStatus.Text=message;LiveStatusTop.Text=message;}); liveMic.Transcript+=line=>Dispatcher.BeginInvoke(()=>{TranscriptBox.AppendText(line+Environment.NewLine);TranscriptBox.ScrollToEnd();}); TranslationLog.ItemsSource=Logs; TextAssistant.TranslationCompleted += OnTranslationCompleted; Closing += (_,e)=>{e.Cancel=true;Hide();}; ShowPanel(HomePanel); }
    private string CurrentMode(){ if(ModeSelect?.SelectedItem is ComboBoxItem i)return i.Content?.ToString()??"Translate"; return "Translate"; }
    private AssistantOptions Options()=>new(){Translate=CurrentMode()=="Translate",Grammar=CurrentMode()=="Grammar",FixAll=true,TargetLanguage=String.IsNullOrWhiteSpace(TargetLanguage?.Text)?"English":TargetLanguage.Text.Trim()};
    private void ShowPanel(UIElement panel){ foreach(var p in new UIElement[]{HomePanel,TranslatePanel,GrammarPanel,WritingPanel,LogPanel,SettingsPanel,AboutPanel,LivePanel}) p.Visibility=p==panel?Visibility.Visible:Visibility.Collapsed; }
    private void ModeSelect_SelectionChanged(object s,SelectionChangedEventArgs e){
        // Page navigation/selection is UI-only. The running Helper mode changes only
        // when Enable is pressed (or with the global Ctrl+Alt+1/2 shortcuts).
    }
    private void EnableCurrent(){try{helper??=new WindowsWritingAssistantService();helper.Enable(Options());Status.Text="● Windows Writing Assistant enabled — "+CurrentMode();Hide();}catch(Exception ex){Status.Text="○ Could not enable";MessageBox.Show(ex.Message,"Windows Writing Assistant");}}
    private void EnableTranslate_Click(object s,RoutedEventArgs e){ModeSelect.SelectedIndex=1;EnableCurrent();}
    private void EnableGrammar_Click(object s,RoutedEventArgs e){ModeSelect.SelectedIndex=0;EnableCurrent();}
    private void Disable_Click(object s,RoutedEventArgs e){helper?.Disable();Status.Text="○ Windows Writing Assistant disabled";}
    private void Close_Click(object s,RoutedEventArgs e)=>Hide();
    public void ShowTranslationLanguagePicker(){ShowPanel(TranslatePanel);ModeSelect.SelectedIndex=1;if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Show();Activate();Topmost=true;Topmost=false;TargetLanguage.Focus();TargetLanguage.SelectAll();}
    private void TargetLanguage_TextChanged(object s,TextChangedEventArgs e){if(LanguageCorrectionBox==null||LanguageCorrectionText==null||TargetLanguage==null)return;string raw=TargetLanguage.Text.Trim();LanguageCorrectionText.Inlines.Clear();if(String.IsNullOrWhiteSpace(raw)){LanguageCorrectionBox.Visibility=Visibility.Collapsed;return;}if(TextAssistant.TryResolveLanguage(raw,out string name,out string code,true)){bool exact=name.Equals(raw,StringComparison.OrdinalIgnoreCase)||code.Equals(raw,StringComparison.OrdinalIgnoreCase);if(exact){LanguageCorrectionText.Inlines.Add(new Run(raw){FontStyle=FontStyles.Italic,Foreground=Brushes.Goldenrod});LanguageCorrectionText.Inlines.Add(new Run(" Is right!"));}else{string sh=TextAssistant.FriendlyShortCode(name,code);LanguageCorrectionText.Inlines.Add(new Run("Did you mean... "));LanguageCorrectionText.Inlines.Add(new Run(name){FontStyle=FontStyles.Italic,Foreground=Brushes.Goldenrod});LanguageCorrectionText.Inlines.Add(new Run("? (Or "));LanguageCorrectionText.Inlines.Add(new Run(sh){FontStyle=FontStyles.Italic,Foreground=Brushes.Goldenrod});LanguageCorrectionText.Inlines.Add(new Run(")"));}LanguageCorrectionBox.Visibility=Visibility.Visible;}else{LanguageCorrectionText.Inlines.Add(new Run("Language not recognized yet"));LanguageCorrectionBox.Visibility=Visibility.Visible;}helper?.UpdateTargetLanguage(TargetLanguage.Text);}
    private SmartReplaceOverlay? diagnosticCaption;
    private void TestCaption_Click(object s,RoutedEventArgs e){
      diagnosticCaption?.Close();
      var area=SystemParameters.WorkArea;
      diagnosticCaption=new SmartReplaceOverlay(new Rect(area.Right-320,area.Top+90,300,75),"WWA test caption — overlay works!");
      diagnosticCaption.Show();
      LiveStatusTop.Text="Test caption shown at top-right. If you see it, the overlay works; OCR/translation is the next issue.";
    }
    private void LiveNav_Click(object s,RoutedEventArgs e)=>ShowPanel(LivePanel);
    private string LiveTargetName() => (LiveTarget.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "English";
    private string[] KnownLanguageNames() => (KnownLanguages.Text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private void LiveFullScreen_Click(object s,RoutedEventArgs e) => StartFullScreen();
    private void StartFullScreen(){
       LiveStatusTop.Text="Start clicked — initializing screen capture...";
       diagnosticCaption?.Close();diagnosticCaption=null;
       liveScreen.Start(new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,
           SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight),LiveTargetName(),KnownLanguageNames(),OnlyForeign.IsChecked==true,true);
    }
    public void ToggleLiveSelection(){if(liveScreen.IsActive){liveScreen.Stop();return;}StartFullScreen();}
    private void StartLiveSelectionAsync(){
       var picker=new ScreenCaptureOverlay();picker.ShowDialog();
       if(!picker.Accepted)return;
       liveScreen.Start(picker.SelectedScreenRect,LiveTargetName(),KnownLanguageNames(),OnlyForeign.IsChecked==true,false);
    }
    private void LiveStart_Click(object s,RoutedEventArgs e)=>StartFullScreen();
    private void LiveStop_Click(object s,RoutedEventArgs e){liveScreen.Stop();diagnosticCaption?.Close();diagnosticCaption=null;}
    private void MicStart_Click(object s,RoutedEventArgs e){try{liveMic.Start();}catch(Exception ex){LiveStatus.Text="Microphone unavailable: "+ex.Message;}}
    private void MicStop_Click(object s,RoutedEventArgs e)=>liveMic.Stop();
    private void Home_Click(object s,RoutedEventArgs e)=>ShowPanel(HomePanel);
    private void TranslateNav_Click(object s,RoutedEventArgs e){ShowPanel(TranslatePanel);ModeSelect.SelectedIndex=1;TargetLanguage.Focus();}
    private void GrammarNav_Click(object s,RoutedEventArgs e){ShowPanel(GrammarPanel);ModeSelect.SelectedIndex=0;}
    private void WritingHelp_Click(object s,RoutedEventArgs e)=>ShowPanel(WritingPanel);
    private void Log_Click(object s,RoutedEventArgs e)=>ShowPanel(LogPanel);
    private void Settings_Click(object s,RoutedEventArgs e)=>ShowPanel(SettingsPanel);
    private void About_Click(object s,RoutedEventArgs e)=>ShowPanel(AboutPanel);
    private void ClearLog_Click(object s,RoutedEventArgs e)=>Logs.Clear();
    private void OnTranslationCompleted(string original,string translated,string to){Dispatcher.BeginInvoke(()=>{Logs.Insert(0,new TranslationLogItem{Time=DateTime.Now.ToString("HH:mm:ss"),To=to,Original=original,Translation=translated});while(Logs.Count>100)Logs.RemoveAt(Logs.Count-1);});}
}
public sealed class TranslationLogItem{public string Time{get;set;}="";public string To{get;set;}="";public string Original{get;set;}="";public string Translation{get;set;}="";}
