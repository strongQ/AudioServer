using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.Enums
{
    public enum CommandHeaderEnum
    {
        /// <summary>
        /// 服务端状态广播
        /// </summary>
        S001,
        /// <summary>
        /// 客户端语音播报信息
        /// </summary>
        S002,
        /// <summary>
        /// 客户端请求开始语音识别
        /// </summary>
        S003,
        /// <summary>
        /// 服务端语音识别结果
        /// </summary>
        S004
    }
}
