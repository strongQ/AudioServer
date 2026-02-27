using Audio.Core.Interfaces;
using Audio.Core.Options;
using Audio.Core.PInvoke;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SherpaOnnx;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vosk;
using XT.Common.Extensions;
using XT.Common.Services;

namespace AudioServer.Services
{
    /// <summary>
    /// Sherpa-ONNX文本转语音
    /// </summary>
    public class EngineAudioService : IVoiceEngineService
    {
        private OfflineTts _tts;
        private int _sampleRate = 22050; // 通常 VITS 模型是 22k 或 16k
        private readonly ILogService _logService;
        private readonly VoskServerOption _voskOptions;
        private readonly Model _voskModel;
        private string _outputWavPath;
        private string _lastSpeakStr = string.Empty;
        public EngineAudioService(VoskServerOption voskOptions,ILogService logService)
        {
            _logService = logService;
            _voskOptions = voskOptions;
            _voskModel = new Model(Path.Combine(AppContext.BaseDirectory, _voskOptions.ModelPath));
            _outputWavPath = Path.Combine(AppContext.BaseDirectory, _voskOptions.OutputWavPath);
            InitTts();
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


        public void InitTts()
        {
            var config = new OfflineTtsConfig();

            var lc = AppContext.BaseDirectory;
            // 配置模型路径 (根据你下载的模型修改)
            config.Model.Vits.Model = Path.Combine(lc,"models/vits-zh-ll/model.onnx");
            config.Model.Vits.Lexicon =Path.Combine(lc, "models/vits-zh-ll/lexicon.txt");
            config.Model.Vits.Tokens = Path.Combine(lc,"models/vits-zh-ll/tokens.txt");

            // 关键优化：MeloTTS 默认语速可能偏快或偏慢，可以调整 scale
            config.Model.Vits.NoiseScale = 0.667f;
            config.Model.Vits.NoiseScaleW = 0.8f;
            config.Model.Vits.LengthScale = 1.5f; // 语速，1.0 标准，数字越大越慢

            // 初始化引擎
            _tts = new OfflineTts(config);
            _sampleRate = _tts.SampleRate;
        }

        // 确保引用了 System.Text.Json

        public async Task<string> RecognizeSpeechAsync(int maxDurationSeconds = 30,float deep=0.5f)
        {
            // 1. 检查模型
            if (_voskModel == null) return "模型未加载";

            float silenceThresholdSeconds = 1.5f;

            var recognizer = new VoskRecognizer(_voskModel, 16000.0f);
            recognizer.SetMaxAlternatives(0);
            recognizer.SetWords(true);

            // 2. 初始化 WaveIn
            using var waveIn = new WaveInEvent();
            waveIn.DeviceNumber = -1; // 使用系统默认
            waveIn.WaveFormat = new WaveFormat(16000, 1);
            waveIn.BufferMilliseconds = 100;

            var tcs = new TaskCompletionSource<string>();
            StringBuilder resultBuilder = new StringBuilder();

            // 状态变量
            bool isSpeaking = false;            // 是否已经开始说话了
            DateTime lastSoundTime = DateTime.Now; // 上一次听到声音的时间
            DateTime startTime = DateTime.Now;     // 开始录音的时间
            bool isStopped = false;             // 防止多次调用 Stop

            // 阈值设置
            // 音量阈值 (0.0 - 1.0): 超过这个值认为是在说话
            // 笔记本麦克风可能噪音大，建议设 0.05 ~ 0.1，如果环境安静可以设小点
            float voiceActivityThreshold = 0.2f;

            Console.WriteLine("==========================================");
            Console.WriteLine("  智能监听模式启动");
            Console.WriteLine("  请说话... (说完后自动停止)");
            Console.WriteLine("==========================================");

            waveIn.DataAvailable += (s, a) =>
            {
                if (isStopped) return;

                byte[] buffer = a.Buffer;
                int bytesRecorded = a.BytesRecorded;

                // 1. 计算当前音量 (原始音量)
                float currentMaxVolume = 0;
                for (int i = 0; i < bytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(buffer, i);
                    float abs = Math.Abs(sample / 32768f);
                    if (abs > currentMaxVolume) currentMaxVolume = abs;
                }

                // 2. 软件放大 (Gain Boost) - 保持不变，为了 Vosk 识别更好
                float multiplier = 4.0f;
                for (int i = 0; i < bytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(buffer, i);
                    float boosted = sample * multiplier;
                    if (boosted > 32767) boosted = 32767;
                    if (boosted < -32768) boosted = -32768;

                    short finalSample = (short)boosted;
                    buffer[i] = (byte)(finalSample & 0xFF);
                    buffer[i + 1] = (byte)((finalSample >> 8) & 0xFF);
                }

                // --- 3. 核心修复：可视化调试与判断 ---
                Console.WriteLine("CurrentVolume" + currentMaxVolume);
                // 逻辑：如果音量 > 阈值，说明有声音 -> 重置时间
                if (currentMaxVolume > voiceActivityThreshold)
                {
                    if (!isSpeaking) isSpeaking = true;
                    lastSoundTime = DateTime.Now; // 刷新活跃时间

                    // 打印调试信息：显示红色或特殊标记，表示正在录音
                    // "Vol: 0.25" 表示当前音量，方便你调整阈值
                    Console.Write($"\r[🗣️ 说话中] Vol: {currentMaxVolume:F3} > {voiceActivityThreshold} | {new string('|', (int)(currentMaxVolume * 20))}   ");
                }
                else
                {
                    // 也就是：噪音(0.08) < 阈值(0.15) -> 认为是静音
                    if (isSpeaking)
                    {
                        var silenceDuration = (DateTime.Now - lastSoundTime).TotalSeconds;

                        // 打印倒计时
                        Console.Write($"\r[⏳ 倒计时] Vol: {currentMaxVolume:F3} < {voiceActivityThreshold} | 剩余: {(silenceThresholdSeconds - silenceDuration):F1}s   ");

                        if (silenceDuration > silenceThresholdSeconds)
                        {
                            StopRecordingSafe();
                        }
                    }
                    else
                    {
                        // 还没开始说话时的待机状态
                        Console.Write($"\r[👂 待机中] Vol: {currentMaxVolume:F3} (请说话...)        ");
                    }
                }

                // 超时强制停止
                if ((DateTime.Now - startTime).TotalSeconds > maxDurationSeconds)
                {
                    Console.WriteLine("\n[超时] 强制停止。");
                    StopRecordingSafe();
                }

                // 喂给 Vosk
                if (recognizer.AcceptWaveform(buffer, bytesRecorded))
                {
                    // var partial = ParseVoskResult(recognizer.PartialResult()); // 可选显示中间结果
                }
            };

            waveIn.RecordingStopped += (s, a) =>
            {
                Console.WriteLine("\n✋ 录音结束，正在解析...");
                var text = ParseVoskResult(recognizer.FinalResult());
                resultBuilder.Append(text);

                string full = resultBuilder.ToString().Trim();
                if (string.IsNullOrEmpty(full)) full = "未识别到内容";

                tcs.TrySetResult(full);
            };

            // 封装一个线程安全的停止方法
            void StopRecordingSafe()
            {
                if (isStopped) return;
                isStopped = true;
                try { waveIn.StopRecording(); } catch { }
            }

            try
            {
                waveIn.StartRecording();
                // 等待任务完成（由 RecordingStopped 触发 SetResult）
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                return $"出错: {ex.Message}";
            }
        }

        // 辅助方法保持不变
        private string ParseVoskResult(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                // Vosk 的 FinalResult 格式略有不同，可能在 'text' 字段，也可能在 'alternatives'[0]['text']
                // 这里的代码通常能处理标准输出
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("text", out var p))
                {
                    return p.GetString() ?? "";
                }
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
        public async Task SpeakAsync(string text)
        {
        
            if (text.IsNotNullOrEmpty() && text.Equals(_lastSpeakStr) && File.Exists(_outputWavPath))
            {
               await PlayVoice();
                return;
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
                    Console.WriteLine("错误：TTS 生成失败，audio.Samples 为空！请检查模型路径是否正确。");
                    return;
                }

                // 保存为 Wav 文件
                var success = audio.SaveToWaveFile(_outputWavPath);

                if (success)
                {
                    await PlayVoice();

                 
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
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
            _logService.Log("Vosk 模型已释放。");
        }

        Task<string> IVoiceEngineService.SpeakAsync(string text)
        {
            throw new NotImplementedException();
        }

        public string Start(VoiceClientOption clientOptions)
        {
            return "";
        }
    }
}
