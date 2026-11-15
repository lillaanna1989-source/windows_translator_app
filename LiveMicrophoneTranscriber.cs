using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
namespace WindowsWritingAssistant;
public sealed class LiveMicrophoneTranscriber : IDisposable {
 Process? process;
 string? script;
 public bool IsActive=>process is {HasExited:false};
 public event Action<string>? Transcript;
 public event Action<string>? Status;
 public void Start(){
  if(IsActive)return;
  script=Path.Combine(Path.GetTempPath(),"WWA_SPEECH_"+Guid.NewGuid().ToString("N")+".ps1");
  File.WriteAllText(script,"""
  $ErrorActionPreference='Stop'
  Add-Type -AssemblyName System.Speech
  $r=New-Object System.Speech.Recognition.SpeechRecognitionEngine
  $r.SetInputToDefaultAudioDevice()
  $r.LoadGrammar((New-Object System.Speech.Recognition.DictationGrammar))
  [Console]::OutputEncoding=[Text.Encoding]::UTF8
  while($true) {
    $result=$r.Recognize([TimeSpan]::FromSeconds(2))
    if($null -ne $result -and $result.Confidence -ge 0.40) { [Console]::WriteLine($result.Text); [Console]::Out.Flush() }
  }
  """,Encoding.UTF8);
  var info=new ProcessStartInfo("powershell.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  info.ArgumentList.Add("-NoProfile");info.ArgumentList.Add("-NonInteractive");info.ArgumentList.Add("-File");info.ArgumentList.Add(script);
  process=new Process {StartInfo=info};process.Start();
  _=Task.Run(async ()=>{try {while(process!=null&&!process.HasExited){var line=await process.StandardOutput.ReadLineAsync();if(line==null)break;if(line.Length>0)Transcript?.Invoke(line);}}catch{} });
  _=Task.Run(async ()=>{try {string err=await process.StandardError.ReadToEndAsync();if(!string.IsNullOrWhiteSpace(err))Status?.Invoke(err.Trim());}catch{} });
  Status?.Invoke("Microphone listening (Windows speech recognition)");
 }
 public void Stop(){try{if(process is {HasExited:false})process.Kill(entireProcessTree:true);}catch{}process?.Dispose();process=null;if(script!=null){try{File.Delete(script);}catch{}script=null;}Status?.Invoke("Microphone transcription stopped");}
 public void Dispose()=>Stop();
}
