using System;
using Topshelf;

namespace WebhookRelay.Net.Distributor
{
    class Program
    {
        static void Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();

            var rc = HostFactory.Run(x =>
            {
                x.Service<MessageHandlerService>(s =>
                {
                    s.ConstructUsing(name => new MessageHandlerService());
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });

                x.EnableServiceRecovery(r =>
                {
                    r.RestartService(1);
                    r.OnCrashOnly();
                    r.SetResetPeriod(1);
                });

                x.RunAsLocalSystem();
                x.StartAutomatically();

                x.SetDescription("WebhookRelay.Net Distributor Service");
                x.SetDisplayName("WebhookRelay.Net Distributor");
                x.SetServiceName("WebhookRelay.Net Distributor");
                x.UseLog4Net();
            });

            var exitCode = (int)Convert.ChangeType(rc, rc.GetTypeCode());
            Environment.ExitCode = exitCode;
        }
    }
}
