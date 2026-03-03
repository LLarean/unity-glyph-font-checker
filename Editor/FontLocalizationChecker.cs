using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LLarean.GlyphFontChecker
{
    public class FontLocalizationChecker : EditorWindow
    {
        // TextArea is safe up to ~500 chars; above this we evacuate the text immediately.
        // 2000 is an absolute hard cap to survive one-frame rendering before evacuation kicks in.
        private const int TextAreaSoftLimit = 500;
        private const int TextAreaHardLimit = 2000;

        // Default SDF paddings used for capacity estimation when font.padding == 0
        private const int DefaultPaddingSdf32 = 16;
        private const int DefaultPaddingSdf16 = 9;
        private const int DefaultPaddingSdf    = 5;

        // --- Text source state ---
        private HashSet<char> _chars = new HashSet<char>();
        private string _charSource;
        private int _totalInputLength;

        // --- Input controls ---
        private TextAsset _textAsset;
        private string _manualText = "";
        private Object _fontAsset;

        [MenuItem("Tools/Font Localization Checker")]
        public static void ShowWindow() => GetWindow<FontLocalizationChecker>("Font Checker");

        private void OnGUI()
        {
            DrawTextSourceSection();
            GUILayout.Space(10);
            DrawFontSection();
            GUILayout.Space(10);
            DrawCheckButton();
        }

        // ── Text source ──────────────────────────────────────────────────────────

        private void DrawTextSourceSection()
        {
            EditorGUILayout.LabelField("Text Source", EditorStyles.boldLabel);

            DrawTextAssetField();
            DrawClipboardRow();

            GUILayout.Space(4);
            EditorGUILayout.LabelField("— or type manually —", EditorStyles.centeredGreyMiniLabel);
            DrawManualTextArea();

            if (_chars.Count > 0)
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    $"Source: {_charSource}   |   {_chars.Count} unique chars  (from {_totalInputLength} total)",
                    MessageType.None);
            }
        }

        private void DrawTextAssetField()
        {
            EditorGUI.BeginChangeCheck();
            _textAsset = (TextAsset)EditorGUILayout.ObjectField("Text Asset", _textAsset, typeof(TextAsset), false);
            if (!EditorGUI.EndChangeCheck()) return;

            if (_textAsset != null)
            {
                _manualText = "";
                LoadChars(_textAsset.text, $"TextAsset: {_textAsset.name}");
            }
            else if (_charSource?.StartsWith("TextAsset") == true)
            {
                ClearChars();
            }
        }

        private void DrawClipboardRow()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Paste from Clipboard"))
            {
                _textAsset = null;
                _manualText = "";
                LoadChars(EditorGUIUtility.systemCopyBuffer, "Clipboard");
            }

            GUI.enabled = _chars.Count > 0;
            if (GUILayout.Button("Clear", GUILayout.Width(52)))
                ClearChars();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawManualTextArea()
        {
            EditorGUI.BeginChangeCheck();
            _manualText = EditorGUILayout.TextArea(_manualText, GUILayout.Height(60));
            if (!EditorGUI.EndChangeCheck()) return;

            if (_manualText.Length > TextAreaHardLimit)
                _manualText = _manualText.Substring(0, TextAreaHardLimit);

            if (_manualText.Length > TextAreaSoftLimit)
            {
                LoadChars(_manualText, "Manual");
                _manualText = "";
            }
            else
            {
                LoadChars(_manualText, _manualText.Length > 0 ? "Manual" : null);
            }
        }

        // ── Font asset ───────────────────────────────────────────────────────────

        private void DrawFontSection()
        {
            EditorGUILayout.LabelField("Font Asset (TMP Font or Unity Font)", EditorStyles.boldLabel);
            _fontAsset = EditorGUILayout.ObjectField(_fontAsset, typeof(Object), false);
        }

        // ── Check ────────────────────────────────────────────────────────────────

        private void DrawCheckButton()
        {
            GUI.enabled = _chars.Count > 0 && _fontAsset != null;
            if (GUILayout.Button("Check", GUILayout.Height(30)))
                RunCheck();
            GUI.enabled = true;
        }

        private void RunCheck()
        {
            EditorUtility.DisplayProgressBar("Font Checker", "Validating...", 0.5f);
            var result = Validate(_chars, _fontAsset);
            EditorUtility.ClearProgressBar();
            ResultWindow.Show(result);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void LoadChars(string text, string source)
        {
            _chars = string.IsNullOrEmpty(text)
                ? new HashSet<char>()
                : new HashSet<char>(text.Where(c => !char.IsWhiteSpace(c)));
            _charSource = source;
            _totalInputLength = text?.Length ?? 0;
        }

        private void ClearChars()
        {
            _chars = new HashSet<char>();
            _charSource = null;
            _totalInputLength = 0;
            _textAsset = null;
            _manualText = "";
        }

        // ── Public API ───────────────────────────────────────────────────────────

        public static ValidationResult Validate(HashSet<char> chars, Object asset) => asset switch
        {
            TMP_FontAsset tmp => ValidateTmpFont(chars, tmp),
            Font font         => ValidateUnityFont(chars, font),
            _                 => new ValidationResult { Error = $"Unsupported asset type: {asset?.GetType().Name ?? "null"}" }
        };

        // ── TMP dispatcher ───────────────────────────────────────────────────────

        private static ValidationResult ValidateTmpFont(HashSet<char> chars, TMP_FontAsset font)
        {
            return font.atlasPopulationMode == AtlasPopulationMode.Dynamic
                ? ValidateDynamicTmpFont(chars, font)
                : ValidateStaticTmpFont(chars, font);
        }

        // ── Static TMP ───────────────────────────────────────────────────────────

        private static ValidationResult ValidateStaticTmpFont(HashSet<char> chars, TMP_FontAsset font)
        {
            var result = new ValidationResult
            {
                AssetName  = font.name,
                AssetType  = "TMP Font (Static)",
                TotalChars = chars.Count,
                IsDynamic  = false,
            };

            // Early health check — atlas must be usable before anything else
            CheckStaticAtlasHealth(font, result);

            // characterLookupTable is the ground truth for static atlases.
            var lookup    = font.characterLookupTable;
            var notInAtlas = new List<char>();
            int presentCount = 0;

            foreach (var c in chars)
            {
                if (lookup.ContainsKey(c)) presentCount++;
                else notInAtlas.Add(c);
            }

            result.PresentChars = presentCount;

            // Cross-check chars missing from atlas against the source font file to determine
            // whether they need Regenerate Atlas (in font but not baked) or a different font
            // (not in font file at all).
            var notBaked        = new List<char>();
            var missingFromFile = new List<char>();

            if (notInAtlas.Count > 0)
            {
                if (font.sourceFontFile != null)
                {
                    result.UsedDirectFileRead = true;
                    var codePoints = FontFileReader.ReadCodePoints(font.sourceFontFile, out string fontDiag);
                    if (codePoints != null)
                    {
                        foreach (var c in notInAtlas)
                        {
                            if (codePoints.Contains(c)) notBaked.Add(c);
                            else missingFromFile.Add(c);
                        }
                    }
                    else
                    {
                        // Parse failed — can't distinguish; treat all as missing
                        result.UsedDirectFileRead    = false;
                        result.FontReadDiagnostic    = fontDiag;
                        missingFromFile.AddRange(notInAtlas);
                        result.AddWarning(
                            "Could not read source font file directly — cannot determine whether missing chars " +
                            "need Regenerate Atlas or a different font. See the diagnostic below the results.",
                            ValidationResult.Severity.Warning);
                    }
                }
                else
                {
                    missingFromFile.AddRange(notInAtlas);
                    result.AddWarning(
                        "Source font file is not assigned — cannot determine whether missing characters could be " +
                        "added by regenerating the atlas. " +
                        "Action: assign a .ttf/.otf file to 'Source Font File' in the TMP Font Asset inspector.",
                        ValidationResult.Severity.Warning);
                }
            }

            result.MissingChars        = missingFromFile;
            result.MissingFromFontFile = missingFromFile;
            result.NotBakedIntoAtlas   = notBaked;

            // Fallback coverage for chars that are truly absent from this font's file
            var visited = new HashSet<TMP_FontAsset> { font };
            if (font.fallbackFontAssetTable?.Count > 0 && missingFromFile.Count > 0)
            {
                var (fallbacks, trulyMissing) = CheckFallbacks(missingFromFile, font.fallbackFontAssetTable, visited);
                result.Fallbacks    = fallbacks;
                result.MissingChars = trulyMissing;
            }

            return result;
        }

        // ── Dynamic TMP ──────────────────────────────────────────────────────────

        private static ValidationResult ValidateDynamicTmpFont(HashSet<char> chars, TMP_FontAsset font)
        {
            var    missingFromFile    = new List<char>();
            var    presentInFont      = new List<char>();
            bool   usedDirectRead     = false;
            string fontReadDiagnostic = null;

            if (font.sourceFontFile != null)
            {
                var codePoints = FontFileReader.ReadCodePoints(font.sourceFontFile, out string fontDiag);
                if (codePoints != null)
                {
                    usedDirectRead = true;
                    foreach (var c in chars)
                    {
                        if (codePoints.Contains(c)) presentInFont.Add(c);
                        else missingFromFile.Add(c);
                    }
                }
                else
                {
                    // Parse failed — fall back to HasCharacter (checks current cache only)
                    fontReadDiagnostic = fontDiag;
                    foreach (var c in chars)
                    {
                        if (font.HasCharacter(c, true)) presentInFont.Add(c);
                        else missingFromFile.Add(c);
                    }
                }
            }
            else
            {
                // No source font at all — check cached atlas only
                foreach (var c in chars)
                {
                    if (font.characterLookupTable.ContainsKey(c)) presentInFont.Add(c);
                    else missingFromFile.Add(c);
                }
            }

            var result = new ValidationResult
            {
                AssetName           = font.name,
                AssetType           = "TMP Font (Dynamic)",
                TotalChars          = chars.Count,
                PresentChars        = presentInFont.Count,
                MissingChars        = missingFromFile,
                MissingFromFontFile = missingFromFile,
                IsDynamic           = true,
                UsedDirectFileRead  = usedDirectRead,
                FontReadDiagnostic  = fontReadDiagnostic,
            };

            // Source font missing — most critical issue
            if (font.sourceFontFile == null)
                result.AddWarning(
                    "Source font file is not assigned — dynamic glyph generation is impossible at runtime. " +
                    "The atlas will only render characters that were pre-cached before build. " +
                    "Action: assign a .ttf/.otf file to 'Source Font File' in the TMP Font Asset inspector.",
                    ValidationResult.Severity.Warning);
            else if (!usedDirectRead)
                result.AddWarning(
                    "Source font file could not be parsed directly — fell back to atlas cache check. " +
                    "Characters shown as missing may still render at runtime if the source font contains them. " +
                    "Action: check the Console for the specific parse error from FontFileReader.",
                    ValidationResult.Severity.Warning);

            // Font file bundling — can the source font reach the runtime?
            if (font.sourceFontFile != null)
                CheckFontFileBundling(font.sourceFontFile, result);

            // Atlas capacity, render mode, clear-on-build
            CheckDynamicAtlasSettings(font, result);

            // Estimated glyph capacity vs. current input
            CheckDynamicAtlasCapacity(font, chars.Count, result);

            // Script compatibility for chars that are present in the font
            if (presentInFont.Count > 0)
                CheckScriptCompatibility(new HashSet<char>(presentInFont), font, result);

            // Fallback chain — depth and dynamic fallback performance
            CheckFallbackChain(font, result);

            // Fallback coverage for chars missing from primary font
            var visited = new HashSet<TMP_FontAsset> { font };
            if (font.fallbackFontAssetTable?.Count > 0 && missingFromFile.Count > 0)
            {
                var (fallbacks, trulyMissing) = CheckFallbacks(missingFromFile, font.fallbackFontAssetTable, visited);
                result.Fallbacks    = fallbacks;
                result.MissingChars = trulyMissing;
            }

            return result;
        }

        // ── Unity Font ───────────────────────────────────────────────────────────

        private static ValidationResult ValidateUnityFont(HashSet<char> chars, Font font)
        {
            var codePoints  = FontFileReader.ReadCodePoints(font, out string fontDiag);
            bool directRead = codePoints != null;

            var missing = new List<char>();
            int present = 0;

            foreach (var c in chars)
            {
                bool has = directRead ? codePoints.Contains(c) : font.HasCharacter(c);
                if (has) present++;
                else missing.Add(c);
            }

            var result = new ValidationResult
            {
                AssetName           = font.name,
                AssetType           = "Unity Font",
                TotalChars          = chars.Count,
                PresentChars        = present,
                MissingChars        = missing,
                MissingFromFontFile = missing,
                UsedDirectFileRead  = directRead,
                FontReadDiagnostic  = directRead ? null : fontDiag,
            };

            if (!directRead)
                result.AddWarning(
                    "Could not parse the font file directly — fell back to Unity's HasCharacter() API, " +
                    "which may return true for characters covered by system fallback fonts rather than this font file itself. " +
                    "Action: check the Console for the FontFileReader parse error.",
                    ValidationResult.Severity.Warning);

            // Import settings (charset, rendering mode)
            CheckUnityFontImportSettings(font, chars, result);

            return result;
        }

        // ── Fallback checking ────────────────────────────────────────────────────

        private static (List<ValidationResult.FallbackCoverage>, List<char>) CheckFallbacks(
            List<char> missing,
            List<TMP_FontAsset> fallbackFonts,
            HashSet<TMP_FontAsset> visited)
        {
            var coverages = new List<ValidationResult.FallbackCoverage>();
            var remaining = missing;

            foreach (var fallback in fallbackFonts)
            {
                if (fallback == null || remaining.Count == 0) continue;
                if (!visited.Add(fallback)) continue;

                bool isDynamicFallback = fallback.atlasPopulationMode == AtlasPopulationMode.Dynamic;

                // Dynamic fallback: check source font file directly; static: use lookup table
                Func<char, bool> hasFallbackChar;
                if (isDynamicFallback && fallback.sourceFontFile != null)
                {
                    var codePoints = FontFileReader.ReadCodePoints(fallback.sourceFontFile);
                    hasFallbackChar = codePoints != null
                        ? (Func<char, bool>)(c => codePoints.Contains(c))
                        : c => fallback.HasCharacter(c);
                }
                else
                {
                    hasFallbackChar = c => fallback.characterLookupTable.ContainsKey(c);
                }

                var covered = remaining.Where(hasFallbackChar).ToList();
                if (covered.Count > 0)
                {
                    coverages.Add(new ValidationResult.FallbackCoverage(fallback.name, covered));
                    var coveredSet = new HashSet<char>(covered);
                    remaining = remaining.Where(c => !coveredSet.Contains(c)).ToList();
                }

                if (remaining.Count > 0 && fallback.fallbackFontAssetTable?.Count > 0)
                {
                    var (subCoverages, subRemaining) = CheckFallbacks(remaining, fallback.fallbackFontAssetTable, visited);
                    coverages.AddRange(subCoverages);
                    remaining = subRemaining;
                }
            }

            return (coverages, remaining);
        }

        // ── Static atlas health ──────────────────────────────────────────────────

        private static void CheckStaticAtlasHealth(TMP_FontAsset font, ValidationResult result)
        {
            if (font.atlasTexture == null)
                result.AddWarning(
                    "Atlas texture is null — the font asset has no baked texture and will not render any glyphs. " +
                    "Action: open Window > TextMeshPro > Font Asset Creator, configure the font, and click 'Generate Font Atlas'. " +
                    "Then assign the generated texture to this Font Asset.",
                    ValidationResult.Severity.Warning);

            if (font.characterLookupTable.Count == 0)
                result.AddWarning(
                    "Character lookup table is empty — no glyphs are baked into this atlas. " +
                    "Action: select the Font Asset, open the inspector, set a Character Set and click 'Update Atlas Texture' " +
                    "or use the Font Asset Creator (Window > TextMeshPro > Font Asset Creator).",
                    ValidationResult.Severity.Warning);
        }

        // ── Dynamic atlas — build & capacity checks ───────────────────────────────

        private static void CheckDynamicAtlasSettings(TMP_FontAsset font, ValidationResult result)
        {
            var so = new SerializedObject(font);
            var clearOnBuildProp = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuildProp != null && clearOnBuildProp.boolValue)
                result.AddWarning(
                    "'Clear Dynamic Data On Build' is enabled — all cached glyphs are wiped at build time and must be " +
                    "regenerated at runtime on first use. This causes a visible stutter when glyphs are first rendered. " +
                    "Action: disable this option unless you intentionally need a clean atlas on every build. " +
                    "Ensure the source font file is included in the build (not in an Editor/ folder).",
                    ValidationResult.Severity.Warning);

            bool isSdf = IsSdfMode(font);
            int  area  = font.atlasWidth * font.atlasHeight;

            if (area < 256 * 256)
                result.AddWarning(
                    $"Atlas size {font.atlasWidth}×{font.atlasHeight} = {area / 1024}k pixels — extremely small, " +
                    "almost guaranteed to overflow with any real character set. " +
                    "Action: increase to at least 512×512 (Latin) or 1024×1024 (CJK/complex scripts).",
                    ValidationResult.Severity.Warning);
            else if (area < 512 * 512)
                result.AddWarning(
                    isSdf
                        ? $"Atlas {font.atlasWidth}×{font.atlasHeight} is small. Render mode '{font.atlasRenderMode}' " +
                          "adds significant per-glyph padding, further reducing effective capacity. " +
                          "Action: increase to 512×512 or larger, or reduce the sample point size."
                        : $"Atlas {font.atlasWidth}×{font.atlasHeight} is relatively small — monitor for overflow " +
                          "with large character sets. Action: increase to 512×512 or larger if needed.",
                    isSdf ? ValidationResult.Severity.Warning : ValidationResult.Severity.Info);

            float utilization = CalculateAtlasUtilization(font);

            if (!font.isMultiAtlasTexturesEnabled)
                result.AddWarning(
                    utilization > 0.8f
                        ? $"Multi-atlas is disabled and the atlas is already {utilization:P0} full — " +
                          "new glyphs will silently fail to render once it overflows. " +
                          "Action: enable 'Multi Atlas Textures' in the Font Asset inspector, or increase the atlas size."
                        : $"Multi-atlas is disabled — atlas is {utilization:P0} full. " +
                          "Glyphs will fail to render if the atlas fills up. " +
                          "Action: enable 'Multi Atlas Textures' or increase the atlas size.",
                    ValidationResult.Severity.Warning);
            else if (utilization > 0.9f)
                result.AddWarning(
                    $"Atlas is {utilization:P0} full. A new texture page will be allocated on overflow " +
                    "(multi-atlas is enabled), which increases memory use. " +
                    "Action: consider increasing the atlas size to delay the next allocation.",
                    ValidationResult.Severity.Info);
        }

        // ── Dynamic atlas — capacity estimation ──────────────────────────────────

        private static void CheckDynamicAtlasCapacity(TMP_FontAsset font, int inputCharsCount, ValidationResult result)
        {
            int pointSize = Mathf.RoundToInt(font.faceInfo.pointSize);
            if (pointSize <= 0) return;

            bool isSdf   = IsSdfMode(font);
            var  so      = new SerializedObject(font);
            var  padProp = so.FindProperty("m_Padding") ?? so.FindProperty("m_AtlasPadding");
            int  padding = padProp != null ? padProp.intValue : 0;
            if (padding == 0 && isSdf)
                padding = EstimateDefaultSdfPadding(font);

            int cellSize = pointSize + padding * 2;
            if (cellSize <= 0) return;

            int glyphsPerRow = font.atlasWidth  / cellSize;
            int glyphsPerCol = font.atlasHeight / cellSize;
            int estimatedCap = glyphsPerRow * glyphsPerCol;
            if (estimatedCap <= 0) return;

            int currentCached = font.glyphTable?.Count ?? 0;
            int afterAdding   = currentCached + inputCharsCount;

            // Show info if approaching 60 %, warning if over 80 %
            float fillRatio = (float)afterAdding / estimatedCap;
            if (fillRatio < 0.6f) return;

            string paddingNote = isSdf ? $" + {padding}px SDF padding" : "";
            string capacityLine = $"Estimated atlas capacity: ~{estimatedCap} glyphs " +
                                  $"({pointSize}pt{paddingNote} → {cellSize}px cell in {font.atlasWidth}×{font.atlasHeight}). " +
                                  $"Currently cached: {currentCached}. Input adds: {inputCharsCount}. " +
                                  $"Estimated total: {afterAdding} ({fillRatio:P0} of capacity).";

            if (fillRatio >= 1.0f)
            {
                result.AddWarning(
                    capacityLine + " OVERFLOW LIKELY — new glyphs will fail to render. " +
                    "Action: increase atlas size, enable Multi Atlas Textures, or reduce sample point size " +
                    $"(current: {pointSize}pt). Halving point size roughly quadruples glyph capacity.",
                    ValidationResult.Severity.Warning);
            }
            else if (fillRatio >= 0.8f)
            {
                result.AddWarning(
                    capacityLine + " Atlas is getting full — monitor for overflow. " +
                    "Action: increase atlas size or enable Multi Atlas Textures before adding more characters.",
                    ValidationResult.Severity.Warning);
            }
            else
            {
                result.AddWarning(
                    capacityLine + " Atlas is approaching 60% — no immediate concern.",
                    ValidationResult.Severity.Info);
            }
        }

        // ── Font file bundling ────────────────────────────────────────────────────

        private static void CheckFontFileBundling(Font sourceFontFile, ValidationResult result)
        {
            string assetPath = AssetDatabase.GetAssetPath(sourceFontFile);
            if (string.IsNullOrEmpty(assetPath)) return;

            // Editor-only folder — definitive exclusion from builds
            bool inEditorFolder = assetPath.Contains("/Editor/") ||
                                  assetPath.StartsWith("Editor/");
            if (inEditorFolder)
            {
                result.AddWarning(
                    $"Source font file is inside an Editor-only folder ('{assetPath}'). " +
                    "It will NOT be included in player builds — dynamic glyph generation will silently fail at runtime. " +
                    "Action: move the .ttf/.otf file outside of any 'Editor' folder, for example to 'Assets/Fonts/'.",
                    ValidationResult.Severity.Warning);
                return;
            }

            // Not in Resources or StreamingAssets — may be fine if referenced, but worth noting
            bool inResources       = assetPath.Contains("/Resources/");
            bool inStreamingAssets = assetPath.Contains("/StreamingAssets/");
            bool inPackages        = assetPath.StartsWith("Packages/");

            if (!inResources && !inStreamingAssets && !inPackages)
                result.AddWarning(
                    $"Source font file ('{assetPath}') is not in Resources/ or StreamingAssets/. " +
                    "It will only be included in the build if something in a scene, prefab, or ScriptableObject " +
                    "directly references this TMP Font Asset. " +
                    "Action: verify the font asset is reachable from a loaded scene or is included in an Addressables group. " +
                    "If using Addressables, confirm the font asset (not just its atlas) is added to a group.",
                    ValidationResult.Severity.Info);
        }

        // ── Fallback chain analysis ───────────────────────────────────────────────

        private static void CheckFallbackChain(TMP_FontAsset root, ValidationResult result)
        {
            if (root.fallbackFontAssetTable == null || root.fallbackFontAssetTable.Count == 0)
                return;

            var dynamicFallbacks = new List<string>();
            int depth = MeasureFallbackChain(root, new HashSet<TMP_FontAsset>(), dynamicFallbacks);

            if (depth > 3)
                result.AddWarning(
                    $"Fallback chain is {depth} levels deep. Each level is traversed every frame for every text component " +
                    "that needs a missing glyph — long chains add per-frame CPU cost. " +
                    "Action: flatten the fallback chain to 3 levels or fewer, or merge fallback fonts into a single atlas.",
                    ValidationResult.Severity.Info);

            if (dynamicFallbacks.Count > 0)
                result.AddWarning(
                    $"Dynamic fallback font(s) in chain: {string.Join(", ", dynamicFallbacks)}. " +
                    "Each character missing from the primary atlas will trigger a glyph generation pass on these fallbacks at runtime, " +
                    "causing stutter on first use. " +
                    "Action: pre-bake these fallbacks as Static atlases with the expected character set, " +
                    "or populate the dynamic fallbacks during a loading screen before gameplay.",
                    ValidationResult.Severity.Info);
        }

        private static int MeasureFallbackChain(TMP_FontAsset font, HashSet<TMP_FontAsset> visited, List<string> dynamicFallbackNames)
        {
            if (!visited.Add(font)) return 0;

            int maxChildDepth = 0;
            foreach (var fb in font.fallbackFontAssetTable ?? new List<TMP_FontAsset>())
            {
                if (fb == null) continue;
                if (fb.atlasPopulationMode == AtlasPopulationMode.Dynamic && !dynamicFallbackNames.Contains(fb.name))
                    dynamicFallbackNames.Add(fb.name);

                int childDepth = MeasureFallbackChain(fb, visited, dynamicFallbackNames);
                if (childDepth + 1 > maxChildDepth)
                    maxChildDepth = childDepth + 1;
            }
            return maxChildDepth;
        }

        // ── Script compatibility ──────────────────────────────────────────────────

        private static void CheckScriptCompatibility(HashSet<char> presentChars, TMP_FontAsset font, ValidationResult result)
        {
            bool isBitmap  = !IsSdfMode(font);
            int  area      = font.atlasWidth * font.atlasHeight;

            // ── CJK ──────────────────────────────────────────────────────────────
            int cjkCount = presentChars.Count(IsCjk);
            if (cjkCount > 0)
            {
                if (area < 1024 * 1024)
                    result.AddWarning(
                        $"{cjkCount} CJK/Japanese/Korean character(s) detected. " +
                        $"Atlas {font.atlasWidth}×{font.atlasHeight} is likely insufficient — " +
                        "CJK glyphs are large and there are thousands of them in typical use. " +
                        "Action: increase atlas to at least 1024×1024 (prefer 2048×2048 for broad CJK support), " +
                        "enable Multi Atlas Textures, and consider using a pre-baked Static atlas " +
                        "with only the specific characters needed for your game.",
                        ValidationResult.Severity.Warning);

                if (cjkCount > 100)
                    result.AddWarning(
                        $"Input contains {cjkCount} CJK characters. Storing all of them in a single dynamic atlas at " +
                        $"{Mathf.RoundToInt(font.faceInfo.pointSize)}pt would require a very large texture. " +
                        "Action: strongly consider a Static atlas pre-baked with exactly the characters used in your game. " +
                        "Use Window > TextMeshPro > Font Asset Creator with a curated character set.",
                        ValidationResult.Severity.Info);

                if (isBitmap)
                    result.AddWarning(
                        "Bitmap render mode with CJK: quality will degrade at text sizes different from the baked size. " +
                        "Action: switch to SDF or SDF8 render mode for resolution-independent CJK rendering.",
                        ValidationResult.Severity.Info);
            }

            // ── Thai ─────────────────────────────────────────────────────────────
            if (presentChars.Any(c => c >= 0x0E00 && c <= 0x0E7F))
                result.AddWarning(
                    "Thai script detected. Standard TMP does not support complex script shaping: " +
                    "tone marks, vowel signs, and other combining characters will not stack or position correctly. " +
                    "Action: install the TextShaper package (com.unity.textmeshpro.textshaper) " +
                    "or use a third-party solution (e.g. RTL-TMPro, ThaiTextShaper). " +
                    "Test with real Thai sentences to confirm correct rendering.",
                    ValidationResult.Severity.Warning);

            // ── Arabic / Persian / Urdu ───────────────────────────────────────────
            if (presentChars.Any(c => (c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F) || (c >= 0xFB50 && c <= 0xFDFF)))
                result.AddWarning(
                    "Arabic/Persian/Urdu script detected. Standard TMP does not support RTL text direction, " +
                    "contextual glyph shaping (initial/medial/final/isolated forms), or ligatures. " +
                    "Text will appear left-to-right with isolated letter forms — completely unusable for production. " +
                    "Action: install the TextShaper package (com.unity.textmeshpro.textshaper) " +
                    "or use RTL-TMPro (available on GitHub). Verify Arabic ligatures render correctly after setup.",
                    ValidationResult.Severity.Warning);

            // ── Hebrew / Syriac ───────────────────────────────────────────────────
            if (presentChars.Any(c => (c >= 0x0590 && c <= 0x05FF) || (c >= 0x0700 && c <= 0x074F)))
                result.AddWarning(
                    "Hebrew or Syriac script detected. Standard TMP does not support RTL text direction " +
                    "or proper placement of cantillation marks and vowel points (nikud). " +
                    "Action: install the TextShaper package or use an RTL-capable plugin. " +
                    "Ensure your font includes all required nikud glyphs (U+05B0–U+05C7) if used.",
                    ValidationResult.Severity.Warning);

            // ── Indic scripts ─────────────────────────────────────────────────────
            bool hasIndic = presentChars.Any(c =>
                (c >= 0x0900 && c <= 0x097F) ||  // Devanagari (Hindi, Sanskrit, Marathi)
                (c >= 0x0980 && c <= 0x09FF) ||  // Bengali
                (c >= 0x0A00 && c <= 0x0A7F) ||  // Gurmukhi (Punjabi)
                (c >= 0x0A80 && c <= 0x0AFF) ||  // Gujarati
                (c >= 0x0B00 && c <= 0x0B7F) ||  // Odia
                (c >= 0x0B80 && c <= 0x0BFF) ||  // Tamil
                (c >= 0x0C00 && c <= 0x0C7F) ||  // Telugu
                (c >= 0x0C80 && c <= 0x0CFF) ||  // Kannada
                (c >= 0x0D00 && c <= 0x0D7F));    // Malayalam

            if (hasIndic)
                result.AddWarning(
                    "Indic script detected (Devanagari/Bengali/Tamil/Telugu/Kannada/Malayalam/Gujarati/Gurmukhi/Odia). " +
                    "Standard TMP does not support complex Indic shaping: conjunct consonants (ligatures), " +
                    "matras (vowel signs), and reordering of characters will not work correctly. " +
                    "Text will appear as isolated unshaped glyphs. " +
                    "Action: install the TextShaper package (com.unity.textmeshpro.textshaper). " +
                    "Test with actual language text to verify conjuncts are formed correctly.",
                    ValidationResult.Severity.Warning);
        }

        private static bool IsCjk(char c) =>
            (c >= 0x4E00 && c <= 0x9FFF)  ||  // CJK Unified Ideographs
            (c >= 0x3400 && c <= 0x4DBF)  ||  // CJK Extension A
            (c >= 0xF900 && c <= 0xFAFF)  ||  // CJK Compatibility Ideographs
            (c >= 0x3040 && c <= 0x30FF)  ||  // Hiragana + Katakana
            (c >= 0xAC00 && c <= 0xD7AF)  ||  // Hangul Syllables
            (c >= 0x3000 && c <= 0x303F);     // CJK Symbols & Punctuation

        // ── Unity Font import settings ────────────────────────────────────────────

        private static void CheckUnityFontImportSettings(Font font, HashSet<char> chars, ValidationResult result)
        {
            string path     = AssetDatabase.GetAssetPath(font);
            var    importer = AssetImporter.GetAtPath(path) as TrueTypeFontImporter;
            if (importer == null) return;

            switch (importer.fontTextureCase)
            {
                case FontTextureCase.ASCII:
                    result.AddWarning(
                        "Font import is set to 'ASCII' character set — only basic Latin characters (U+0020–U+007E) " +
                        "are baked. All non-ASCII characters (accented Latin, Cyrillic, CJK, etc.) will not render. " +
                        "Action: select the font in the Project window, change 'Character' to 'Unicode' or " +
                        "'Custom Set' in the Inspector, then click Apply.",
                        ValidationResult.Severity.Warning);
                    break;

                case FontTextureCase.CustomSet:
                    var customSet = new HashSet<char>(importer.customCharacters ?? string.Empty);
                    var missingFromCustom = chars.Where(c => !customSet.Contains(c)).ToList();
                    if (missingFromCustom.Count > 0)
                        result.AddWarning(
                            $"Font uses a 'Custom Set' of {customSet.Count} characters, but {missingFromCustom.Count} " +
                            "of the input characters are not in that set: " +
                            $"{string.Join(" ", missingFromCustom.OrderBy(c => c).Take(20))}" +
                            (missingFromCustom.Count > 20 ? $" … (+{missingFromCustom.Count - 20} more)" : "") + ". " +
                            "Action: select the font, add the missing characters to the 'Custom Characters' field " +
                            "in the Inspector, and click Apply.",
                            ValidationResult.Severity.Warning);
                    break;
            }

            if (importer.fontRenderingMode == FontRenderingMode.OSDefault)
                result.AddWarning(
                    "Font rendering mode is 'OS Default' — glyph rendering may look inconsistent across platforms " +
                    "(Windows ClearType vs macOS sub-pixel AA vs Linux grayscale). " +
                    "Action: consider setting 'Rendering Mode' to 'Smooth' for consistent cross-platform appearance.",
                    ValidationResult.Severity.Info);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsSdfMode(TMP_FontAsset font) =>
            font.atlasRenderMode.ToString().IndexOf("SDF", StringComparison.OrdinalIgnoreCase) >= 0;

        private static int EstimateDefaultSdfPadding(TMP_FontAsset font)
        {
            string mode = font.atlasRenderMode.ToString().ToUpperInvariant();
            if (mode.Contains("SDF32")) return DefaultPaddingSdf32;
            if (mode.Contains("SDF16")) return DefaultPaddingSdf16;
            return DefaultPaddingSdf;
        }

        private static float CalculateAtlasUtilization(TMP_FontAsset font)
        {
            int totalArea = font.atlasWidth * font.atlasHeight;
            if (totalArea <= 0) return 0f;

            int usedArea = 0;
            foreach (var glyph in font.glyphTable)
                usedArea += glyph.glyphRect.width * glyph.glyphRect.height;

            return (float)usedArea / totalArea;
        }
    }
}
