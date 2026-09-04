using Newtonsoft.Json;

namespace RfpProxyLib.Messages
{
    public class Hello
    {
        public Hello(){}

        public Hello(string message)
        {
            Message = message;
        }

        [JsonProperty("msg")]
        public string Message { get; set; }
    }
}
