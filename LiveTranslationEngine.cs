using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
namespace WindowsWritingAssistant;
public static class LiveTranslationEngine {
 static readonly HttpClient client=new(){Timeout=TimeSpan.FromSeconds(12)};
 public static string LanguageCode(string language){
   if(TextAssistant.TryResolveLanguage(language,out _,out var code,false))return code.Split('-')[0].ToLowerInvariant();
   return "";
 }
 public static async Task<(string translation,string source)> TranslateAsync(string text,string language){
   string target=LanguageCode(language);
   if(target.Length==0)throw new ArgumentException("Choose a supported target language.");
   // Use exactly the existing Text to Translate engine for the translated words.
   // The metadata request below is only for source-language detection; its failure
   // must never prevent a successful translation from being displayed.
   string translated=await TextAssistant.TranslateForOcrAsync(text,language);
   if(string.IsNullOrWhiteSpace(translated) || string.Equals(translated.Trim(),text.Trim(),StringComparison.OrdinalIgnoreCase))
     throw new InvalidOperationException("The shared translator returned unchanged text. Retrying on the next scan; check connectivity and source language.");
   string source="unknown";
   try {
     string url="https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl="+
       Uri.EscapeDataString(target)+"&dt=t&q="+Uri.EscapeDataString(text);
     using var response=await client.GetAsync(url);
     response.EnsureSuccessStatusCode();
     using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
     var root=doc.RootElement;
     if(root.ValueKind==JsonValueKind.Array && root.GetArrayLength()>2 && root[2].ValueKind==JsonValueKind.String)
       source=(root[2].GetString()??"unknown").Split('-')[0].ToLowerInvariant();
   } catch { /* Do not discard an otherwise successful translation. */ }
   return (translated,source);
 }
}
