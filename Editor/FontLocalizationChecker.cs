using System;
using System.Collections.Generic;
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

        // --- Text source state ---
        private HashSet<char> _chars = new();
        private string _charSource;   // displayed label: "TextAsset: foo", "Clipboard", "Manual"
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

            // Hard cap: prevent crashes from large one-frame renders
            if (_manualText.Length > TextAreaHardLimit)
                _manualText = _manualText[..TextAreaHardLimit];

            if (_manualText.Length > TextAreaSoftLimit)
            {
                // Evacuate large text out of the TextArea immediately
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

        // ── Validators ───────────────────────────────────────────────────────────

        private static ValidationResult ValidateTmpFont(HashSet<char> chars, TMP_FontAsset font)
        {
            var (present, notInPrimary) = PartitionChars(chars, c => font.HasCharacter(c));
            bool isDynamic = font.atlasPopulationMode == AtlasPopulationMode.Dynamic;

            var visited = new HashSet<TMP_FontAsset> { font };
            var (fallbacks, trulyMissing) = font.fallbackFontAssetTable?.Count > 0
                ? CheckFallbacks(notInPrimary, font.fallbackFontAssetTable, visited)
                : (new List<ValidationResult.FallbackCoverage>(), notInPrimary);

            var result = new ValidationResult
            {
                AssetName    = font.name,
                AssetType    = "TMP Font",
                TotalChars   = chars.Count,
                PresentChars = present,
                MissingChars = trulyMissing,
                Fallbacks    = fallbacks,
                IsDynamic    = isDynamic,
            };

            if (!isDynamic) return result;

            CheckDynamicAtlasSettings(font, result);

            if (trulyMissing.Count > 0 && font.sourceFontFile != null)
                result.SourceFontCoversAllMissing = trulyMissing.All(c => font.sourceFontFile.HasCharacter(c));

            return result;
        }

        private static ValidationResult ValidateUnityFont(HashSet<char> chars, Font font)
        {
            var (present, missing) = PartitionChars(chars, c => font.HasCharacter(c));
            return new ValidationResult
            {
                AssetName    = font.name,
                AssetType    = "Unity Font",
                TotalChars   = chars.Count,
                PresentChars = present,
                MissingChars = missing,
            };
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

                var covered = remaining.Where(c => fallback.HasCharacter(c)).ToList();
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

        // ── Atlas checks ─────────────────────────────────────────────────────────

        private static void CheckDynamicAtlasSettings(TMP_FontAsset font, ValidationResult result)
        {
            // clearDynamicDataOnBuild is internal in TMP — read via SerializedObject
            var so = new SerializedObject(font);
            var clearOnBuildProp = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuildProp != null && clearOnBuildProp.boolValue)
                result.AddWarning(
                    "'Clear Dynamic Data On Build' is enabled — cached glyphs are wiped on build and must be regenerated at runtime. " +
                    "Ensure the source font file is bundled in the build.",
                    ValidationResult.Severity.Warning);

            // Atlas size + render mode (SDF modes require significant per-glyph padding)
            int area = font.atlasWidth * font.atlasHeight;
            bool isSdf = font.atlasRenderMode.ToString().IndexOf("SDF", StringComparison.OrdinalIgnoreCase) >= 0;

            if (area < 256 * 256)
                result.AddWarning(
                    $"Atlas size {font.atlasWidth}×{font.atlasHeight} is very small — high overflow risk.",
                    ValidationResult.Severity.Warning);
            else if (area < 512 * 512)
                result.AddWarning(
                    isSdf
                        ? $"Atlas {font.atlasWidth}×{font.atlasHeight} is small and render mode '{font.atlasRenderMode}' adds significant per-glyph padding — increased overflow risk."
                        : $"Atlas size {font.atlasWidth}×{font.atlasHeight} is relatively small — monitor for overflow with large character sets.",
                    isSdf ? ValidationResult.Severity.Warning : ValidationResult.Severity.Info);

            // Atlas utilization + multi-atlas (combined)
            float utilization = CalculateAtlasUtilization(font);

            if (!font.isMultiAtlasTexturesEnabled)
                result.AddWarning(
                    utilization > 0.8f
                        ? $"Multi-atlas disabled and atlas is {utilization:P0} full — new glyphs will fail to render when it overflows."
                        : $"Multi-atlas disabled — atlas is {utilization:P0} full; glyphs will fail to render if atlas fills up.",
                    ValidationResult.Severity.Warning);
            else if (utilization > 0.9f)
                result.AddWarning(
                    $"Atlas is {utilization:P0} full — a new texture will be allocated on overflow (multi-atlas is enabled).",
                    ValidationResult.Severity.Info);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static (int present, List<char> missing) PartitionChars(HashSet<char> chars, Func<char, bool> hasChar)
        {
            var missing = new List<char>();
            var present = 0;
            foreach (var c in chars)
            {
                if (hasChar(c)) present++;
                else missing.Add(c);
            }
            return (present, missing);
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