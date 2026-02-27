using Audio.Core.Consts;
using Audio.Core.Enums;
using Audio.Core.Extensions;
using Audio.Core.Interfaces;
using Audio.Core.Models;
using Audio.Core.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XT.Common.Extensions;
using XT.Common.Services;
using XT.MNet.Tcp;
using XT.MNet.Tcp.Options;
using static System.Net.Mime.MediaTypeNames;

namespace Audio.Core.Services
{
    public class VoiceClientService
    {
        private  VoiceClientOption _clientOptions;

        public string ReplyWord { get; set; } = "请说";
        private readonly IVoiceEngineService _voskEngineService;

        /// <summary>
        /// 成功加载
        /// </summary>
        public bool LoadOk { get; set; }

        /// <summary>
        /// 是否有视图
        /// </summary>
        public bool HasView { get; set; }


        private PortAudioListener _wakeListener;

        /// <summary>
        /// 语音日志变化
        /// </summary>
        public event Action<string> OnVoiceLogChanged;
        /// <summary>
        /// 音量发生变化
        /// </summary>
        public event Action<float> OnVolumeChanged;
        /// <summary>
        /// 语音发生变化
        /// </summary>
        public event Action<string, bool> OnVoiceChanged;

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="clientOptions">vosk配置</param>
        /// <param name="logService"></param>
        /// <param name="voskEngineService"></param>
        public VoiceClientService(IVoiceEngineService voskEngineService)
        {
           
            _voskEngineService = voskEngineService;                
        }


      

      /// <summary>
      /// 语音播报
      /// </summary>
      /// <param name="text"></param>
      /// <returns></returns>
        public async Task<string>  Speak(string text)
        {      
            var result= await _voskEngineService.SpeakAsync(text);    
            return result;
        }
       
        /// <summary>
        /// 启动
        /// </summary>
        /// <param name="clientOptions"></param>
        /// <param name="reply"></param>
        /// <param name="words"></param>
        /// <returns></returns>
        public  string Start(VoiceClientOption clientOptions,string reply="请说",List<string> words=null)
        {
            ReplyWord = reply;
            if (LoadOk)
            {
                return string.Empty;
            }
            LoadOk = true;
            _clientOptions=clientOptions;

            OnVoiceLogChanged?.Invoke("开始启动语音环境");

           var result= _voskEngineService.Start(clientOptions,words);


            OnVoiceLogChanged?.Invoke($"语音环境已经启动完成，{result}");

            if (result.IsNullOrEmpty())
            {
                _voskEngineService.OnVolumeChanged += _voskEngineService_OnVolumeChanged;

                _voskEngineService.OnPartialResult += _voskEngineService_OnPartialResult;

                OnVoiceLogChanged?.Invoke($"语音事件已经订阅完成");

                LoadOk = true;
            }
            else
            {
                LoadOk = false;
            }



                return result;
        }
        /// <summary>
        /// 语音识别
        /// </summary>
        /// <param name="obj"></param>
        private void _voskEngineService_OnPartialResult(string obj)
        {
            OnVoiceChanged?.Invoke(obj,false);
        }

        /// <summary>
        /// 音量变化事件处理
        /// </summary>
        /// <param name="obj"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void _voskEngineService_OnVolumeChanged(float obj)
        {
            OnVolumeChanged?.Invoke(obj);
        }

        /// <summary>
        /// 主动唤醒
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public string StartListen(string text)
        {
          
            if(_wakeListener != null)
            {
                return string.Empty ;
            }
            OnVoiceLogChanged?.Invoke($"开始启动语音关键词监听，关键词：{text}");
            if (_clientOptions.VoskSmallPath.IsNullOrEmpty())
            {
                var error = $"监听vosk路径不存在，{_clientOptions.VoskSmallPath}";
                Console.WriteLine(error) ;

                OnVoiceLogChanged?.Invoke(error);
                return error ;
            }
            _wakeListener = new PortAudioListener(_clientOptions.VoskSmallPath, text);

            _wakeListener.OnWakeWordDetected += _wakeListener_OnWakeWordDetected;


            var result= _wakeListener.StartListening();

            if (result.IsNotNullOrEmpty())
            {
                OnVoiceLogChanged?.Invoke(result);
            }

            return string.Empty;
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void StopListen()
        {
            if (_wakeListener != null)
            {
                _wakeListener.StopListening();
            }


        }

        private async void _wakeListener_OnWakeWordDetected()
        {
            OnVoiceLogChanged?.Invoke("已经监听到关键词，开始启动语音识别");


            Console.Beep(1000, 200);

            OnVoiceChanged?.Invoke(ReplyWord, false);

           await Speak(ReplyWord);

            string result= await Voice(_clientOptions.DurationSecond);


            if(result.IsNotNullOrEmpty())
            {
                OnVoiceLogChanged?.Invoke($"语音识别完成，识别文字：{result}");

                // 识别完成
                OnVoiceChanged?.Invoke(result, true);
            }
            else
            {
                OnVoiceLogChanged?.Invoke($"语音识别完成，无法识别成任何文字");
            }
               

            var listen= _wakeListener.StartListening();
            if (listen.IsNotNullOrEmpty())
            {
                OnVoiceLogChanged?.Invoke(listen);
            }

            OnVoiceLogChanged?.Invoke($"开始启动语音关键词监听，关键词：{_wakeListener.GetWord()}");
        }


        /// <summary>
        /// 语音转文字
        /// </summary>
        /// <param name="maxSeconds">最大时长</param>
        /// <returns></returns>
        public async Task<string> Voice(int maxSeconds)
        {
           
            var res = await _voskEngineService.RecognizeSpeechAsync(_clientOptions.DurationSecond,(float)_clientOptions.Deeep);


            if (res.IsNotNullOrEmpty())
            {
                Console.WriteLine(res);

                OnVoiceChanged?.Invoke(res, true);
            }

            return res;

        }


   

        public Task StopAsync(CancellationToken cancellationToken)
        {

            LoadOk = false;
            _voskEngineService.Stop();

            OnVoiceLogChanged?.Invoke("停止语音服务");
            _voskEngineService.OnVolumeChanged -= _voskEngineService_OnVolumeChanged;

            _voskEngineService.OnPartialResult -= _voskEngineService_OnPartialResult;

            return Task.CompletedTask;
        }
    }
}
