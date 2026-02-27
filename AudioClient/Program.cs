// Program.cs
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Audio.Core.Options;
using Audio.Core.Interfaces;
using XT.Common.Services;
using Audio.Core.LogServices;
using Audio.Core.Services;
using Audio.Core.Engines;


var services = new ServiceCollection();
IConfiguration configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false, reloadOnChange: false).Build();
var cg = configuration.GetSection("Vosk");

var option = new VoiceClientOption
{
    VoskPath = cg["VoskPath"],
    PiperExePath = cg["PiperExePath"],
    PiperModelPath = cg["PiperModelPath"],
    VoskSmallPath = cg["VoskSmallPath"],
    DurationSecond = int.Parse(cg["DurationSecond"] ?? "20"),
    SherpaPath = cg["SherpaPath"],
    Deeep = double.Parse(cg["Deep"]??"0.2")
};



services.AddSingleton<IVoiceEngineService, SherpaEngineService>();
services.AddSingleton<VoiceClientService>();

services.AddSingleton<ILogService, LogService>();
var builder = services.BuildServiceProvider();

Console.WriteLine("=================================================");
Console.WriteLine("=        C# 离线语音后台服务 (控制台版)       =");
Console.WriteLine("  按 [R] 键: 主动开始语音识别并广播结果。");
Console.WriteLine("  按 [W] 键: 文字转语音。");
Console.WriteLine("=================================================");
Console.WriteLine("正在初始化...");

// --- 配置 ---




// --- 设置优雅关机 ---
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    Console.WriteLine("捕获到 Ctrl+C, 正在关闭服务器...");
    cts.Cancel();
    // 阻止程序立即终止，给服务器时间来处理关闭逻辑
    e.Cancel = true;
};




try
{

    Console.WriteLine("初始化完成。按 Ctrl+C 关闭服务。");

    var voiceService = builder.GetService<VoiceClientService>();

    voiceService.OnVoiceLogChanged += VoiceService_OnVoiceLogChanged;

    voiceService.OnVoiceChanged += VoiceService_OnVoiceChanged;

    voiceService.OnVolumeChanged += VoiceService_OnVolumeChanged;

    var words = new List<string> { "托", "盘", "定", "位", "站", "台", "入", "库", "出", "口", "一","二","三","四","五","六","七","八","九"};
    _ = Task.Run(() =>
    {
        voiceService.Start(option,"请说",words);
    }).ContinueWith((x) =>
    {
        voiceService.StartListen("系统");
    });
      

    




    // 主线程循环，用于监听键盘输入
    while (!cts.Token.IsCancellationRequested)
    {
        if (Console.KeyAvailable)
        {
            var keyInfo = Console.ReadKey(intercept: true);
            if (keyInfo.Key == ConsoleKey.R)
            {
                // 以“即发即忘”的方式调用，不阻塞主循环
                var result = await voiceService.Voice(30);

                Console.WriteLine(result);
            }
            else if (keyInfo.Key == ConsoleKey.W)
            {
                Console.WriteLine("按下 W 键，请输入播报文字");
                var result = Console.ReadLine();
                if (!string.IsNullOrEmpty(result))
                {
                    await voiceService.Speak(result);
                }

            }
        }
        // 短暂休眠，避免CPU空转
        await Task.Delay(100, cts.Token);
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"程序发生致命错误: {ex.StackTrace}");
    Console.ResetColor();
}

void VoiceService_OnVolumeChanged(float obj)
{
    Console.WriteLine($"Volume：{obj}");
}

void VoiceService_OnVoiceChanged(string arg1, bool arg2)
{
    if (arg2)
    {
        Console.WriteLine($"最终识别文字 {arg1}");
    }
    else
    {
        Console.WriteLine($"过程识别文字 {arg1}");
    }
}

void VoiceService_OnVoiceLogChanged(string obj)
{
    Console.WriteLine(obj);
}

Console.ReadKey();