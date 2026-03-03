using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LLarean.GlyphFontChecker
{
    /// <summary>
    /// Reads Unicode code points directly from an OpenType/TrueType font file by parsing
    /// the cmap table binary data. No system font substitution — only glyphs physically
    /// present in the file are returned.
    /// </summary>
    public static class FontFileReader
    {
        // OpenType table tags
        private const uint TagCmap = 0x636D6170; // 'cmap'
        private const uint TagTtcf = 0x74746366; // 'ttcf'

        // Platform / encoding IDs we accept
        private const ushort PlatformWindows = 3;
        private const ushort PlatformUnicode  = 0;
        private const ushort EncWinBmp           = 1;   // BMP → Format 4
        private const ushort EncWinFullUnicode   = 10;  // Full Unicode → Format 12
        private const ushort EncUnicodeBmp       = 3;
        private const ushort EncUnicodeFullUnicode = 4;

        // Priority order for choosing the best encoding record (lower = better)
        private static int EncodingPriority(ushort platform, ushort encoding)
        {
            if (platform == PlatformWindows && encoding == EncWinFullUnicode)    return 0;
            if (platform == PlatformUnicode  && encoding == EncUnicodeFullUnicode) return 1;
            if (platform == PlatformWindows && encoding == EncWinBmp)            return 2;
            if (platform == PlatformUnicode  && encoding == EncUnicodeBmp)       return 3;
            return int.MaxValue; // unsupported — skip
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all Unicode code points physically present in the Unity Font's source
        /// file, or null if the file cannot be located or parsed.
        /// </summary>
        public static HashSet<int> ReadCodePoints(Font unityFont) =>
            ReadCodePoints(unityFont, out _);

        /// <summary>
        /// Same as <see cref="ReadCodePoints(Font)"/> but also returns a human-readable
        /// <paramref name="diagnostic"/> message when the read fails (null on success).
        /// </summary>
        public static HashSet<int> ReadCodePoints(Font unityFont, out string diagnostic)
        {
            diagnostic = null;
            if (unityFont == null) return null;

            string assetPath = AssetDatabase.GetAssetPath(unityFont);
            if (string.IsNullOrEmpty(assetPath))
            {
                diagnostic = $"Asset path is empty for font '{unityFont.name}'. " +
                             "The font may not be a saved project asset (e.g. it was created at runtime).";
                Debug.LogWarning($"[FontFileReader] {diagnostic}");
                return null;
            }

            // Resolve relative to the project root (handles both Assets/ and Packages/ paths)
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string fullPath    = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            return ReadCodePoints(fullPath, out diagnostic);
        }

        /// <summary>
        /// Returns all Unicode code points physically present in the font file at the
        /// given absolute path, or null if the file cannot be read or parsed.
        /// </summary>
        public static HashSet<int> ReadCodePoints(string fontFilePath) =>
            ReadCodePoints(fontFilePath, out _);

        /// <summary>
        /// Same as <see cref="ReadCodePoints(string)"/> but also returns a human-readable
        /// <paramref name="diagnostic"/> message when the read fails (null on success).
        /// </summary>
        public static HashSet<int> ReadCodePoints(string fontFilePath, out string diagnostic)
        {
            diagnostic = null;

            if (string.IsNullOrEmpty(fontFilePath))
                return null;

            if (!File.Exists(fontFilePath))
            {
                diagnostic = $"Font file not found at resolved path:\n  {fontFilePath}\n\n" +
                             "Likely causes:\n" +
                             "  • Font is in a Packages/ folder cached outside the project root " +
                               "(e.g. Library/PackageCache). Package fonts are included in builds automatically " +
                               "but cannot be read from disk by this tool.\n" +
                             "  • Path contains special characters that were not resolved correctly.\n" +
                             "  • The font asset was moved or deleted from the file system.";
                Debug.LogWarning($"[FontFileReader] {diagnostic}");
                return null;
            }

            try
            {
                using var stream = new FileStream(fontFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(stream);
                var result = ParseFontFile(reader);
                if (result == null)
                {
                    diagnostic = $"No supported Unicode cmap subtable found in:\n  {fontFilePath}\n\n" +
                                 "Likely causes:\n" +
                                 "  • Font uses an unsupported format: WOFF or WOFF2 (web fonts). " +
                                   "Convert to TTF/OTF using FontForge or a web tool.\n" +
                                 "  • Font only has a Mac-platform cmap (Format 0/2) with no Unicode mapping. " +
                                   "Very rare in modern fonts.\n" +
                                 "  • File is corrupted or is not a valid OpenType/TrueType font.";
                    Debug.LogWarning($"[FontFileReader] {diagnostic}");
                }
                return result;
            }
            catch (Exception ex)
            {
                diagnostic = $"Exception while parsing:\n  {fontFilePath}\n\n" +
                             $"  {ex.GetType().Name}: {ex.Message}\n\n" +
                             "Likely causes:\n" +
                             "  • File is not a valid TTF/OTF/TTC (may be WOFF, WOFF2, or another format).\n" +
                             "  • File is locked by another process.\n" +
                             "  • Unexpected font structure (variable font, font with non-standard tables).";
                Debug.LogWarning($"[FontFileReader] {diagnostic}");
                return null;
            }
        }

        // ── File-level parsing ───────────────────────────────────────────────────

        private static HashSet<int> ParseFontFile(BinaryReader r)
        {
            uint signature = ReadUInt32BE(r);

            if (signature == TagTtcf)
                return ParseTtc(r);

            // Single font — rewind and parse as sfnt
            r.BaseStream.Seek(0, SeekOrigin.Begin);
            return ParseSfnt(r, 0);
        }

        /// <summary>TrueType Collection — use the first sub-font in the collection.</summary>
        private static HashSet<int> ParseTtc(BinaryReader r)
        {
            ReadUInt32BE(r); // TTC version
            uint numFonts = ReadUInt32BE(r);
            if (numFonts == 0) return null;

            uint offset = ReadUInt32BE(r); // offset of first sub-font
            r.BaseStream.Seek(offset, SeekOrigin.Begin);
            return ParseSfnt(r, offset);
        }

        /// <summary>
        /// Parse a single sfnt (TrueType or CFF OpenType) starting at <paramref name="sfntOffset"/>.
        /// </summary>
        private static HashSet<int> ParseSfnt(BinaryReader r, long sfntOffset)
        {
            r.BaseStream.Seek(sfntOffset, SeekOrigin.Begin);
            ReadUInt32BE(r); // sfVersion: 0x00010000 (TT) or 'OTTO' (CFF)

            ushort numTables = ReadUInt16BE(r);
            ReadUInt16BE(r); // searchRange
            ReadUInt16BE(r); // entrySelector
            ReadUInt16BE(r); // rangeShift

            long cmapOffset = -1;
            for (int i = 0; i < numTables; i++)
            {
                uint   tag    = ReadUInt32BE(r);
                ReadUInt32BE(r); // checksum
                uint   offset = ReadUInt32BE(r);
                ReadUInt32BE(r); // length

                if (tag == TagCmap)
                {
                    cmapOffset = sfntOffset + offset;
                    break;
                }
            }

            if (cmapOffset < 0) return null;
            return ParseCmap(r, cmapOffset);
        }

        // ── cmap table ───────────────────────────────────────────────────────────

        private static HashSet<int> ParseCmap(BinaryReader r, long cmapStart)
        {
            r.BaseStream.Seek(cmapStart, SeekOrigin.Begin);
            ReadUInt16BE(r); // version (always 0)
            ushort numTables = ReadUInt16BE(r);

            int    bestPriority = int.MaxValue;
            uint   bestOffset   = 0;

            for (int i = 0; i < numTables; i++)
            {
                ushort platform = ReadUInt16BE(r);
                ushort encoding = ReadUInt16BE(r);
                uint   offset   = ReadUInt32BE(r);

                int priority = EncodingPriority(platform, encoding);
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    bestOffset   = offset;
                }
            }

            if (bestOffset == 0) return null;

            long subtableStart = cmapStart + bestOffset;
            r.BaseStream.Seek(subtableStart, SeekOrigin.Begin);
            ushort format = ReadUInt16BE(r);

            return format switch
            {
                4  => ParseFormat4(r, subtableStart),
                6  => ParseFormat6(r),
                12 => ParseFormat12(r),
                _  => null
            };
        }

        // ── Format 4 — BMP segmented mapping (most common) ───────────────────────

        private static HashSet<int> ParseFormat4(BinaryReader r, long subtableStart)
        {
            ushort length     = ReadUInt16BE(r);
            ReadUInt16BE(r);  // language
            int    segCount   = ReadUInt16BE(r) / 2; // segCountX2 / 2

            ReadUInt16BE(r);  // searchRange
            ReadUInt16BE(r);  // entrySelector
            ReadUInt16BE(r);  // rangeShift

            var endCode  = new ushort[segCount];
            for (int i = 0; i < segCount; i++) endCode[i] = ReadUInt16BE(r);

            ReadUInt16BE(r);  // reservedPad

            var startCode     = new ushort[segCount];
            var idDelta       = new short[segCount];
            var idRangeOffset = new ushort[segCount];

            for (int i = 0; i < segCount; i++) startCode[i]     = ReadUInt16BE(r);
            for (int i = 0; i < segCount; i++) idDelta[i]        = ReadInt16BE(r);
            for (int i = 0; i < segCount; i++) idRangeOffset[i]  = ReadUInt16BE(r);

            // glyphIdArray occupies the remainder of the subtable
            long subtableEnd   = subtableStart + length;
            int  glyphIdCount  = (int)((subtableEnd - r.BaseStream.Position) / 2);
            var  glyphIdArray  = new ushort[Math.Max(0, glyphIdCount)];
            for (int i = 0; i < glyphIdArray.Length; i++)
                glyphIdArray[i] = ReadUInt16BE(r);

            var result = new HashSet<int>();

            for (int i = 0; i < segCount - 1; i++) // skip last sentinel (0xFFFF)
            {
                for (int c = startCode[i]; c <= endCode[i]; c++)
                {
                    int glyphId;
                    if (idRangeOffset[i] == 0)
                    {
                        glyphId = (c + idDelta[i]) & 0xFFFF;
                    }
                    else
                    {
                        int idx = (idRangeOffset[i] / 2) + (c - startCode[i]) - (segCount - i);
                        glyphId = (idx >= 0 && idx < glyphIdArray.Length) ? glyphIdArray[idx] : 0;
                        if (glyphId != 0) glyphId = (glyphId + idDelta[i]) & 0xFFFF;
                    }

                    if (glyphId != 0) result.Add(c);
                }
            }

            return result;
        }

        // ── Format 6 — trimmed table mapping ─────────────────────────────────────
        // Used by some CJK sub-fonts and older Mac fonts. Covers a contiguous range.

        private static HashSet<int> ParseFormat6(BinaryReader r)
        {
            ReadUInt16BE(r);  // length
            ReadUInt16BE(r);  // language
            ushort firstCode  = ReadUInt16BE(r);
            ushort entryCount = ReadUInt16BE(r);

            var result = new HashSet<int>();
            for (int i = 0; i < entryCount; i++)
            {
                ushort glyphId = ReadUInt16BE(r);
                if (glyphId != 0)
                    result.Add(firstCode + i);
            }
            return result;
        }

        // ── Format 12 — segmented coverage, full Unicode (32-bit) ─────────────────

        private static HashSet<int> ParseFormat12(BinaryReader r)
        {
            ReadUInt16BE(r);   // reserved
            ReadUInt32BE(r);   // length
            ReadUInt32BE(r);   // language
            uint numGroups = ReadUInt32BE(r);

            var result = new HashSet<int>();
            for (uint g = 0; g < numGroups; g++)
            {
                uint startChar = ReadUInt32BE(r);
                uint endChar   = ReadUInt32BE(r);
                ReadUInt32BE(r); // startGlyphID

                for (uint c = startChar; c <= endChar; c++)
                    result.Add((int)c);
            }
            return result;
        }

        // ── Big-endian binary helpers ────────────────────────────────────────────

        private static ushort ReadUInt16BE(BinaryReader r)
        {
            byte b0 = r.ReadByte(), b1 = r.ReadByte();
            return (ushort)((b0 << 8) | b1);
        }

        private static short ReadInt16BE(BinaryReader r)
        {
            byte b0 = r.ReadByte(), b1 = r.ReadByte();
            return (short)((b0 << 8) | b1);
        }

        private static uint ReadUInt32BE(BinaryReader r)
        {
            byte b0 = r.ReadByte(), b1 = r.ReadByte(), b2 = r.ReadByte(), b3 = r.ReadByte();
            return (uint)((b0 << 24) | (b1 << 16) | (b2 << 8) | b3);
        }
    }
}
