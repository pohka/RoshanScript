using System;
using System.Text;

namespace DotaTranspiler.IR
{
    /// <summary>
    /// Writes Lua source text with indentation tracking.
    /// Wraps a StringBuilder; used by all LuaNode.WriteTo implementations.
    /// </summary>
    public sealed class LuaWriter : IDisposable
    {
        private readonly StringBuilder _sb;
        private int _indentLevel;
        private bool _atLineStart = true;
        private const string IndentUnit = "    ";  // 4 spaces

        public LuaWriter(StringBuilder sb) => _sb = sb;

        public void Indent() => _indentLevel++;
        public void Dedent() => _indentLevel = Math.Max(0, _indentLevel - 1);

        public void Write(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_atLineStart)
            {
                for (int i = 0; i < _indentLevel; i++)
                    _sb.Append(IndentUnit);
                _atLineStart = false;
            }
            _sb.Append(text);
        }

        public void NewLine()
        {
            _sb.AppendLine();
            _atLineStart = true;
        }

        public void WriteLine(string text)
        {
            Write(text);
            NewLine();
        }

        public void Dispose() { }
    }
}
