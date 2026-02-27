// Program.cs
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;
using AudioServer;

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
var cg=configuration.GetSection("Vosk");

var option = new VoskServerOption
{
    ServerIp = cg["ServerIp"],
    ServerPort = ushort.Parse(cg["ServerPort"] ?? "8888"),
    ModelPath = cg["ModelPath"],
    PiperExePath = cg["PiperExePath"],
    PiperModelPath = cg["PiperModelPath"],
    OutputWavPath = cg["OutputWavPath"],
    DurationSeconds = int.Parse(cg["DurationSeconds"] ?? "10")

};

services.AddSingleton<VoskServerOption>(option);

services.AddSingleton<IVoiceEngineService, SherpaEngineService>();
services.AddSingleton<VoiceServerService>();

services.AddSingleton<ILogService, LogService>();
var builder=services.BuildServiceProvider();

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

   var voiceService= builder.GetService<VoiceServerService>();


  await  voiceService.StartAsync();


    // 主线程循环，用于监听键盘输入
    while (!cts.Token.IsCancellationRequested)
    {
        if (Console.KeyAvailable)
        {
            var keyInfo = Console.ReadKey(intercept: true);
            if (keyInfo.Key == ConsoleKey.R)
            {
                // 以“即发即忘”的方式调用，不阻塞主循环
               var result=  await voiceService.Voice(30);

                Console.WriteLine(result);
            }
            else if(keyInfo.Key == ConsoleKey.W)
            {
                Console.WriteLine("按下 W 键，请输入播报文字");
               var result= Console.ReadLine();
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







Console.ReadKey();