using Audio.Core.Options;
using log4net.Config;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Audio.Core.Extensions
{
    public static class IServiceCollectionExtension
    {
        public static IServiceCollection ConfigureLog4net(this IServiceCollection services)
        {
            var directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string fileName = directoryName + "\\log4net.config";
            XmlConfigurator.ConfigureAndWatch(new FileInfo(fileName));

            return services;
        }
    }
}
