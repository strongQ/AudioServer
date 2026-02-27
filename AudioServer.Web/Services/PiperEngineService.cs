using Audio.Core.Interfaces;
using Audio.Core.Options;
using Audio.Core.PInvoke;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Vosk;
using XT.Common.Services;

namespace AudioServer.Web.Services
{
    /// <summary>
    /// piper文本转语音
    /// </summary>
    public class PiperEngineService: IVoiceEngineService
    {
        private readonly VoskServerOption _voskOptions;
        private readonly ILogService _logService;

        private readonly Model _voskModel;
        private readonly string _piperExePath;
        private readonly string _piperModelPath ;
        private  string _outputWavPath;

        public PiperEngineService(IOptions<VoskServerOption> voskOptions, ILogService logService)
        {
            _voskOptions=voskOptions.Value;
            _logService=logService;

            _piperExePath = Path.Combine(AppContext.BaseDirectory, _voskOptions.PiperExePath);
            _piperModelPath = Path.Combine(AppContext.BaseDirectory, _voskOptions.PiperModelPath);
            _outputWavPath = Path.Combine(AppContext.BaseDirectory, _voskOptions.OutputWavPath);

            _voskModel = new Model(Path.Combine(AppContext.BaseDirectory, _voskOptions.ModelPath));

            // 检查 PiperExe 是否存在
            if (!File.Exists(_piperExePath))
            {
                _logService.LogError($"错误: Piper 可执行程序未找到！路径: {_piperExePath}");
            }

            // 检查 PiperModel 是否存在
            if (!File.Exists(_piperModelPath))
            {
                _logService.LogError($"错误: Piper 模型文件未找到！路径: {_piperModelPath}");
            }
        }

      

        /// <summary>
        /// 文字转语音
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public async Task<string> SpeakAsync(string text)
        {
            if (File.Exists(_outputWavPath))
            {
                try { File.Delete(_outputWavPath); }
                catch (Exception ex) { Console.WriteLine($"无法删除旧的 output.wav 文件: {ex.Message}"); }
            }

        
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _piperExePath,
                    Arguments = $"--model \"{_piperModelPath}\" --output_file \"{_outputWavPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // 【重要】明确指定进程启动时使用的编码，尽管主要控制在StreamWriter中
                    StandardInputEncoding = System.Text.Encoding.UTF8
                }
            };

            process.Start();

            // 【核心修正】
            // 1. 获取底层的 BaseStream。
            // 2. 创建一个新的 StreamWriter，包装该流，并【在构造函数中】指定 UTF-8 编码。
            // 3. 使用 await using 来确保 StreamWriter 在使用完毕后被正确释放和关闭。
            await using (var stdinWriter = new StreamWriter(process.StandardInput.BaseStream, System.Text.Encoding.UTF8))
            {
                // 4. AutoFlush 确保内容写入后能立即被对方进程读取，而不是被缓存
                stdinWriter.AutoFlush = true;
                // 5. 写入文本
                await stdinWriter.WriteLineAsync(text);
            }
            // 当 await using 代码块结束时，stdinWriter 会被自动关闭(Dispose)。
            // 关闭 StreamWriter 会自动关闭其包装的底层流(BaseStream)，
            // 这会向 Piper 发送“输入结束”的信号，触发它开始语音合成。

            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.WriteLine($"Piper TTS Error: {error}");
            }

            if (File.Exists(_outputWavPath))
            {
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Console.WriteLine("On Windows, using winmm.dll PlaySound API...");
                        WinmmAudioPlayer.PlayWavFile(_outputWavPath);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        var playerProcess = new Process();
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            Console.WriteLine("On Linux, using 'aplay' command...");
                            playerProcess.StartInfo.FileName = "aplay";
                            playerProcess.StartInfo.Arguments = $"-q \"{_outputWavPath}\""; // -q for quiet mode
                        }
                        else // macOS
                        {
                            Console.WriteLine("On macOS, using 'afplay' command...");
                            playerProcess.StartInfo.FileName = "afplay";
                            playerProcess.StartInfo.Arguments = $"\"{_outputWavPath}\"";
                        }

                        playerProcess.StartInfo.UseShellExecute = false;
                        playerProcess.StartInfo.CreateNoWindow = true;
                        playerProcess.Start();
                        await playerProcess.WaitForExitAsync();
                    }
                    else
                    {
                        Console.WriteLine("Error: Unsupported OS for audio playback.");
                    }
                    Console.WriteLine("Playback finished.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"播放 WAV 文件时出错: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Piper 未能成功生成 output.wav 文件。");
            }

            return "";
        }

        /// <summary>
        /// 语音识别
        /// </summary>
        /// <param name="durationSeconds"></param>
        /// <returns></returns>
        public async Task<string> RecognizeSpeechAsync(int durationSeconds,float deep)
        {
            var recognizer = new VoskRecognizer(_voskModel, 16000.0f);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine($"[Vosk] Starting AOT-compatible microphone listening for {durationSeconds} seconds...");
                using (var recorder = new WinmmAudioRecorder())
                {
                    recorder.StartRecording();
                    await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
                    byte[] recordedAudio = recorder.StopRecording();

                    if (recordedAudio.Length == 0)
                    {
                        return "未能录制到任何音频。";
                    }

                    recognizer.AcceptWaveform(recordedAudio, recordedAudio.Length);
                    string resultJson = recognizer.FinalResult();
                    return System.Text.Json.JsonDocument.Parse(resultJson).RootElement.GetProperty("text").GetString() ?? "识别结果为空。";
                }
            }

            var waveIn = new WaveInEvent { DeviceNumber = 0, WaveFormat = new WaveFormat(16000, 1) };

            var tcs = new TaskCompletionSource<string>();
            string recognizedText = "";

            waveIn.DataAvailable += (sender, args) =>
            {
                if (recognizer.AcceptWaveform(args.Buffer, args.BytesRecorded))
                {
                    var resultJson = recognizer.Result();
                    recognizedText = System.Text.Json.JsonDocument.Parse(resultJson).RootElement.GetProperty("text").GetString() ?? "";
                }
            };

            waveIn.RecordingStopped += (sender, args) =>
            {
                // 获取最后的结果
                var finalResultJson = recognizer.FinalResult();
                var finalRecognizedText = System.Text.Json.JsonDocument.Parse(finalResultJson).RootElement.GetProperty("text").GetString() ?? "";

                // 取最长的那个识别结果
                if (finalRecognizedText.Length > recognizedText.Length)
                {
                    recognizedText = finalRecognizedText;
                }

                if (string.IsNullOrWhiteSpace(recognizedText))
                {
                    recognizedText = "未能识别到语音。";
                }

                tcs.TrySetResult(recognizedText);

                waveIn.Dispose();
                recognizer.Dispose();
            };

            Console.WriteLine($"[SpeechEngine] 开始监听麦克风，持续 {durationSeconds} 秒...");
            waveIn.StartRecording();

            // 在指定时间后停止
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(t => waveIn.StopRecording());

            return await tcs.Task;
        }


        public void Stop()
        {
            _voskModel.Dispose();
            _logService.Log("Vosk 模型已释放。");
        }

       

        public string Start(VoiceClientOption clientOptions)
        {
            return "";
        }
    }
}
