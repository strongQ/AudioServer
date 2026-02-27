using Audio.Core.Interfaces;
using Audio.Core.Options;
using Audio.Core.PInvoke;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PortAudioSharp;
using SherpaOnnx;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Vosk;
using XT.Common.Extensions;
using XT.Common.Services;
using XT.MNet.Ws.Options;

namespace Audio.Core.Engines
{
    /// <summary>
    /// Sherpa-ONNX文本转语音
    /// </summary>
    public class SherpaEngineService : IVoiceEngineService
    {
        private OfflineTts _tts;
        private int _sampleRate = 22050; // 通常 VITS 模型是 22k 或 16k

        private  VoiceClientOption _clientOptions;
        private  Model _voskModel;
        private string _outputWavPath;
        private string _lastSpeakStr = string.Empty;

        private List<string> _specialWords = new List<string>();

        public event Action<float> OnVolumeChanged;
        /// <summary>
        /// 正在说的话
        /// </summary>
        public event Action<string> OnPartialResult;
        public SherpaEngineService()
        {
          
           
      
        }
        /// <summary>
        /// 工厂黑话翻译机：把一切非中文转为中文拟声
        /// </summary>
        private string NormalizeForFactory(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            StringBuilder sb = new StringBuilder();

            // 遍历每一个字符
            foreach (char c in input)
            {
                // --- 1. 处理数字 (工厂标准) ---
                if (char.IsDigit(c))
                {
                    switch (c)
                    {
                        case '0': sb.Append("零"); break;
                        case '1': sb.Append("一"); break; // 强制读 幺
                        case '2': sb.Append("二"); break;
                        case '3': sb.Append("三"); break;
                        case '4': sb.Append("四"); break;
                        case '5': sb.Append("五"); break;
                        case '6': sb.Append("六"); break;
                        case '7': sb.Append("七"); break; // 或 拐
                        case '8': sb.Append("八"); break;
                        case '9': sb.Append("九"); break; // 或 勾
                    }
                    continue; // 处理完数字，跳过本次循环
                }

                // --- 2. 处理字母 (强制拟声) ---
                // 只要是 A-Z，全部转成中文拼音/汉字。模型绝对认识汉字。
                if ( (c >= 'A' && c <= 'Z'))
                {
                    switch (char.ToUpper(c))
                    {
                        case 'A': sb.Append("未"); break;      // A -> Ei (爱)
                        case 'B': sb.Append("毕"); break;      // B -> Bi (毕)
                        case 'C': sb.Append("西"); break;      // C -> Xi (西/Sei)
                        case 'D': sb.Append("弟"); break;      // D -> Di (弟)
                        case 'E': sb.Append("义"); break;      // E -> Yi (义)
                        case 'F': sb.Append("艾夫"); break;    // F -> Ai Fu
                        case 'G': sb.Append("记"); break;      // G -> Ji (记)
                        case 'H': sb.Append("艾尺"); break;    // H -> Ai Chi
                        case 'I': sb.Append("爱"); break;      // I -> Ai (爱) - 注意 A 和 I 读音接近，但在工厂可区分
                        case 'J': sb.Append("杰"); break;      // J -> Jie (杰) / Zhei
                        case 'K': sb.Append("克"); break;      // K -> Ke (克) / Kei
                        case 'L': sb.Append("艾勒"); break;    // L -> Ai Le
                        case 'M': sb.Append("艾姆"); break;    // M -> Ai Mu
                        case 'N': sb.Append("恩"); break;      // N -> En (恩)
                        case 'O': sb.Append("欧"); break;      // O -> Ou (欧)
                        case 'P': sb.Append("辟"); break;      // P -> Pi (辟)
                        case 'Q': sb.Append("秋"); break;      // Q -> Qiu (秋)
                        case 'R': sb.Append("阿"); break;      // R -> A (阿) / Ar
                        case 'S': sb.Append("艾斯"); break;    // S -> Ai Si
                        case 'T': sb.Append("替"); break;      // T -> Ti (替)
                        case 'U': sb.Append("优"); break;      // U -> You (优)
                        case 'V': sb.Append("维"); break;      // V -> Wei (维)
                        case 'W': sb.Append("达布溜"); break;  // W -> Da Bu Liu
                        case 'X': sb.Append("艾克斯"); break;  // X -> Ai Ke Si
                        case 'Y': sb.Append("外"); break;      // Y -> Wai (外)
                        case 'Z': sb.Append("贼"); break;      // Z -> Zei (贼)
                    }
                    sb.Append(" "); // 字母后加个空格，防止连读
                    continue;
                }

                // --- 3. 处理常见符号 ---
                if (c == '-') { sb.Append("杠"); continue; }
                if (c == '.') { sb.Append("点"); continue; }
                if (c == '#') { sb.Append("井号"); continue; }

                // --- 4. 其他字符 (汉字) ---
                // 只有在这个模型字典里的字才会被保留，
                // 为了防止有些生僻字搞崩模型，可以加个简单的正则判断是否是中文
               
                   
                
                // 忽略所有其他未知符号（如空格、换行符等保留一个空格即可）
                 if (char.IsWhiteSpace(c))
                {
                    sb.Append(" ");
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // 简单的中文判断
        private bool IsChinese(char c)
        {
            return c >= 0x4E00 && c <= 0x9FA5;
        }

        /// <summary>
        /// 开始
        /// </summary>
        /// <returns></returns>
        public string Start(VoiceClientOption clientOptions, List<string> words = null)
        {
            string error = string.Empty;

            _specialWords = words;

            _clientOptions=clientOptions;
          
            _outputWavPath = Path.Combine(AppContext.BaseDirectory, "output.wav");

            _voskModel = new Model(Path.Combine(AppContext.BaseDirectory, _clientOptions.VoskPath));

            // vosk模型配置为空
            if (_clientOptions.VoskPath.IsNullOrEmpty())
            {
                error = $"错误: vosk 路径未配置！路径: {_clientOptions.VoskPath}";
           

                return error;
            }

            var config = new OfflineTtsConfig();

            var lc = AppContext.BaseDirectory;
            // 配置模型路径 (根据你下载的模型修改)
            config.Model.Vits.Model = Path.Combine(lc, "models/vits-zh-ll/model.onnx");
            config.Model.Vits.Lexicon = Path.Combine(lc, "models/vits-zh-ll/lexicon.txt");
            config.Model.Vits.Tokens = Path.Combine(lc, "models/vits-zh-ll/tokens.txt");

            // 关键优化：MeloTTS 默认语速可能偏快或偏慢，可以调整 scale
            config.Model.Vits.NoiseScale = 0.667f;
            config.Model.Vits.NoiseScaleW = 0.8f;
            config.Model.Vits.LengthScale = 1.5f; // 语速，1.0 标准，数字越大越慢

            if (!File.Exists(config.Model.Vits.Model))
            {
                error = $"Sherpa 模型文件未找到！路径：{config.Model.Vits.Model}";
                
                return error;
            }

            if (!File.Exists(config.Model.Vits.Lexicon))
            {
                error = $"Sherpa lexicon文件未找到！路径：{config.Model.Vits.Lexicon}";
             
                return error;
            }

            if (!File.Exists(config.Model.Vits.Tokens))
            {
                error = $"Sherpa tokens！路径：{config.Model.Vits.Tokens}";
              
                return error;
            }

            // 初始化引擎
            _tts = new OfflineTts(config);
            _sampleRate = _tts.SampleRate;


            return error;
        }


        // 确保引用了 System.Text.Json


        /// <summary>
        /// 语音识别
        /// </summary>
        /// <param name="durationSeconds">持续录音时长</param>
        /// <param name="deep">噪音深度</param>
        /// <returns></returns>
       public async  Task<string> RecognizeSpeechAsync(int durationSeconds, float deep = 0.2f)
        {
            if (_voskModel == null) return "模型未加载";

            // VAD 参数
            float silenceThresholdSeconds = 2f;
            float voiceActivityThreshold = deep;
            string grammarJson = string.Empty;
            if (_specialWords != null && _specialWords.Count > 0)
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
                    string json = recognizer.PartialResult();
                    string text = ParsePartialJson(json);

                    // 只有当内容不为空时触发，避免 UI 疯狂刷新
                    if (!string.IsNullOrWhiteSpace(text))
                    {
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

        // ✅ 新增：解析 Vosk 的 Partial JSON
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


        /// <summary>
        /// 文本转语音
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<string> SpeakAsync(string text)
        {
        
            string error=string.Empty;
            if (text.IsNotNullOrEmpty() && text.Equals(_lastSpeakStr) && File.Exists(_outputWavPath))
            {
               await PlayVoice();
                return string.Empty;
            }
            _lastSpeakStr = text;
            try
            {
                text = NormalizeForFactory(text);
                // 这里的 text 可以直接是 "当前工单 A123"
                // 只要 lexicon.txt 里定义了 A -> ei，它就会读对
                var audio = _tts.Generate(text, speed: 1.0f, 0);

                // 3. 【关键】检查生成的 Samples 是否为空
                if (audio.Samples == null || audio.Samples.Length == 0)
                {
                    
                    error= ("错误：TTS 生成失败，audio.Samples 为空！请检查模型路径是否正确。");
                    Console.WriteLine(error);
                    return error;
                }

                // 保存为 Wav 文件
                var success = audio.SaveToWaveFile(_outputWavPath);

                if (success)
                {
                    await PlayVoice();
                    return string.Empty;
                 
                }
                else
                {
                    error = "错误：TTS生成成功，无法生成文件";
                    Console.WriteLine(error);
                    return error;
                }
            }
            catch(Exception ex)
            {
                error=(ex.StackTrace);
                Console.WriteLine(error);
                return error;
            }
        }

        private async Task PlayVoice()
        {
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
                Console.WriteLine("Sherpa 未能成功生成 output.wav 文件。");
            }
        }

        /// <summary>
        /// 停止
        /// </summary>
        public void Stop()
        {
            _voskModel.Dispose();
          
        }
    }
}
