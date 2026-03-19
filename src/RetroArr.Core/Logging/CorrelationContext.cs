using System;
using System.Threading;

namespace RetroArr.Core.Logging
{
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<string?> _requestId = new();

        public static string? RequestId
        {
            get => _requestId.Value;
            set => _requestId.Value = value;
        }

        public static string GetOrCreate()
        {
            if (string.IsNullOrEmpty(_requestId.Value))
            {
                _requestId.Value = Guid.NewGuid().ToString("N")[..12];
            }
            return _requestId.Value;
        }
    }
}
