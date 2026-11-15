using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace WindowsWritingAssistant;

/// <summary>Conservative OCR-line filter: avoids translating names, handles, and short UI labels.
/// Heuristic only: OCR has no semantic knowledge of which app element is dialogue.</summary>
public static class ScreenTextFilter {
 static readonly Regex Words = new(@"[\p{L}]+(?:['’][\p{L}]+)?", RegexOptions.Compiled);
 static readonly Regex UiOnly = new(@"^(?:home|settings|profile|search|menu|login|log in|sign up|follow|following|share|like|reply|send|edit|delete|cancel|save|back|next|skip|close|new chat|chats|messages|notifications|characters|explore|translate|translation|language|copy|paste|report|block|online|offline)$", RegexOptions.IgnoreCase|RegexOptions.Compiled);
 static readonly Regex Handle = new(@"(?:^|\s)@\w+|https?://|www\.|\S+@\S+\.\S+",RegexOptions.IgnoreCase|RegexOptions.Compiled);
 static readonly Regex SentenceMarker = new(@"[.!?…。！？¿¡؛؟:]",RegexOptions.Compiled);
 public static bool ShouldTranslate(string? text) {
  if(string.IsNullOrWhiteSpace(text))return false;
  var s=text.Trim();
  if(s.Length<4 || s.Length>600 || UiOnly.IsMatch(s) || Handle.IsMatch(s))return false;
  if(s.All(c=>!char.IsLetter(c)))return false;
  var words=Words.Matches(s).Select(m=>m.Value).ToArray();
  if(words.Length<2)return false; // names and one-word buttons
  // Name-like headings, e.g. 'Sofia Martinez' or 'The Royal Prince'.
  if(words.Length<=5 && !SentenceMarker.IsMatch(s) &&
     words.All(w=>char.IsUpper(w[0]) && w.Skip(1).All(c=>!char.IsLetter(c)||char.IsLower(c))))return false;
  // Short unpunctuated dialogue (e.g. "David hermoso") must remain eligible.
  return true;
 }
}
