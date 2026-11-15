using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
namespace WindowsWritingAssistant;
public sealed class SmartReplaceOverlay : Window {
 [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int index,int value);
 [DllImport("user32.dll", SetLastError=true)] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
 public bool ExcludedFromCapture { get; private set; }
 public Rect ScreenRegion { get; private set; }
 readonly TextBlock caption;
 public void Update(Rect rect, string translation) {
  ScreenRegion=rect; Left=rect.Left; Top=rect.Top; Width=Math.Max(180,rect.Width); Height=Math.Max(42,rect.Height);
  if(caption.Text!=translation)caption.Text=translation;
 }
 public SmartReplaceOverlay(Rect rect,string translation) {
  ScreenRegion=rect;
  WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
  Background=Brushes.Transparent;ShowInTaskbar=false;Topmost=true;ShowActivated=false;
  Left=rect.Left;Top=rect.Top;Width=Math.Max(180,rect.Width);Height=Math.Max(42,rect.Height);
  // Text-only subtitles: no visible window chrome, background, border or panel.
  // A soft shadow keeps white text legible without obscuring the original app.
  caption=new TextBlock {
    Text=translation,
    TextWrapping=TextWrapping.Wrap,
    Foreground=new SolidColorBrush(Color.FromArgb(225, 190, 194, 202)),
    FontSize=15,
    FontWeight=FontWeights.Medium,
    // Do not truncate long translated paragraphs.
    MaxHeight=double.PositiveInfinity,
    Effect=new System.Windows.Media.Effects.DropShadowEffect {
      Color=Colors.Black, BlurRadius=4, ShadowDepth=1, Opacity=0.75
    }
  };
  Content=caption;IsHitTestVisible=false;
  SourceInitialized+=(_,__)=>{var h=new WindowInteropHelper(this).Handle;int ex=GetWindowLong(h,-20);SetWindowLong(h,-20,ex|0x20|0x08000000|0x80);ExcludedFromCapture=SetWindowDisplayAffinity(h,0x11);};
 }
}
