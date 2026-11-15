using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
namespace WindowsWritingAssistant;
public sealed class LiveScreenTranslator : IDisposable {
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2.5)};
 readonly Dictionary<string,(string translation,string source)> cache=new();
 readonly List<SmartReplaceOverlay> overlays=new();
 readonly Dictionary<string,SmartReplaceOverlay> stableOverlays=new();
 readonly Dictionary<string,Rect> sourcePositions=new();
 readonly Dictionary<string,int> missingScans=new();
 readonly Dictionary<string,string> captionText=new();
 Rect region;bool active,busy,skipKnown;long generation;
 string target="English";string[] known=Array.Empty<string>();
 public event Action<string>? StatusChanged;
 public bool IsActive=>active;
 public LiveScreenTranslator(){timer.Tick+=async (_,__)=>await RefreshAsync();}
 public void Start(Rect selected,string language,string[] knownLanguages,bool skip,bool wholeScreen){
  Stop();region=selected;target=language;known=knownLanguages;skipKnown=skip;
  active=true;generation++;timer.Start();StatusChanged?.Invoke("LIVE started — scanning the entire screen. Keep this window open to see OCR/translation errors.");_=RefreshAsync();
 }
 public void Stop(){active=false;generation++;timer.Stop();ClearOverlays();StatusChanged?.Invoke("Live screen stopped");}
 void ClearOverlays(){foreach(var o in overlays){try{o.Close();}catch{}}overlays.Clear();stableOverlays.Clear();sourcePositions.Clear();missingScans.Clear();captionText.Clear();}
 async Task RefreshAsync(){
  if(!active||busy)return;busy=true;long run=generation;
  string png=Path.Combine(Path.GetTempPath(),"WWA_live_"+Guid.NewGuid().ToString("N")+".png");
  try {
   // Never OCR our own captions: hide all before capture.
   foreach(var o in overlays)if(!o.ExcludedFromCapture)o.Opacity=0;
   try {
    await Task.Delay(75);
    ScreenOcrTranslator.SaveScreenRegionAsPng(region,png);
   } finally {foreach(var o in overlays)if(!o.ExcludedFromCapture)o.Opacity=1;}
   StatusChanged?.Invoke("Screen captured — recognizing text (up to 18 seconds)...");
   var lines=await LiveOcrLines.ReadAsync(png);
   if(!active||run!=generation)return;
   if(lines.Count==0){ReconcileCaptions(new List<(string key,string text,Rect rect,Rect source)>());StatusChanged?.Invoke("No text recognized — removing captions after two confirmed empty scans.");return;}
   lines=GroupParagraphs(lines);
   StatusChanged?.Invoke($"OCR found {lines.Count} lines — translating...");
   var knownCodes=known.Select(LiveTranslationEngine.LanguageCode).Where(c=>c.Length>0).ToHashSet(StringComparer.OrdinalIgnoreCase);
   knownCodes.Add(LiveTranslationEngine.LanguageCode(target));
   var translated=new List<(string key,string text,Rect rect,Rect source)>();int errors=0,skipped=0,filtered=0,imagesSkipped=0;
   // UI Automation image bounds are screen pixels; the OCR line bounds are capture-relative pixels.
   var imageBounds=await Task.Run(ScreenImageExclusion.GetImageBounds);
   // Limit requests per scan; cache makes subsequent scans much cheaper.
   foreach(var line in lines.Take(60)){
    if(!active||run!=generation)return;
    if(!ScreenTextFilter.ShouldTranslate(line.Text)){filtered++;continue;}
    var sourcePixels=new Rect(region.Left+line.Bounds.Left,region.Top+line.Bounds.Top,line.Bounds.Width,line.Bounds.Height);
    if(ScreenImageExclusion.IsInsideImage(sourcePixels,imageBounds)){imagesSkipped++;continue;}
    string key=target+"|"+line.Text;
    if(!cache.TryGetValue(key,out var result)){
     try{result=await LiveTranslationEngine.TranslateAsync(line.Text,target);if(cache.Count>300)cache.Clear();cache[key]=result;}
     catch(Exception ex){errors++;StatusChanged?.Invoke("Translation failed (will retry next scan): "+ex.Message);continue;}
    }
    if(skipKnown && result.source!="unknown" && knownCodes.Contains(result.source)){skipped++;continue;}
    if(string.IsNullOrWhiteSpace(result.translation)||string.Equals(line.Text,result.translation,StringComparison.OrdinalIgnoreCase)){skipped++;continue;}
    // Keep the caption adjacent to the *source block*, never in an unrelated
    // distant column or at the browser title bar. All WPF positions are DIPs.
    var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(Application.Current.MainWindow);
    double sx=dpi.DpiScaleX, sy=dpi.DpiScaleY;
    var source=new Rect(sourcePixels.X/sx,sourcePixels.Y/sy,sourcePixels.Width/sx,sourcePixels.Height/sy);
    double screenLeft=SystemParameters.VirtualScreenLeft/sx;
    double screenRight=(SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth)/sx;
    double screenTop=SystemParameters.VirtualScreenTop/sy;
    double screenBottom=(SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight)/sy;
    if(source.Top<screenTop+35 || source.Bottom>screenBottom-18)continue;
    // Size from translated content rather than a fixed 180px height cap.
    // Use a wider readable gutter and enough height for the *entire* paragraph.
    double width=Math.Clamp(result.translation.Length*4.8+28,210,320);
    double charsPerLine=Math.Max(18,(width-12)/7.6);
    double height=Math.Max(42,Math.Ceiling(result.translation.Length/charsPerLine)*21+12);
    double gap=14;
    // Only use a nearby gutter. Do not send a caption across the entire page.
    double x=source.Right+gap;
    if(x+width>screenRight-24 || x-source.Right>40){
      x=source.Left-width-gap;
      if(x<screenLeft+24)continue;
    }
    // Do not detach a caption vertically: hide it if it cannot fit alongside its source.
    if(height>screenBottom-screenTop-60 || source.Top+height>screenBottom-12)continue;
    double y=source.Top;
    var candidate=new Rect(x,y,width,height);
    // Never shift a translation far away vertically to avoid a collision.
    // Instead omit it this scan; it can appear when space becomes available.
    if(translated.Any(t=>t.rect.IntersectsWith(candidate)))continue;
    translated.Add((key,result.translation,candidate,source));
   }
   if(!active||run!=generation)return;
   ReconcileCaptions(translated);
   StatusChanged?.Invoke($"Detected {lines.Count} lines · showing {translated.Count} text-only captions · ignored labels/names {filtered} · image text skipped {imagesSkipped} · skipped {skipped} · errors {errors}");
  } catch(Exception ex){if(active)StatusChanged?.Invoke("Live scan error: "+ex.Message);}
  finally{try{File.Delete(png);}catch{}if(active && run==generation)foreach(var o in overlays)if(!o.ExcludedFromCapture)o.Opacity=1;busy=false;}
 }
 // Merge wrapped OCR lines belonging to the same paragraph/message.
 // Avoid combining different columns or distant labels.
 static List<LiveOcrLine> GroupParagraphs(List<LiveOcrLine> input){
  var ordered=input.OrderBy(l=>l.Bounds.Top).ThenBy(l=>l.Bounds.Left).ToList();
  var result=new List<LiveOcrLine>();
  foreach(var line in ordered){
   int match=-1; double best=double.MaxValue;
   for(int i=0;i<result.Count;i++){
    var prev=result[i];
    double gap=line.Bounds.Top-prev.Bounds.Bottom;
    double leftDifference=Math.Abs(line.Bounds.Left-prev.Bounds.Left);
    double overlap=Math.Max(0,Math.Min(line.Bounds.Right,prev.Bounds.Right)-Math.Max(line.Bounds.Left,prev.Bounds.Left));
    bool sameColumn=leftDifference<Math.Max(30,prev.Bounds.Height*2.5) && overlap>Math.Min(line.Bounds.Width,prev.Bounds.Width)*0.30;
    if(gap>=-2 && gap<=Math.Max(19,line.Bounds.Height*1.5) && sameColumn && gap<best && prev.Text.Length+line.Text.Length<800){match=i;best=gap;}
   }
   if(match<0){result.Add(line);continue;}
   var old=result[match];
   result[match]=new LiveOcrLine(old.Text.TrimEnd()+" "+line.Text.TrimStart(),Rect.Union(old.Bounds,line.Bounds));
  }
  return result;
 }
 // Track the same source across scrolling. Identity is text + closest source geometry;
 // never let two identical messages share a single overlay.
 void ReconcileCaptions(List<(string key,string text,Rect rect,Rect source)> found){
  var oldIds=stableOverlays.Keys.ToArray();
  var unused=new HashSet<string>(oldIds,StringComparer.Ordinal);
  var seen=new HashSet<string>(StringComparer.Ordinal);
  int sequence=0;
  foreach(var item in found){
   // Prefer a matching text near its previous position, but allow large vertical
   // movement when the page scrolls. Horizontal column must remain stable.
   string? match=unused.Where(id=>captionText.TryGetValue(id,out var t) && t==item.text &&
       sourcePositions.TryGetValue(id,out var r) && Math.Abs(r.Left-item.source.Left)<65)
     .OrderBy(id=>Math.Abs(sourcePositions[id].Top-item.source.Top)).FirstOrDefault();
   if(match==null){
    // Stable new identity; the sequence disambiguates duplicate text on one scan.
    match="caption:"+(++sequence)+":"+Guid.NewGuid().ToString("N");
   }else unused.Remove(match);
   seen.Add(match);
   sourcePositions[match]=item.source;captionText[match]=item.text;missingScans[match]=0;
   if(stableOverlays.TryGetValue(match,out var existing))existing.Update(item.rect,item.text);
   else {var overlay=new SmartReplaceOverlay(item.rect,item.text);stableOverlays[match]=overlay;overlays.Add(overlay);overlay.Show();}
  }
  foreach(var id in unused){
   int misses=missingScans.TryGetValue(id,out var n)?n+1:1;
   missingScans[id]=misses;
   if(misses<2)continue;
   var overlay=stableOverlays[id];stableOverlays.Remove(id);sourcePositions.Remove(id);
   captionText.Remove(id);missingScans.Remove(id);overlays.Remove(overlay);overlay.Close();
  }
 }
 public void Dispose()=>Stop();
}
