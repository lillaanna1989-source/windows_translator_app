using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;

namespace WindowsWritingAssistant;
/// <summary>Best-effort exclusion for native images exposed through UI Automation.
/// Browser canvas/screenshots not exposed as Image controls cannot be distinguished by OCR alone.</summary>
public static class ScreenImageExclusion {
 public static List<Rect> GetImageBounds() {
  var result=new List<Rect>();
  try {
   var images=AutomationElement.RootElement.FindAll(TreeScope.Descendants,
     new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Image));
   for(int i=0;i<images.Count && i<300;i++) {
    var r=images[i].Current.BoundingRectangle;
    if(!r.IsEmpty && r.Width>75 && r.Height>40 && r.Width<3000 && r.Height<2500)
      result.Add(r);
   }
  }catch{} // Some apps do not expose their image controls.
  return result;
 }
 public static bool IsInsideImage(Rect text, IReadOnlyList<Rect> images) {
  foreach(var image in images){var overlap=Rect.Intersect(text,image);
   if(!overlap.IsEmpty && overlap.Width*overlap.Height >= text.Width*text.Height*0.75)return true;
  }
  return false;
 }
}
