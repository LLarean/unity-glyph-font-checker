using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace LLarean.GlyphFontChecker
{
    /// <summary>
    /// Reads Unicode code points directly from an OpenType/TrueType/WOFF font file by parsing
    /// the cmap table binary data. No system font substitution — only glyphs physically
    /// present in the file are returned.
    ///
    /// Supported formats:
    ///   Container:  TTF/OTF (sfnt), TTC (all sub-fonts merged), WOFF
    ///   cmap:       Format 2 (mixed single/double-byte), 4 (BMP segmented), 6 (trimmed),
    ///               10 (trimmed 32-bit), 12 (segmented full Unicode), 13 (many-to-one range)
    ///   Not supported: WOFF2 (Brotli — requires .NET 6+)
    /// </summary>
    public static class FontFileReader
    {
        // OpenType table tags
        private const uint TagCmap = 0x636D6170; // 'cmap'
        private const uint TagTtcf = 0x74746366; // 'ttcf'

        // Container signatures
        private const uint SigWoff  = 0x774F4646; // 'wOFF'
        private const uint SigWoff2 = 0x774F4632; // 'wOF2'

        // Platform / encoding IDs we accept
        private const ushort PlatformWindows = 3;
        private const ushort PlatformUnicode  = 0;
        private const ushort EncWinBmp            = 1;   // BMP → Format 4
        private const ushort EncWinFullUnicode    = 10;  // Full Unicode → Format 12
        private const ushort EncUnicodeBmp        = 3;
        private const ushort EncUnicodeFullUnicode = 4;

        // Priority order for choosing the best encoding record (lower = better)
        private static int EncodingPriority(ushort platform, ushort encoding)
        {
            if (platform == PlatformWindows && encoding == EncWinFullUnicode)      return 0;
            if (platform == PlatformUnicode  && encoding == EncUnicodeFullUnicode)  return 1;
            if (platform == PlatformWindows && encoding == EncWinBmp)              return 2;
            if (platform == PlatformUnicode  && encoding == EncUnicodeBmp)         return 3;
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

                var result = ParseFontFile(reader, out string parseDiag);

                if (result == null)
                {
                    diagnostic = parseDiag ?? (
                        $"No supported Unicode cmap subtable found in:\n  {fontFilePath}\n\n" +
                        "Likely causes:\n" +
                        "  • Font only has a Mac-platform cmap (Format 0) with no Unicode mapping — very rare in modern fonts.\n" +
                        "  • File is corrupted or is not a valid OpenType/TrueType/WOFF font.\n" +
                        "  • Font uses a cmap encoding not supported by this tool.");
                    Debug.LogWarning($"[FontFileReader] {diagnostic}");
                }

                return result;
            }
            catch (Exception ex)
            {
                diagnostic = $"Exception while parsing:\n  {fontFilePath}\n\n" +
                             $"  {ex.GetType().Name}: {ex.Message}\n\n" +
                             "Likely causes:\n" +
                             "  • File is not a valid TTF/OTF/TTC/WOFF (may be WOFF2 or another format).\n" +
                             "  • File is locked by another process.\n" +
                             "  • Unexpected font structure (variable font, font with non-standard tables).";
                Debug.LogWarning($"[FontFileReader] {diagnostic}");
                return null;
            }
        }

        // ── File-level parsing ───────────────────────────────────────────────────

        private static HashSet<int> ParseFontFile(BinaryReader r, out string diagnostic)
        {
            diagnostic = null;
            uint signature = ReadUInt32BE(r);

            if (signature == TagTtcf) return ParseTtc(r);
            if (signature == SigWoff) return ParseWoff(r);

            if (signature == SigWoff2)
            {
                diagnostic =
                    "Font is in WOFF2 format (Brotli-compressed web font).\n\n" +
                    "WOFF2 uses Brotli compression which requires .NET 6+ and is not available\n" +
                    "in the Unity Editor scripting runtime (.NET Standard 2.0).\n\n" +
                    "Action: convert the font to TTF or OTF using FontForge, Transfonter,\n" +
                    "or any online font converter, then reimport into Unity.";
                return null;
            }

            // Single sfnt (TTF / OTF) — rewind and parse
            r.BaseStream.Seek(0, SeekOrigin.Begin);
            return ParseSfnt(r, 0);
        }

        // ── TrueType Collection ──────────────────────────────────────────────────

        /// <summary>
        /// Parses a TrueType Collection and merges code points from all sub-fonts.
        /// Unity may reference any sub-font by name, so the union gives the most
        /// accurate coverage picture.
        /// </summary>
        private static HashSet<int> ParseTtc(BinaryReader r)
        {
            ReadUInt32BE(r); // TTC version
            uint numFonts = ReadUInt32BE(r);
            if (numFonts == 0) return null;

            var offsets = new uint[numFonts];
            for (int i = 0; i < numFonts; i++)
                offsets[i] = ReadUInt32BE(r);

            HashSet<int> merged = null;
            foreach (uint offset in offsets)
            {
                var sub = ParseSfnt(r, offset);
                if (sub == null) continue;

                if (merged == null) merged = sub;
                else merged.UnionWith(sub);
            }
            return merged;
        }

        // ── WOFF (zlib-compressed sfnt tables) ───────────────────────────────────

        /// <summary>
        /// Parses a WOFF file. Table data may be zlib-compressed (RFC 1950);
        /// the cmap table is extracted and decompressed if necessary.
        /// </summary>
        private static HashSet<int> ParseWoff(BinaryReader r)
        {
            // Header (signature already consumed — stream is at byte 4)
            ReadUInt32BE(r); // flavor (original sfnt version)
            ReadUInt32BE(r); // total file length
            ushort numTables = ReadUInt16BE(r);
            ReadUInt16BE(r); // reserved
            ReadUInt32BE(r); // totalSfntSize
            ReadUInt16BE(r); // majorVersion
            ReadUInt16BE(r); // minorVersion
            ReadUInt32BE(r); // metaOffset
            ReadUInt32BE(r); // metaLength
            ReadUInt32BE(r); // metaOrigLength
            ReadUInt32BE(r); // privOffset
            ReadUInt32BE(r); // privLength
            // Stream is now at byte 44 — start of table directory

            long cmapOffset  = -1;
            uint cmapCompLen = 0;
            uint cmapOrigLen = 0;

            for (int i = 0; i < numTables; i++)
            {
                uint tag     = ReadUInt32BE(r);
                uint offset  = ReadUInt32BE(r);
                uint compLen = ReadUInt32BE(r);
                uint origLen = ReadUInt32BE(r);
                ReadUInt32BE(r); // origChecksum

                if (tag == TagCmap)
                {
                    cmapOffset  = offset;
                    cmapCompLen = compLen;
                    cmapOrigLen = origLen;
                    break;
                }
            }

            if (cmapOffset < 0) return null;

            r.BaseStream.Seek(cmapOffset, SeekOrigin.Begin);

            byte[] cmapBytes;
            if (cmapCompLen == cmapOrigLen)
            {
                // Stored uncompressed
                cmapBytes = r.ReadBytes((int)cmapOrigLen);
            }
            else
            {
                // zlib-compressed: skip 2-byte CMF+FLG header, then raw deflate
                r.ReadByte(); // CMF
                r.ReadByte(); // FLG
                cmapBytes = new byte[cmapOrigLen];
                int totalRead = 0;
                using var deflate = new DeflateStream(r.BaseStream, CompressionMode.Decompress, leaveOpen: true);
                while (totalRead < cmapBytes.Length)
                {
                    int n = deflate.Read(cmapBytes, totalRead, cmapBytes.Length - totalRead);
                    if (n == 0) break;
                    totalRead += n;
                }
            }

            // Parse cmap from the in-memory buffer (offset = 0 since buffer IS the cmap table)
            using var ms      = new MemoryStream(cmapBytes);
            using var cmapRdr = new BinaryReader(ms);
            return ParseCmap(cmapRdr, 0);
        }

        // ── Single sfnt ──────────────────────────────────────────────────────────

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
                uint tag    = ReadUInt32BE(r);
                ReadUInt32BE(r); // checksum
                uint offset = ReadUInt32BE(r);
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

            int  bestPriority = int.MaxValue;
            uint bestOffset   = 0;

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
                2  => ParseFormat2(r, subtableStart),
                4  => ParseFormat4(r, subtableStart),
                6  => ParseFormat6(r),
                10 => ParseFormat10(r),
                12 => ParseFormat12(r),
                13 => ParseFormat13(r),
                _  => null
            };
        }

        // ── Format 2 — mixed single/double-byte (some CJK legacy fonts) ──────────

        private static HashSet<int> ParseFormat2(BinaryReader r, long subtableStart)
        {
            ushort length = ReadUInt16BE(r); // total subtable length in bytes (from format field)
            ReadUInt16BE(r);                 // language

            // 256 subHeaderKey values: subHeaderKeys[i] / 8 = index into subHeaders[]
            var subHeaderKeys = new ushort[256];
            for (int i = 0; i < 256; i++) subHeaderKeys[i] = ReadUInt16BE(r);

            int maxSubHeaderIdx = 0;
            for (int i = 0; i < 256; i++)
            {
                int idx = subHeaderKeys[i] / 8;
                if (idx > maxSubHeaderIdx) maxSubHeaderIdx = idx;
            }
            int numSubHeaders = maxSubHeaderIdx + 1;

            // Record stream position of each subHeader's idRangeOffset field for pointer arithmetic
            var firstCode         = new ushort[numSubHeaders];
            var entryCount        = new ushort[numSubHeaders];
            var idDelta           = new short[numSubHeaders];
            var idRangeOffset     = new ushort[numSubHeaders];
            var idRangeOffsetPos  = new long[numSubHeaders]; // stream byte address of the field

            for (int i = 0; i < numSubHeaders; i++)
            {
                firstCode[i]        = ReadUInt16BE(r);
                entryCount[i]       = ReadUInt16BE(r);
                idDelta[i]          = ReadInt16BE(r);
                idRangeOffsetPos[i] = r.BaseStream.Position;
                idRangeOffset[i]    = ReadUInt16BE(r);
            }

            // glyphIdArray: remaining bytes of the subtable
            // subtable = length bytes from start of format field (format already consumed before this method)
            // bytes consumed so far: 2(length) + 2(language) + 512(keys) + numSubHeaders*8
            long glyphArrayStart = r.BaseStream.Position;
            int  consumed        = 2 + 2 + 512 + numSubHeaders * 8;
            int  glyphArrayLen   = Math.Max(0, (length - consumed) / 2);
            var  glyphIdArray    = new ushort[glyphArrayLen];
            for (int i = 0; i < glyphArrayLen; i++) glyphIdArray[i] = ReadUInt16BE(r);

            var result = new HashSet<int>();

            for (int high = 0; high < 256; high++)
            {
                int sh = subHeaderKeys[high] / 8;
                if (entryCount[sh] == 0) continue;

                for (int j = 0; j < entryCount[sh]; j++)
                {
                    int low  = firstCode[sh] + j;
                    int code = (high << 8) | low;

                    int glyphId;
                    if (idRangeOffset[sh] == 0)
                    {
                        glyphId = (low + idDelta[sh]) & 0xFFFF;
                    }
                    else
                    {
                        // Pointer arithmetic: field address + field value + j*2 = byte addr of glyph entry
                        long glyphByteAddr = idRangeOffsetPos[sh] + idRangeOffset[sh] + (long)j * 2;
                        int  arrayIdx      = (int)((glyphByteAddr - glyphArrayStart) / 2);
                        if (arrayIdx < 0 || arrayIdx >= glyphIdArray.Length) continue;

                        glyphId = glyphIdArray[arrayIdx];
                        if (glyphId != 0) glyphId = (glyphId + idDelta[sh]) & 0xFFFF;
                    }

                    if (glyphId != 0) result.Add(code);
                }
            }

            return result;
        }

        // ── Format 4 — BMP segmented mapping (most common) ───────────────────────

        private static HashSet<int> ParseFormat4(BinaryReader r, long subtableStart)
        {
            ushort length   = ReadUInt16BE(r);
            ReadUInt16BE(r); // language
            int segCount    = ReadUInt16BE(r) / 2; // segCountX2 / 2

            ReadUInt16BE(r); // searchRange
            ReadUInt16BE(r); // entrySelector
            ReadUInt16BE(r); // rangeShift

            var endCode  = new ushort[segCount];
            for (int i = 0; i < segCount; i++) endCode[i] = ReadUInt16BE(r);

            ReadUInt16BE(r); // reservedPad

            var startCode     = new ushort[segCount];
            var idDelta       = new short[segCount];
            var idRangeOffset = new ushort[segCount];

            for (int i = 0; i < segCount; i++) startCode[i]    = ReadUInt16BE(r);
            for (int i = 0; i < segCount; i++) idDelta[i]       = ReadInt16BE(r);
            for (int i = 0; i < segCount; i++) idRangeOffset[i] = ReadUInt16BE(r);

            long subtableEnd  = subtableStart + length;
            int  glyphIdCount = (int)((subtableEnd - r.BaseStream.Position) / 2);
            var  glyphIdArray = new ushort[Math.Max(0, glyphIdCount)];
            for (int i = 0; i < glyphIdArray.Length; i++) glyphIdArray[i] = ReadUInt16BE(r);

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

        private static HashSet<int> ParseFormat6(BinaryReader r)
        {
            ReadUInt16BE(r); // length
            ReadUInt16BE(r); // language
            ushort firstCode  = ReadUInt16BE(r);
            ushort entryCount = ReadUInt16BE(r);

            var result = new HashSet<int>();
            for (int i = 0; i < entryCount; i++)
            {
                ushort glyphId = ReadUInt16BE(r);
                if (glyphId != 0) result.Add(firstCode + i);
            }
            return result;
        }

        // ── Format 10 — trimmed array, 32-bit code points ─────────────────────────

        private static HashSet<int> ParseFormat10(BinaryReader r)
        {
            ReadUInt16BE(r);   // reserved
            ReadUInt32BE(r);   // length
            ReadUInt32BE(r);   // language
            uint startChar = ReadUInt32BE(r);
            uint numChars  = ReadUInt32BE(r);

            var result = new HashSet<int>();
            for (uint i = 0; i < numChars; i++)
            {
                ushort glyphId = ReadUInt16BE(r);
                if (glyphId != 0) result.Add((int)(startChar + i));
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

        // ── Format 13 — many-to-one range (last-resort / symbol fonts) ───────────

        private static HashSet<int> ParseFormat13(BinaryReader r)
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
                uint glyphId   = ReadUInt32BE(r);

                if (glyphId == 0) continue; // unmapped range

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
