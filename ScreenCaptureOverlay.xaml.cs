using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace WindowsWritingAssistant;
public partial class ScreenCaptureOverlay : Window {
    System.Windows.Point start; bool dragging;
    public Rect SelectedScreenRect { get; private set; }
    public bool Accepted { get; private set; }
    public ScreenCaptureOverlay() {
        InitializeComponent();
        Left=SystemParameters.VirtualScreenLeft; Top=SystemParameters.VirtualScreenTop;
        Width=SystemParameters.VirtualScreenWidth; Height=SystemParameters.VirtualScreenHeight;
        Loaded += (_,__)=>{ Activate(); Focus(); };
    }
    void Down(object s,System.Windows.Input.MouseButtonEventArgs e){ start=e.GetPosition(Surface); dragging=true; Selection.Visibility=Visibility.Visible; Surface.CaptureMouse(); }
    void Move(object s,System.Windows.Input.MouseEventArgs e){ if(!dragging)return; Draw(e.GetPosition(Surface)); }
    void Up(object s,System.Windows.Input.MouseButtonEventArgs e){
        if(!dragging)return; dragging=false; Surface.ReleaseMouseCapture(); var p=e.GetPosition(Surface); Draw(p);
        double x=Math.Min(start.X,p.X), y=Math.Min(start.Y,p.Y), w=Math.Abs(p.X-start.X), h=Math.Abs(p.Y-start.Y);
        if(w<8||h<8){ Close(); return; }
        SelectedScreenRect=new Rect(x+Left,y+Top,w,h); Accepted=true; Close();
    }
    void Draw(System.Windows.Point p){ double x=Math.Min(start.X,p.X),y=Math.Min(start.Y,p.Y); Canvas.SetLeft(Selection,x);Canvas.SetTop(Selection,y);Selection.Width=Math.Abs(p.X-start.X);Selection.Height=Math.Abs(p.Y-start.Y); }
    void Keys(object s,System.Windows.Input.KeyEventArgs e){ if(e.Key==Key.Escape){Accepted=false;Close();} }
}
