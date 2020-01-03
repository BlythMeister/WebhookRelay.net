using System.Collections.Generic;

namespace WebhookRelay.Net.Models
{
    public class HookInformation
    {
        public string Type { get; set; }
        public string SubType { get; set; }
        public string JsonData { get; set; }
        public List<KeyValuePair<string, IEnumerable<string>>> RequestHeaders { get; set; }
    }
}
