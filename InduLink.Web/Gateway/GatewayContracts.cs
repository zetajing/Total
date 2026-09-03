using System;
using System.Collections.Generic;
using InduLink.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InduLink.Web.Gateway
{
    public sealed class GatewayReadRequest
    {
        [JsonProperty("correlationId")]
        public string CorrelationId { get; set; }

        [JsonProperty("items")]
        public List<GatewayReadItem> Items { get; set; }
    }

    public sealed class GatewayReadItem
    {
        [JsonProperty("device")]
        public string Device { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }
    }

    public sealed class GatewayWriteRequest
    {
        [JsonProperty("correlationId")]
        public string CorrelationId { get; set; }

        [JsonProperty("items")]
        public List<GatewayWriteItem> Items { get; set; }
    }

    public sealed class GatewayWriteItem
    {
        [JsonProperty("device")]
        public string Device { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("value")]
        public JToken Value { get; set; }
    }

    public sealed class GatewayRawReadRequest
    {
        [JsonProperty("correlationId")]
        public string CorrelationId { get; set; }

        [JsonProperty("items")]
        public List<GatewayRawReadItem> Items { get; set; }
    }

    public sealed class GatewayRawReadItem
    {
        [JsonProperty("device")]
        public string Device { get; set; }

        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("dataType")]
        public DataType DataType { get; set; }

        [JsonProperty("length")]
        public ushort Length { get; set; } = 1;
    }

    internal sealed class GatewaySubscriptionCommand
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("correlationId")]
        public string CorrelationId { get; set; }

        [JsonProperty("items")]
        public List<GatewayReadItem> Items { get; set; }
    }

    internal sealed class GatewayHttpException : Exception
    {
        internal GatewayHttpException(int statusCode, string code, string message)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code;
        }

        internal int StatusCode { get; private set; }
        internal string Code { get; private set; }
    }
}
