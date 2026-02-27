using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.Options
{
    /// <summary>
    /// 客户端配置
    /// </summary>
    public class VoiceClientOption
    {
        /// <summary>
        /// 文字转语音
        /// </summary>
        public string PiperModelPath { get; set; }
        /// <summary>
        /// Piper执行路径
        /// </summary>
        public string PiperExePath { get; set; }
        /// <summary>
        /// 文字转语音
        /// </summary>
        public string SherpaPath { get; set; }
        /// <summary>
        /// 语音转文字
        /// </summary>
        public string VoskPath { get; set; }
        /// <summary>
        /// 语音转文字
        /// </summary>
        public string VoskSmallPath { get; set; }
        /// <summary>
        /// 噪音深度
        /// </summary>
        public double Deeep { get; set; } = 0.2;
        /// <summary>
        /// 持续时间
        /// </summary>
        public int DurationSecond { get; set; } = 30;
    }
}
