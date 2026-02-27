using Audio.Core.Interfaces;
using Audio.Core.Options;
using Audio.Core.PInvoke;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using PortAudioSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vosk;
using XT.Common.Extensions;
using XT.Common.Services;

namespace Audio.Core.Engines
{
    /// <summary>
    /// piper文本转语音
    /// </summary>
    public class PiperEngineService: IVoiceEngineService
    {
        private  VoiceClientOption _clientOptions;

        private List<string> _specialWords = new List<string>();

        private Model _voskModel;
        private  string _piperExePath;
        private  string _piperModelPath ;
        private  string _outputWavPath;

        public event Action<float> OnVolumeChanged;

        /// <summary>
        /// 正在说的话
        /// </summary>
        public event Action<string> OnPartialResult;

        public PiperEngineService()
        { 
            

         
        }

        /// <summary>
        /// 开始
        /// </summary>
        /// <returns></returns>
        public string Start(VoiceClientOption clientOptions, List<string> words = null)
        {
            _specialWords = words;
            _clientOptions=clientOptions;
            string error = string.Empty;
            _piperExePath = Path.Combine(AppContext.BaseDirectory, _clientOptions.PiperExePath);
            _piperModelPath = Path.Combine(AppContext.BaseDirectory, _clientOptions.PiperModelPath);
            _outputWavPath = Path.Combine(AppContext.BaseDirectory, "output.wav");

            _voskModel = new Model(Path.Combine(AppContext.BaseDirectory, _clientOptions.VoskPath));

            // 检查 PiperExe 是否存在
            if (!File.Exists(_piperExePath))
            {
                error = $"错误: Piper 可执行程序未找到！路径: {_piperExePath}";      
                return error;

            }

            // 检查 PiperModel 是否存在
            if (!File.Exists(_piperModelPath))
            {
                error = $"错误: Piper 模型文件未找到！路径: {_piperModelPath}";
                return error;
            }
            // vosk模型配置为空
            if (_clientOptions.VoskPath.IsNullOrEmpty())
            {
                error = $"错误: vosk 路径未配置！路径: {_clientOptions.VoskPath}";
            

                return error;
            }

            return error;
        }
      

        /// <summary>
        /// 文字转语音
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public async Task<string> SpeakAsync(string text)
        {
            string errMsg=string.Empty;
            if (File.Exists(_outputWavPath))
            {
                try { File.Delete(_outputWavPath); }
                catch (Exception ex) 
                {
                    errMsg = $"无法删除旧的 output.wav 文件: {ex.Message}";
                    Console.WriteLine(errMsg);
                    return errMsg;
                
                }
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
                errMsg = error;
                return errMsg;
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
                errMsg = "Piper 未能成功生成 output.wav 文件。";
                Console.WriteLine(errMsg);
                return errMsg;
            }

            return errMsg;
        }

        /// <summary>
        /// 语音识别
        /// </summary>
        /// <param name="durationSeconds">持续录音时长</param>
        /// <param name="deep">噪音深度</param>
        /// <returns></returns>
        public async Task<string> RecognizeSpeechAsync(int durationSeconds, float deep = 0.2f)
        {
            if (_voskModel == null) return "模型未加载";

            // VAD 参数
            float silenceThresholdSeconds = 2f;
            float voiceActivityThreshold = deep;

            string grammarJson = string.Empty;
            if(_specialWords!=null && _specialWords.Count>0)
            {
                if (!_specialWords.Contains("[unk]"))
                {
                    _specialWords.Add("[unk]");
                }
                 grammarJson = "[" + string.Join(", ", _specialWords.Select(w => $"\"{w}\"")) + "]";
            }

         
            // 初始化 Vosk
            var recognizer = new VoskRecognizer(_voskModel, 16000.0f,grammarJson);
            recognizer.SetMaxAlternatives(0);
            recognizer.SetWords(true);

            // 状态变量
            bool isSpeaking = false;
            DateTime lastSoundTime = DateTime.Now;
            DateTime startTime = DateTime.Now;
            bool isStopped = false;

            var tcs = new TaskCompletionSource<string>();
            StringBuilder resultBuilder = new StringBuilder();
            PortAudioSharp.Stream stream = null;

            // 1. 定义停止逻辑 (本地函数)
            void StopSafe()
            {
                lock (tcs)
                {
                    if (isStopped) return;
                    isStopped = true;
                }

                Task.Run(() =>
                {
                    try { if (stream != null && !stream.IsStopped) stream.Stop(); } catch { }

                    var text = ParseVoskResult(recognizer.FinalResult());
                    resultBuilder.Append(text);
                    string full = resultBuilder.ToString().Trim();
                    if (string.IsNullOrEmpty(full)) full = "未识别到内容";
                    tcs.TrySetResult(full);
                });
            }

            // ✅【关键修复】定义为本地函数，明确参数类型，解决 Lambda 报错
            // 这个函数可以直接访问上面的 isSpeaking, tcs 等变量
            StreamCallbackResult OnAudioCallback(
                IntPtr input,
                IntPtr output,
                uint frameCount,
                ref StreamCallbackTimeInfo timeInfo,
                StreamCallbackFlags statusFlags,
                IntPtr userData)
            {
                if (isStopped) return StreamCallbackResult.Complete;

                // --- 1. 读取 48000Hz 原始数据 ---
                // PortAudio 这次录的是 48k，所以数据量是原来的 3 倍
                int srcCount = (int)frameCount;     // 48000 下的点数
                int destCount = srcCount / 3;       // 我们要转换成 16000 下的点数

                int srcBytes = srcCount * 2;
                byte[] rawBuffer = new byte[srcBytes];
                Marshal.Copy(input, rawBuffer, 0, srcBytes);

                // 准备目标 Buffer (16k)
                // Vosk 需要 short[] (不含 header 的 PCM)
                short[] destBuffer = new short[destCount];

                float currentMaxVolume = 0;
                float multiplier = 4.0f;

                // --- 2. 核心算法：手动降采样 (48k -> 16k) ---
                // 算法：每隔 3 个采样点取 1 个 (Decimation)
                // 索引映射：dest[0] <- raw[0], dest[1] <- raw[3], dest[2] <- raw[6]...

                for (int i = 0; i < destCount; i++)
                {
                    // 计算原始数据中的位置 (i * 3 * 2bytes)
                    int srcIndex = i * 3 * 2;

                    // 防止越界
                    if (srcIndex + 1 >= rawBuffer.Length) break;

                    short sample = BitConverter.ToInt16(rawBuffer, srcIndex);

                    // 软件放大
                    float boosted = sample * multiplier;
                    if (boosted > short.MaxValue) boosted = short.MaxValue;
                    if (boosted < short.MinValue) boosted = short.MinValue;

                    short finalSample = (short)boosted;

                    destBuffer[i] = finalSample;

                    // VAD 音量计算
                    float abs = Math.Abs(finalSample / 32768f);
                    if (abs > currentMaxVolume) currentMaxVolume = abs;
                }
                // --- 3. 核心修复：可视化调试与判断 ---
                Console.WriteLine("CurrentVolume" + currentMaxVolume);

                OnVolumeChanged?.Invoke(currentMaxVolume);
                // C. 智能 VAD 状态机
                if (currentMaxVolume > voiceActivityThreshold)
                {
                    if (!isSpeaking) isSpeaking = true; Console.Write($"\r[🗣️ 说话中] Vol: {currentMaxVolume:F3} > {voiceActivityThreshold} | {new string('|', (int)(currentMaxVolume * 20))}   "); Console.Write($"\r[🗣️] Vol: {currentMaxVolume:F2}   ");

                    lastSoundTime = DateTime.Now;
                }
                else
                {
                    if (isSpeaking)
                    {
                        var silenceTime = (DateTime.Now - lastSoundTime).TotalSeconds;
                        Console.Write($"\r[⏳ 倒计时] Vol: {currentMaxVolume:F3} < {voiceActivityThreshold} | 剩余: {(silenceThresholdSeconds - silenceTime):F1}s   ");

                        if (silenceTime > silenceThresholdSeconds)
                        {
                            StopSafe();
                            return StreamCallbackResult.Complete;
                        }
                    }
                    else
                    {
                        Console.Write($"\r[👂] 待机 Vol: {currentMaxVolume:F3}   ");
                    }
                }

                if ((DateTime.Now - startTime).TotalSeconds > durationSeconds)
                {
                    Console.WriteLine("\n[超时] 强制停止");
                    StopSafe();
                    return StreamCallbackResult.Complete;
                }

                // --- 4. 喂给 Vosk (注意：AcceptWaveform 支持直接传 short[]) ---
                if (recognizer.AcceptWaveform(destBuffer, destBuffer.Length))
                {
                    // 中间结果
                }
                else
                {
                    // 返回 False：表示正在说话中 (Partial Result)
                    // 获取 JSON，例如：{ "partial" : "今天天..." }
                    string json = recognizer.PartialResult();
                    string text = ParsePartialJson(json);

                    // 只有当内容不为空时触发，避免 UI 疯狂刷新
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // 注意：这里是在后台音频线程，千万不要在这里调用 UI 代码
                        OnPartialResult?.Invoke(text);
                    }
                }

                    return StreamCallbackResult.Continue;
            }

            try
            {
                PortAudio.Initialize();
                int deviceIndex = PortAudio.DefaultInputDevice;
                if (deviceIndex == PortAudio.NoDevice) throw new Exception("未找到默认麦克风");

                var inputParams = new StreamParameters
                {
                    device = deviceIndex,
                    channelCount = 1,
                    sampleFormat = SampleFormat.Int16,
                    suggestedLatency = PortAudio.GetDeviceInfo(deviceIndex).defaultLowInputLatency,
                    hostApiSpecificStreamInfo = IntPtr.Zero
                };

                Console.WriteLine("==========================================");
                Console.WriteLine("  🎙️ PortAudio 智能监听中...");
                Console.WriteLine("==========================================");

                // 2. 创建流，传入本地函数
                stream = new PortAudioSharp.Stream(
                     inputParams,
                     null,
                    sampleRate: 48000,
                    framesPerBuffer: 0,
                    streamFlags: StreamFlags.ClipOff,
                    callback: OnAudioCallback, // ✅ 直接传函数名，不会报错了
                    userData: IntPtr.Zero
                );

                stream.Start();
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (stream != null)
                {
                    stream.Close();
                    stream.Dispose();
                }
                PortAudio.Terminate();
                recognizer.Dispose();
            }
        }

        private string ParsePartialJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("partial", out var p))
                {
                    return p.GetString() ?? "";
                }
            }
            catch { /* 忽略解析错误 */ }
            return "";
        }
        // 辅助方法 (保持不变)
        private string ParseVoskResult(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("text", out var p)) return p.GetString() ?? "";
            }
            catch { }
            return "";
        }

        public void Stop()
        {
            _voskModel.Dispose();
         
        }
    }
}
