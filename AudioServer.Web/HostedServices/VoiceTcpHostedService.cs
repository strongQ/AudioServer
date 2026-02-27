using Audio.Core.Consts;
using Audio.Core.Enums;
using Audio.Core.Extensions;
using Audio.Core.Interfaces;
using Audio.Core.Models;
using Audio.Core.Options;
using AudioServer.Web.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using XT.Common.Enums;
using XT.Common.Extensions;
using XT.Common.Services;
using XT.MNet.Tcp;
using XT.MNet.Tcp.Options;
using XT.MNet.Ws;

namespace AudioServer.Web.HostedServices
{
    public class VoiceTcpHostedService : IHostedService
    {
        private readonly VoskServerOption _voskOptions;
        private readonly ILogService _logService;
        private readonly IVoiceEngineService _voskEngineService;
        private readonly TcpServer _listener;

        private Timer? _statusTimer;

        private VoskServerStatusEnum _status;
        public VoskServerStatusEnum Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnStatusChanged(_status);
                }
            }
        }

        public VoiceTcpHostedService(IOptions<VoskServerOption> voskOptions, ILogService logService, IVoiceEngineService voskEngineService)
        {
            _voskOptions=voskOptions.Value;
            _logService=logService;
            _voskEngineService=voskEngineService;
            _listener = new TcpServer(new TcpServerOptions()
            {
                Address = _voskOptions.ServerIp,
                Port = _voskOptions.ServerPort,
                SpecialChar = VoskConst.Footer  
            });
            Status = VoskServerStatusEnum.Free;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _listener.Start();
            _listener.On(OnTcpDataReceived);            
            _listener.OnConnect+= Connect;
            _listener.OnDisconnect+= Disconnect;

            _logService.Log($"[TCPServer] 服务端配置: IP={_voskOptions.ServerIp}, 端口={_voskOptions.ServerPort}, 特殊字符={VoskConst.Footer}");

            _statusTimer = new Timer(_ =>
            {
                OnStatusChanged(Status);
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));

            return Task.CompletedTask;
        }

        private async void OnTcpDataReceived(Memory<byte> data, TcpServerConnection e)
        {
            var command = Encoding.UTF8.GetString(data.ToArray()).Trim();

            _logService.Log($"[TCPServer] 收到命令: {command} 来自 {e.Socket.RemoteEndPoint}");

            if (command.ConvertToTcpData(out var result))
            {
                if (Status == VoskServerStatusEnum.Busy) return;

                switch (result.Header)
                {
                    case CommandHeaderEnum.S002:
                      await  Handler002(result, e);
                        break;
                    case CommandHeaderEnum.S003:
                       await Handler003(result, e);
                        break;
                    default:
                        e.Send(MessageConst.InvalidMsg.AddFooter());
                        break;
                }
            }
            else
            {
                e.Send(MessageConst.InvalidMsg.AddFooter());
            }
        }

        /// <summary>
        /// 语音播报
        /// </summary>
        /// <param name="input"></param>
        /// <param name="e"></param>
        private async Task Handler002(TcpDataTemplate input, TcpServerConnection e)
        {
            if(input==null || input.Body.IsNullOrEmpty())
            {
                return;
            }
            Status = VoskServerStatusEnum.Busy;

         await   _voskEngineService.SpeakAsync(input.Body);

            Status = VoskServerStatusEnum.Free;
        }

        /// <summary>
        /// 开启语音识别
        /// </summary>
        /// <param name="input"></param>
        /// <param name="e"></param>
        private async Task Handler003(TcpDataTemplate input, TcpServerConnection e)
        {
            Status = VoskServerStatusEnum.Busy;

            if (int.TryParse(input.Body,out var d))
            {
                _voskOptions.DurationSeconds = d;
            }

            var res =await _voskEngineService.RecognizeSpeechAsync(_voskOptions.DurationSeconds);

            var sendMsg = new TcpDataTemplate
            {
                Header = CommandHeaderEnum.S004,
                Body = res,
            };

            Status = VoskServerStatusEnum.Free;
            e.Send(sendMsg.ToString()); 

         
        }

        private void Connect(TcpServerConnection connection)
        {
            OnStatusChanged(Status);
            _logService.Log($"[TCPServer] 客户端已连接: {connection.Socket.RemoteEndPoint}");
        }

        private void Disconnect(TcpServerConnection connection) { }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _listener.Stop();
            _voskEngineService.Stop();
            _statusTimer?.Dispose();
            return Task.CompletedTask;
        }

        private void OnStatusChanged(VoskServerStatusEnum statusEnum)
        {
            var sendMsg = new TcpDataTemplate
            {
                Header = CommandHeaderEnum.S001,
                Body = ((int)statusEnum).ToString(),
            };
            var sendData = Encoding.UTF8.GetBytes(sendMsg.ToString());
            _listener.Broadcast(sendData);
        }
    }
}
