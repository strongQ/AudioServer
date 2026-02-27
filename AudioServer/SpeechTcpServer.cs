using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using XT.MNet.Tcp.Options;
using XT.MNet.Tcp;

namespace AudioServer
{
    public class SpeechTcpServer
    {
        private readonly TcpServer _listener;
        private readonly OfflineSpeechEngine _speechEngine;

        public SpeechTcpServer(string ipAddress, int port, OfflineSpeechEngine speechEngine)
        {
            _speechEngine = speechEngine;

            _listener = new TcpServer(new TcpServerOptions()
            {
                Address = "0.0.0.0",
                Port = 8888,

                SpecialChar = '#' // only work for raw data

            });

        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _listener.Start();
            Console.WriteLine($"[TCPServer] 服务器已启动，正在监听 {_listener.Socket.LocalEndPoint}");

            // 注册回调，当收到取消信号时停止监听
            cancellationToken.Register(() => _listener.Stop());


            _listener.On(async(data,e) =>
            {
                var command = Encoding.UTF8.GetString(data.ToArray()).Trim();
              
                Console.WriteLine($"[TCPServer] 收到命令: {command} 来自 {e.Socket.RemoteEndPoint}");
                if (command.StartsWith("SPEAK:", StringComparison.OrdinalIgnoreCase))
                {
                    var textToSpeak = command.Substring(6);
               
                    await _speechEngine.SpeakTextAsync(textToSpeak);
                 
                }
                e.Send(Encoding.UTF8.GetBytes("OK#")); // 回复客户端确认

            });

            _listener.OnConnect += _listener_OnConnect;
            _listener.OnDisconnect += _listener_OnDisconnect;



        }


        // 【新增】核心功能：主动识别并向所有客户端广播
        public async Task ProactivelyRecognizeAndBroadcastAsync(CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine("[Proactive] 开始主动识别...");
                string recognizedText = await _speechEngine.RecognizeSpeechAsync(10); // 监听10秒
                Console.WriteLine($"[Proactive] 识别完成: {recognizedText}");

                if (cancellationToken.IsCancellationRequested) return;

                string messageToSend = $"语音识别结果:{recognizedText}";

              await   _speechEngine.SpeakTextAsync(messageToSend);
                byte[] messageBytes = Encoding.UTF8.GetBytes(messageToSend);

                _listener.Broadcast(messageBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Proactive] 主动识别或广播时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 主动读取文本并播报
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public async Task ReadText(string msg)
        {
          await  _speechEngine.SpeakTextAsync(msg);
        }

        private void _listener_OnDisconnect(TcpServerConnection connection)
        {
            Console.WriteLine($"[TCPServer] 新连接已断开");
        }

        private void _listener_OnConnect(TcpServerConnection connection)
        {
            connection.Send(Encoding.UTF8.GetBytes($"[TCPServer] 新连接已建立: {connection.Socket.RemoteEndPoint}#"));
            Console.WriteLine($"[TCPServer] 新连接已建立: {connection.Socket.RemoteEndPoint}");
        }
    }
}
