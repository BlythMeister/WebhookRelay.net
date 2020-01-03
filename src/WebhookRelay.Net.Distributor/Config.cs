using Newtonsoft.Json;
using System.Collections.Generic;

namespace WebhookRelay.Net.Distributor
{
    public class Config
    {
        [JsonConverter(typeof(EncryptingJsonConverter))] public string ServiceBusConnectionString { get; }
        [JsonConverter(typeof(EncryptingJsonConverter))] public string QueueName { get; }
        public bool DebugOutput { get; }
        public string DebugOutputPath { get; }
        public List<RouteConfig> Routes { get; }

        public Config(string serviceBusConnectionString, string queueName, bool debugOutput, string debugOutputPath, List<RouteConfig> routes)
        {
            ServiceBusConnectionString = serviceBusConnectionString;
            QueueName = queueName;
            DebugOutput = debugOutput;
            DebugOutputPath = debugOutputPath;
            Routes = routes;
        }
    }
}
