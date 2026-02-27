using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using PortAudioSharp; // 引用包
using Vosk;

public class PortAudioListener : IDisposable
{
    private VoskRecognizer _recognizer;
    private readonly string _wakeWord;
    private Model _model;

    // PortAudioSharp 的流对象
    private PortAudioSharp.Stream _stream;

    // 线程锁 (保护 Vosk)
    private readonly object _speechLock = new object();

    public event Action OnWakeWordDetected;

    public PortAudioListener(string modelPath, string wakeWord)
    {
        _wakeWord = wakeWord;
        if (!System.IO.Directory.Exists(modelPath))
            throw new Exception($"模型路径不存在: {modelPath}");

        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, 16000.0f);
        _recognizer.SetMaxAlternatives(0);
        _recognizer.SetWords(true);
    }
    /// <summary>
    /// 获取关键词
    /// </summary>
    /// <returns></returns>
    public string GetWord()
    {
        return _wakeWord;
    }

    public string StartListening()
    {
        if (_stream != null && _stream.IsActive) return "";

        try
        {
            // 1. 初始化 PortAudio
            PortAudio.Initialize();

            // 2. 配置输入参数
            // 使用系统默认输入设备 (索引 -1 或 DefaultInputDevice)
            int deviceIndex = PortAudio.DefaultInputDevice;
            if (deviceIndex == PortAudio.NoDevice)
                return ("未找到默认麦克风设备");

            var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);
            Console.WriteLine($"[PortAudio] 使用设备: {deviceInfo.name}");

            StreamParameters inputParams = new StreamParameters();
            inputParams.device = deviceIndex;
            inputParams.channelCount = 1; // 单声道
            inputParams.sampleFormat = SampleFormat.Int16; // 16位整数
            inputParams.suggestedLatency = deviceInfo.defaultLowInputLatency;
            inputParams.hostApiSpecificStreamInfo = IntPtr.Zero;

            // 3. 创建流
            // 采样率设为 16000 (PortAudio 会请求驱动重采样)
            _stream = new PortAudioSharp.Stream(
                inputParams,
                null, // 无输出
                sampleRate: 16000,
                framesPerBuffer: 0, // 0 = 让系统决定缓冲大小
                streamFlags: StreamFlags.ClipOff,
                callback: OnAudioCallback, // 绑定回调
                userData: IntPtr.Zero
            );

            // 4. 启动
            _stream.Start();
            Console.WriteLine($"[唤醒引擎] 监听启动: 等待 \"{_wakeWord}\" ...");
            return string.Empty;
        }
        catch (Exception ex)
        {
           
            Dispose(); // 出错就清理
            return ($"启动失败: {ex.Message}");
        }
    }

    // 音频回调 (在此处处理增益和识别)
    private StreamCallbackResult OnAudioCallback(
        IntPtr input, IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        if (input == IntPtr.Zero) return StreamCallbackResult.Continue;

        // 1. 从非托管内存拷贝到 C# 数组
        int bytesToRead = (int)frameCount * 2; // 16-bit = 2 bytes
        byte[] buffer = new byte[bytesToRead];
        Marshal.Copy(input, buffer, 0, bytesToRead);

        // 2. 软件放大 (4x Gain)
        float multiplier = 4.0f;
        for (int i = 0; i < bytesToRead; i += 2)
        {
            short sample = BitConverter.ToInt16(buffer, i);
            float boosted = sample * multiplier;

            // 防爆音截断
            if (boosted > short.MaxValue) boosted = short.MaxValue;
            if (boosted < short.MinValue) boosted = short.MinValue;

            short finalSample = (short)boosted;
            buffer[i] = (byte)(finalSample & 0xFF);
            buffer[i + 1] = (byte)((finalSample >> 8) & 0xFF);
        }

        // 3. 喂给 Vosk (加锁保护)
        lock (_speechLock)
        {
            // 如果已经被 StopListening 销毁，就别跑了
            if (_recognizer == null) return StreamCallbackResult.Continue;

            if (_recognizer.AcceptWaveform(buffer, bytesToRead))
            {
                CheckResult(_recognizer.Result());
            }
            else
            {
                CheckResult(_recognizer.PartialResult());
            }
        }

        return StreamCallbackResult.Continue;
    }

    public void StopListening()
    {
        try
        {
            if (_stream != null)
            {
                if (!_stream.IsStopped) _stream.Stop();
                _stream.Close();
                _stream.Dispose();
                _stream = null;
            }

            PortAudio.Terminate(); // 彻底关闭引擎

            //lock (_speechLock)
            //{
            //    _recognizer?.Dispose();
            //    _recognizer = null;
            //}

            //Console.WriteLine("[唤醒引擎] 已停止");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"停止异常: {ex.Message}");
        }
    }

    private void CheckResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        string text = "";
        if (doc.RootElement.TryGetProperty("text", out var t)) text = t.GetString();
        else if (doc.RootElement.TryGetProperty("partial", out var p)) text = p.GetString();

        if (string.IsNullOrWhiteSpace(text)) return;

        if (text.Contains(_wakeWord))
        {
            Console.WriteLine($"\n>>> 🔥 触发唤醒: \"{text}\" <<<");

            lock (_speechLock)
            {
                if (_recognizer != null) _recognizer.Reset();
            }

            // 异步触发，防止死锁 (非常重要)
            Task.Run(() => OnWakeWordDetected?.Invoke());
        }
    }

    public void Dispose()
    {
        StopListening();
        _model?.Dispose();
    }
}