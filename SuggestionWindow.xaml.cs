using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WindowsWritingAssistant;
public partial class SuggestionWindow : Window
{
    public string Suggestion { get; private set; } = "";
    string normal="", polite="", welcoming="";
    public SuggestionWindow()
    {
        InitializeComponent();
        Topmost = true;
        SourceInitialized += (_,__) => {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, -20);
            SetWindowLong(hwnd, -20, ex | 0x08000000 | 0x00000080);
        };
    }
    public void SetSuggestion(string s)
    {
        normal=polite=welcoming=s;
        NormalText.Text=PoliteText.Text=WelcomingText.Text=s;
        Suggestion=s;
        SelectMode(2);
        UpdateLayout();
    }
    public void SetSuggestions(string n,string p,string w,int selected=2)
    {
        normal=n; polite=p; welcoming=w;
        NormalText.Text=n; PoliteText.Text=p; WelcomingText.Text=w;
        SelectMode(selected);
        UpdateLayout();
    }
    public void SelectMode(int mode)
    {
        var clear=new SolidColorBrush(Color.FromArgb(0,0,0,0));
        var selected=new SolidColorBrush(Color.FromRgb(225,238,255));
        NormalRow.Background=clear; PoliteRow.Background=clear; WelcomingRow.Background=clear;
        if(mode==1){ NormalRow.Background=selected; Suggestion=normal; }
        else if(mode==3){ WelcomingRow.Background=selected; Suggestion=welcoming; }
        else { PoliteRow.Background=selected; Suggestion=polite; }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(System.IntPtr h,int i);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(System.IntPtr h,int i,int v);
}
