using System.Collections.Generic;
using Newtonsoft.Json;

namespace Apify.Models
{
    public class MockServerConfig
    {
        [JsonProperty("Port")]
        public int Port { get; set; } = 8080;

        [JsonProperty("Directory")]
        public string Directory { get; set; } = ".apify/mocks";

        [JsonProperty("Verbose")]
        public bool Verbose { get; set; } = false;

        [JsonProperty("EnableCors")]
        public bool EnableCors { get; set; } = true;

        [JsonProperty("DefaultHeaders")]
        public Dictionary<string, string>? DefaultHeaders { get; set; }

        [JsonProperty("FileStoragePath")]
        public string? FileStoragePath { get; set; }
    }
}
