using System;

namespace IndustrialCommSdk.Exceptions
{
    public class IndustrialCommunicationException : Exception
    {
        public IndustrialCommunicationException(string message) : base(message) { }
        public IndustrialCommunicationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed class IndustrialConnectionException : IndustrialCommunicationException
    {
        public IndustrialConnectionException(string message, Exception innerException) : base(message, innerException) { }
        public IndustrialConnectionException(string message) : base(message) { }
    }

    public sealed class IndustrialTimeoutException : IndustrialCommunicationException
    {
        public IndustrialTimeoutException(string message, Exception innerException) : base(message, innerException) { }
        public IndustrialTimeoutException(string message) : base(message) { }
    }

    public sealed class IndustrialProtocolException : IndustrialCommunicationException
    {
        public IndustrialProtocolException(string message, Exception innerException) : base(message, innerException) { }
        public IndustrialProtocolException(string message) : base(message) { }
    }

    public sealed class IndustrialAddressParseException : IndustrialCommunicationException
    {
        public IndustrialAddressParseException(string message, Exception innerException) : base(message, innerException) { }
        public IndustrialAddressParseException(string message) : base(message) { }
    }

    public sealed class IndustrialDataConversionException : IndustrialCommunicationException
    {
        public IndustrialDataConversionException(string message, Exception innerException) : base(message, innerException) { }
        public IndustrialDataConversionException(string message) : base(message) { }
    }
}
