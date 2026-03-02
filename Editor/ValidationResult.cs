using System.Collections.Generic;

namespace LLarean.GlyphFontChecker
{
    public class ValidationResult
    {
        public enum Severity { Info, Warning }

        public class AtlasWarning
        {
            public readonly string Message;
            public readonly Severity Level;

            public AtlasWarning(string message, Severity level)
            {
                Message = message;
                Level = level;
            }
        }

        public class FallbackCoverage
        {
            public readonly string FontName;
            public readonly List<char> Chars;

            public FallbackCoverage(string fontName, List<char> chars)
            {
                FontName = fontName;
                Chars = chars;
            }
        }

        public string AssetName;
        public string AssetType;
        public int TotalChars;
        public int PresentChars;
        public List<char> MissingChars = new List<char>();          // truly missing — not in primary font or any fallback
        public List<FallbackCoverage> Fallbacks = new List<FallbackCoverage>(); // chars rescued by fallback fonts
        public string Error;

        // Dynamic atlas
        public bool IsDynamic;
        public bool? SourceFontCoversAllMissing; // null = source font not assigned

        public List<AtlasWarning> AtlasWarnings = new List<AtlasWarning>();

        public int MissingCount => MissingChars.Count;
        public bool HasError => !string.IsNullOrEmpty(Error);
        public bool HasFallbacks => Fallbacks.Count > 0;

        public int FallbackCoveredCount
        {
            get
            {
                int count = 0;
                foreach (var f in Fallbacks) count += f.Chars.Count;
                return count;
            }
        }

        public void AddWarning(string message, Severity level) =>
            AtlasWarnings.Add(new AtlasWarning(message, level));
    }
}
