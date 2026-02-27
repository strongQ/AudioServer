using Audio.Core.Consts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.Options
{
    public class TcpOption
    {
        public string ServerIp { get; set; } = VoskConst.DefaultIP;

        public ushort ServerPort { get; set; }
    }
}
