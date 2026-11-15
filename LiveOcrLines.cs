using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace WindowsWritingAssistant;
public sealed record LiveOcrLine(string Text, Rect Bounds);
public static class LiveOcrLines {
 public static async Task<List<LiveOcrLine>> ReadAsync(string imagePath) {
  // Use OCR word rectangles, not one giant OCR paragraph. Coordinates are relative to capture.
  string script = @"
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
function A($o, [type]$resultType) {
  # PowerShell cannot infer the generic WinRT AsTask<T> overload.
  $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and
    $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
  } | Select-Object -First 1
  if ($null -eq $method) { throw 'Cannot locate WinRT AsTask<T> overload.' }
  $task = $method.MakeGenericMethod($resultType).Invoke($null, @($o))
  $task.GetAwaiter().GetResult()
}
$f=A ([Windows.Storage.StorageFile]::GetFileFromPathAsync($args[0])) ([Windows.Storage.StorageFile])
$s=A ($f.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
try {
$d=A ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($s)) ([Windows.Graphics.Imaging.BitmapDecoder])
$b=A ($d.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
$e=[Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if($null -eq $e){throw 'Windows OCR unavailable. Install the appropriate OCR language pack.'}
$r=A ($e.RecognizeAsync($b)) ([Windows.Media.Ocr.OcrResult])
$lines=@(foreach($line in $r.Lines){
$words=@($line.Words)
if($words.Count -eq 0){continue}
$x=($words | ForEach-Object {$_.BoundingRect.X} | Measure-Object -Minimum).Minimum
$y=($words | ForEach-Object {$_.BoundingRect.Y} | Measure-Object -Minimum).Minimum
$right=($words | ForEach-Object {$_.BoundingRect.X+$_.BoundingRect.Width} | Measure-Object -Maximum).Maximum
$bottom=($words | ForEach-Object {$_.BoundingRect.Y+$_.BoundingRect.Height} | Measure-Object -Maximum).Maximum
[pscustomobject]@{text=$line.Text;x=[double]$x;y=[double]$y;w=[double]($right-$x);h=[double]($bottom-$y)}
})
[Console]::OutputEncoding=[Text.Encoding]::UTF8
ConvertTo-Json -InputObject $lines -Compress -Depth 4
} finally {$s.Dispose()}
";
  string scriptPath=Path.Combine(Path.GetTempPath(),"WWA_ocr_"+Guid.NewGuid().ToString("N")+".ps1");
  try {
   await File.WriteAllTextAsync(scriptPath,script,Encoding.UTF8);
   var si=new ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8 };
   si.ArgumentList.Add("-NoProfile");si.ArgumentList.Add("-NonInteractive");si.ArgumentList.Add("-ExecutionPolicy");si.ArgumentList.Add("Bypass");si.ArgumentList.Add("-File");si.ArgumentList.Add(scriptPath);si.ArgumentList.Add(imagePath);
   using var proc=Process.Start(si) ?? throw new InvalidOperationException("Cannot launch Windows OCR");
   var output=proc.StandardOutput.ReadToEndAsync();var errors=proc.StandardError.ReadToEndAsync();
   using var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(18));
   try{await proc.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){try{proc.Kill(true);}catch{}throw new TimeoutException("Windows OCR timed out after 18 seconds. Check OCR language packs and PowerShell permissions.");}
   string json=await output;string err=await errors;
   if(proc.ExitCode!=0)throw new InvalidOperationException("OCR failed: "+err.Trim());
   var lines=new List<LiveOcrLine>();
   using var doc=JsonDocument.Parse(string.IsNullOrWhiteSpace(json)?"[]":json);
   if(doc.RootElement.ValueKind!=JsonValueKind.Array)return lines;
   foreach(var el in doc.RootElement.EnumerateArray()) {
    string t=el.GetProperty("text").GetString()?.Trim()??"";
    if(t.Length<3)continue;
    double x=el.GetProperty("x").GetDouble(),y=el.GetProperty("y").GetDouble();
    double w=el.GetProperty("w").GetDouble(),h=el.GetProperty("h").GetDouble();
    if(w>0 && h>0)lines.Add(new(t,new Rect(x,y,w,h)));
   }
   return lines;
  } finally {try{File.Delete(scriptPath);}catch{}}
 }
}
