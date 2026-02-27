using Audio.Core.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.Interfaces
{


    /// <summary>
    /// 语音服务
    /// </summary>
    public interface IVoiceEngineService
    {
        public event Action<float> OnVolumeChanged;

        /// <summary>
        /// 正在说的话
        /// </summary>
        public event Action<string> OnPartialResult;
        /// <summary>
        /// 文字转语音
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        Task<string> SpeakAsync(string text);

       /// <summary>
       /// 语音识别
       /// </summary>
       /// <param name="durationSeconds">持续录音时长</param>
       /// <param name="deep">噪音深度</param>
       /// <returns></returns>
        Task<string> RecognizeSpeechAsync(int durationSeconds,float deep=0.2f);
        /// <summary>
        /// 停止
        /// </summary>
        void Stop();

    
        /// <summary>
        /// 开始
        /// </summary>
        /// <returns>异常信息</returns>
        string Start(VoiceClientOption clientOptions,List<string> words=null);
       
    }
}
