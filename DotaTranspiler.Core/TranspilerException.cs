using System;
using Microsoft.CodeAnalysis;

namespace DotaTranspiler
{
    public sealed class TranspilerException : Exception
    {
        public Location? SourceLocation { get; }

        public TranspilerException(string message) : base(message) { }

        public TranspilerException(string message, Location location)
            : base(FormatMessage(message, location))
        {
            SourceLocation = location;
        }

        private static string FormatMessage(string message, Location location)
        {
            var span = location.GetLineSpan();
            return $"{span.Path}({span.StartLinePosition.Line + 1}): {message}";
        }
    }
}
