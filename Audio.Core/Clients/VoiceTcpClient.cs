using Audio.Core.Consts;
using Audio.Core.Enums;
using Audio.Core.Extensions;
using Audio.Core.LogServices;
using Audio.Core.Models;
using Audio.Core.Options;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XT.Common.Services;
using XT.MNet.Tcp;
using XT.MNet.Tcp.Options;

namespace Audio.Core.Clients
{
    public class VoiceTcpClient : INotifyPropertyChanged
    {
        private TcpClient _tcpClient;

        public event Action<string> SpeechRecognized;
        public event PropertyChangedEventHandler PropertyChanged;

        private VoskServerStatusEnum _serverStatus;
        public VoskServerStatusEnum ServerStatus
        {
            get => _serverStatus;
            set
            {
                if (_serverStatus != value)
                {
                    _serverStatus = value;
                    OnPropertyChanged(propertyName: nameof(ServerStatus));
                }
            }
        }

        public bool IsConnection { get; set; }
        public VoiceTcpClient()
        {
           
        }

        public void Connect(VoskServerOption option)
        {
            _tcpClient = new TcpClient(new TcpClientOptions()
            {
                Address = option.ServerIp,
                Port = option.ServerPort,
                SpecialChar = VoskConst.Footer
            });

            _tcpClient.Connect();

            _tcpClient.On(OnTcpDataReceived);

            _tcpClient.OnConnect += _tcpClient_OnConnect;
            _tcpClient.OnDisconnect += _tcpClient_OnDisconnect;
        }

        private void _tcpClient_OnDisconnect()
        {
            IsConnection = false;
        }

        private void _tcpClient_OnConnect()
        {
            IsConnection = true;
        }

        public void SendSpeakCommand(string input)
        {
            if (ServerStatus == VoskServerStatusEnum.Busy) return;

            var dataTemplate = new TcpDataTemplate
            {
                Header = CommandHeaderEnum.S002,
                Body = input
            };
            _tcpClient.Send(dataTemplate.ToString());
        }

        public void SendRequestCommand(int input)
        {
            if (ServerStatus == VoskServerStatusEnum.Busy) return;

            var dataTemplate = new TcpDataTemplate
            {
                Header = CommandHeaderEnum.S003,
                Body = input.ToString()
            };
            _tcpClient.Send(dataTemplate.ToString());
        }

        public async Task SendByCycle(string msg, int count)
        {
            if (ServerStatus == VoskServerStatusEnum.Busy) return;

            for (int i = 0; i < count; i++)
            {
                while (ServerStatus == VoskServerStatusEnum.Busy)
                {
                    await Task.Delay(1000);
                }

                SendSpeakCommand(msg);
            }
        }

        private void OnTcpDataReceived(Memory<byte> data)
        {
            var command = Encoding.UTF8.GetString(data.ToArray()).Trim();

            //_logService.Log($"[TCPServer] 收到命令: {command}");

            if (command.ConvertToTcpData(out var result))
            {
                switch (result.Header)
                {
                    case CommandHeaderEnum.S001:
                        Handler001(result);
                        break;
                    case CommandHeaderEnum.S004:
                        Handler004(result);
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// 服务端状态广播
        /// </summary>
        /// <param name="input"></param>
        /// <param name="e"></param>
        private void Handler001(TcpDataTemplate input)
        {
            ServerStatus = input.Body == "1" ? VoskServerStatusEnum.Free : VoskServerStatusEnum.Busy;
        }

        /// <summary>
        /// 服务端语音识别结果
        /// </summary>
        /// <param name="input"></param>
        /// <param name="e"></param>
        private void Handler004(TcpDataTemplate input)
        {
            SpeechRecognized?.Invoke(input.Body);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
