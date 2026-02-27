using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.Models
{
    /// <summary>
    /// 交互日志
    /// </summary>
    public class InteractionLog
    {
        public string Time { get; set; }= DateTime.Now.ToString("HH:mm:ss");

        public string Content { get; set; }
        /// <summary>
        /// true用户回复   false系统回复
        /// </summary>
        public bool IsUser { get; set; }
    }
}
