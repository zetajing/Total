using System;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>Controls how batch read operations are planned and executed.</summary>
    public sealed class BatchReadOptions
    {
        public BatchReadOptions(
            TimeSpan? totalTimeout = null,
            TimeSpan? perRequestTimeout = null,
            bool continueOnError = true,
            bool preserveOrder = true,
            bool allowPartialResults = true,
            int? maxItemsPerBatch = null,
            int? maxAddressSpan = null,
            int? maxPduBytes = null)
        {
            ValidateTimeout(totalTimeout, nameof(totalTimeout));
            ValidateTimeout(perRequestTimeout, nameof(perRequestTimeout));
            ValidatePositive(maxItemsPerBatch, nameof(maxItemsPerBatch));
            ValidatePositive(maxAddressSpan, nameof(maxAddressSpan));
            ValidatePositive(maxPduBytes, nameof(maxPduBytes));

            TotalTimeout = totalTimeout;
            PerRequestTimeout = perRequestTimeout;
            ContinueOnError = continueOnError;
            PreserveOrder = preserveOrder;
            AllowPartialResults = allowPartialResults;
            MaxItemsPerBatch = maxItemsPerBatch;
            MaxAddressSpan = maxAddressSpan;
            MaxPduBytes = maxPduBytes;
        }

        public TimeSpan? TotalTimeout { get; private set; }
        public TimeSpan? PerRequestTimeout { get; private set; }
        public bool ContinueOnError { get; private set; }
        public bool PreserveOrder { get; private set; }
        public bool AllowPartialResults { get; private set; }
        public int? MaxItemsPerBatch { get; private set; }
        public int? MaxAddressSpan { get; private set; }
        public int? MaxPduBytes { get; private set; }

        public static BatchReadOptions Default { get { return new BatchReadOptions(); } }

        internal static void ValidateTimeout(TimeSpan? value, string name)
        {
            if (value.HasValue && value.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(name);
        }

        internal static void ValidatePositive(int? value, string name)
        {
            if (value.HasValue && value.Value <= 0) throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>Controls how batch write operations are planned and executed.</summary>
    public sealed class BatchWriteOptions
    {
        public BatchWriteOptions(
            TimeSpan? totalTimeout = null,
            TimeSpan? perRequestTimeout = null,
            bool continueOnError = false,
            bool preserveOrder = true,
            int? maxItemsPerBatch = null,
            int? maxAddressSpan = null,
            int? maxPduBytes = null)
        {
            BatchReadOptions.ValidateTimeout(totalTimeout, nameof(totalTimeout));
            BatchReadOptions.ValidateTimeout(perRequestTimeout, nameof(perRequestTimeout));
            BatchReadOptions.ValidatePositive(maxItemsPerBatch, nameof(maxItemsPerBatch));
            BatchReadOptions.ValidatePositive(maxAddressSpan, nameof(maxAddressSpan));
            BatchReadOptions.ValidatePositive(maxPduBytes, nameof(maxPduBytes));

            TotalTimeout = totalTimeout;
            PerRequestTimeout = perRequestTimeout;
            ContinueOnError = continueOnError;
            PreserveOrder = preserveOrder;
            MaxItemsPerBatch = maxItemsPerBatch;
            MaxAddressSpan = maxAddressSpan;
            MaxPduBytes = maxPduBytes;
        }

        public TimeSpan? TotalTimeout { get; private set; }
        public TimeSpan? PerRequestTimeout { get; private set; }
        public bool ContinueOnError { get; private set; }
        public bool PreserveOrder { get; private set; }
        public int? MaxItemsPerBatch { get; private set; }
        public int? MaxAddressSpan { get; private set; }
        public int? MaxPduBytes { get; private set; }

        public static BatchWriteOptions Default { get { return new BatchWriteOptions(); } }
    }
}
