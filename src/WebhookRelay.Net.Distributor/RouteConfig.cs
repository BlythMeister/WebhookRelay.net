using Newtonsoft.Json;
using System;

namespace WebhookRelay.Net.Distributor
{
    public class RouteConfig
    {
        public string Name { get; }
        public string Type { get; }
        public string SubType { get; }
        public string PostDestination { get; }
        public string AuthenticationScheme { get; }
        [JsonConverter(typeof(EncryptingJsonConverter))] public string AuthenticationHeader { get; }
        [JsonIgnore] public string PostDestinationHost { get; }


        public RouteConfig(string name, string type, string subType, string postDestination, string authenticationScheme, string authenticationHeader)
        {
            Name = name;
            Type = type;
            SubType = subType;
            PostDestination = postDestination;
            AuthenticationScheme = authenticationScheme;
            AuthenticationHeader = authenticationHeader;
            PostDestinationHost = new Uri(postDestination).Host;
        }
    }
}
