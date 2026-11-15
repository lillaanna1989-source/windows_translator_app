using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsWritingAssistant;
public partial class StatusPopup : Window
{
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(1300)};
    public StatusPopup()
    {
        InitializeComponent();
        timer.Tick+=(_,__)=>{timer.Stop();Hide();};
        SourceInitialized+=(_,__)=>{
            var h=new WindowInteropHelper(this).Handle;
            int ex=GetWindowLong(h,-20);
            SetWindowLong(h,-20,ex|0x08000000|0x00000080);
        };
    }
    public void Flash(bool enabled)
    {
        ShowTemporary(enabled ? "Windows Writing Assistant enabled" : "Windows Writing Assistant disabled",
                      enabled ? Brushes.MediumSeaGreen : Brushes.Gray, 1300);
    }

    // Processing status is UI-only: it never becomes part of the text/suggestion pipeline.
    public void ShowWorking(bool translating)
    {
        timer.Stop();
        Message.Text = translating ? "WWA is translating…" : "WWA is checking grammar…";
        Dot.Fill = Brushes.DodgerBlue;
        PlaceAndShow();
    }

    public void ShowNoChanges()
    {
        ShowTemporary("No changes needed ✓", Brushes.MediumSeaGreen, 900);
    }

    public void Dismiss()
    {
        timer.Stop();
        Hide();
    }

    void ShowTemporary(string message, Brush dot, int milliseconds)
    {
        Message.Text=message;
        Dot.Fill=dot;
        PlaceAndShow();
        timer.Interval=TimeSpan.FromMilliseconds(milliseconds);
        timer.Stop(); timer.Start();
    }

    void PlaceAndShow()
    {
        Left=SystemParameters.WorkArea.Right-Width-18;
        Top=SystemParameters.WorkArea.Bottom-Height-18;
        if(!IsVisible) Show();
        Topmost=true;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int i);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int i,int v);
}
