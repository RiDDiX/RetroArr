using System;

namespace RetroArr.Core.Games
{
    // Thrown when an Update or Add would collide with an existing row on the
    // unique (Title, PlatformId) index. Lets controllers map to a 409 response
    // with actionable details instead of a generic 500.
    public class DuplicateGameException : Exception
    {
        public string? ConflictField { get; }
        public string? AttemptedValue { get; }

        public DuplicateGameException(string message, string? field = null, string? value = null, Exception? inner = null)
            : base(message, inner)
        {
            ConflictField = field;
            AttemptedValue = value;
        }
    }
}
