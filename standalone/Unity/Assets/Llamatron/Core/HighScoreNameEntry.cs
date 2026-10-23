using System.Text;

namespace Llamatron.Core
{
    public sealed class HighScoreNameEntry
    {
        public string Value { get; private set; } = "";
        private bool replaceOnInput;
        public void Reset(string name)
        {
            var clean = new StringBuilder(8);
            foreach (char c in name)
            {
                if (clean.Length == 8) break;
                clean.Append(c >= 32 && c <= 126 ? c : '?');
            }
            Value = clean.ToString(); replaceOnInput = true;
        }
        public void Type(char c)
        {
            if (c < 32 || c > 126) return;
            if (replaceOnInput) { Value = ""; replaceOnInput = false; }
            if (Value.Length < 8) Value = $"{Value}{c}";
        }
        public void Backspace()
        {
            if (replaceOnInput) Value = "";
            else if (Value.Length != 0) Value = Value.Substring(0, Value.Length - 1);
            replaceOnInput = false;
        }
    }
}
