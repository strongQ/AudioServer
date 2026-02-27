using Audio.Core.Consts;
using Audio.Core.Enums;
using Audio.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XT.Common.Enums;

namespace Audio.Core.Models
{
    public class TcpDataTemplate
    {
        public CommandHeaderEnum Header { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public char ContentHeader { get; set; } = VoskConst.Header;

        public string Body { get; set; }

        public override string ToString()
        {
            return $"{Header}{Timestamp.ToString("yyyyMMddHHmmss")}{ContentHeader}{Body.AddFooter()}";
        }
    }
}
