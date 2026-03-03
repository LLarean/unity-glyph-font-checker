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

        public class CharAtlasIssue
        {
            public readonly char Character;
            public readonly string Reason;

            public CharAtlasIssue(char character, string reason)
            {
                Character = character;
                Reason = reason;
            }
        }

        public string AssetName;
        public string AssetType;
        public int TotalChars;
        public int PresentChars;

        /// <summary>
        /// Characters that won't render in the current setup (final verdict — union of
        /// MissingFromFontFile and, for static TMP, chars missing from atlas but also
        /// absent from source font).
        /// </summary>
        public List<char> MissingChars = new List<char>();

        /// <summary>
        /// Characters physically absent from the font file.
        /// These cannot be rendered regardless of atlas settings → a different font is needed.
        /// </summary>
        public List<char> MissingFromFontFile = new List<char>();

        /// <summary>
        /// For static TMP only: characters present in the source font file but not yet baked
        /// into the atlas. Re-generating the atlas (Regenerate Atlas) will fix these.
        /// </summary>
        public List<char> NotBakedIntoAtlas = new List<char>();

        /// <summary>
        /// For dynamic TMP only: characters present in the source font file but with potential
        /// atlas compatibility issues (script shaping, atlas size, render mode).
        /// The character WILL render if the atlas has enough space.
        /// </summary>
        public List<CharAtlasIssue> InFontWithAtlasIssues = new List<CharAtlasIssue>();

        /// <summary>Characters rescued by fallback fonts.</summary>
        public List<FallbackCoverage> Fallbacks = new List<FallbackCoverage>();

        public string Error;

        /// <summary>
        /// Diagnostic message from FontFileReader when direct file parsing failed.
        /// Shown in the result window so the user doesn't need to open the Console.
        /// </summary>
        public string FontReadDiagnostic;

        // Dynamic atlas
        public bool IsDynamic;

        /// <summary>
        /// True  = FontFileReader confirmed source font has all missing chars (runtime-safe).
        /// False = source font is missing some chars.
        /// Null  = source font not assigned or could not be read.
        /// Used only when FontFileReader fails and we fall back to HasCharacter().
        /// </summary>
        public bool? SourceFontCoversAllMissing;

        /// <summary>
        /// True when FontFileReader was used for the check.
        /// False when we fell back to Unity's HasCharacter() API (may include substituted glyphs).
        /// </summary>
        public bool UsedDirectFileRead;

        public List<AtlasWarning> AtlasWarnings = new List<AtlasWarning>();

        // ── Derived counts ───────────────────────────────────────────────────────

        public int MissingCount                => MissingChars.Count;
        public int MissingFromFontFileCount    => MissingFromFontFile.Count;
        public int NotBakedIntoAtlasCount      => NotBakedIntoAtlas.Count;
        public bool HasError                   => !string.IsNullOrEmpty(Error);
        public bool HasFallbacks               => Fallbacks.Count > 0;

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
