using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.Options
{
    public class VoskServerOption : TcpOption
    {
        public string ModelPath { get; set; }

        public string PiperExePath { get; set; }

        public string PiperModelPath { get; set; }

        public string OutputWavPath { get; set; }

        public int DurationSeconds { get; set; }
    }
}
