using System.Windows;
namespace WindowsWritingAssistant;
public partial class OcrResultWindow : Window {
 public OcrResultWindow(string original,string translated,string language){InitializeComponent();Original.Text=original;Translated.Text=translated;LanguageLine.Text="Translated automatically to Windows language: "+language;}
 void Copy_Click(object s,RoutedEventArgs e){try{Clipboard.SetText(Translated.Text);}catch{}}
 void Close_Click(object s,RoutedEventArgs e)=>Close();
}
