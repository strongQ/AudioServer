using log4net;
using XT.Common.Enums;
using XT.Common.Interfaces;
using XT.Common.Models.SignalR;
using XT.Common.Services;

namespace Audio.Core.LogServices
{
    public class LogService : ILogService
    {
        public event EventHandler<string> AddLogEvent;

      


        public LogService()
        {
           

        }

        public void Log(string info)
        {
         
            WriteConsole(info);
        }

        public void LogError(string info, Exception ex)
        {
         
            WriteConsole(info, ex);
        }

        public void LogError(string error)
        {
          
            WriteConsole(error);
        }

        public void LogHeart(string info)
        {
         
            WriteConsole(info);
        }

        public async Task LogRemote(RemoteLog remoteLog)
        {
            try
            {
                WriteConsole($"{remoteLog.Title} {remoteLog.Content}");
            

         
            }
            catch (Exception ex)
            {
                WriteConsole($"{remoteLog.ID} {remoteLog.Title} {remoteLog.Content}", ex);
            }
            return;
        }

        public void WriteConsole(string msg, Exception? ex = null)
        {
            var log = $"{DateTime.Now} {msg} {ex?.StackTrace}";
            Console.WriteLine(log);
        }
    }
}
