using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LLarean.GlyphFontChecker
{
    public class ResultWindow : EditorWindow
    {
        private ValidationResult _result;
        private Vector2 _scrollPos;

        // Foldout states
        private bool _foldParsing;
        private bool _foldAtlas    = true;
        private bool _foldFallback = true;
        private bool _foldMissing  = true;
        private bool _foldNotBaked = true;

        private bool _initialized;

        public static void Show(ValidationResult result)
        {
            var window = GetWindow<ResultWindow>(true, "Font Check Result");
            window._result      = result;
            window._initialized = false;
            window.minSize      = new Vector2(420, 300);
            window.Show();
        }

        private void OnGUI()
        {
            if (_result == null) return;

            // One-time: collapse Parsing section when direct read succeeded
            if (!_initialized)
            {
                _initialized  = true;
                _foldParsing  = !_result.UsedDirectFileRead; // expand if there's a problem
            }

            if (_result.HasError)
            {
                EditorGUILayout.HelpBox(_result.Error, MessageType.Error);
                DrawCloseButton();
                return;
            }

            DrawStatusBox();

            GUILayout.Space(4);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawSectionParsing();
            DrawSectionAtlas();
            DrawSectionFallback();
            DrawSectionMissing();
            DrawSectionNotBaked();

            GUILayout.Space(4);
            EditorGUILayout.EndScrollView();

            DrawCloseButton();
        }

        // ── Status box ────────────────────────────────────────────────────────────

        private void DrawStatusBox()
        {
            bool hardMissing = _result.MissingCount > 0 && _result.UsedDirectFileRead;
            bool hasWarning  = _result.MissingCount > 0
                            || _result.AtlasWarnings.Any(w => w.Level == ValidationResult.Severity.Warning);

            MessageType boxType = hardMissing ? MessageType.Error
                                : hasWarning  ? MessageType.Warning
                                :               MessageType.None;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{_result.AssetName}  [{_result.AssetType}]");

            sb.Append($"{_result.TotalChars} chars  ·  {_result.PresentChars} present");
            if (_result.FallbackCoveredCount > 0) sb.Append($"  ·  {_result.FallbackCoveredCount} via fallback");
            if (_result.NotBakedIntoAtlasCount > 0) sb.Append($"  ·  {_result.NotBakedIntoAtlasCount} not baked");
            sb.Append($"  ·  {_result.MissingCount} missing");

            EditorGUILayout.HelpBox(sb.ToString(), boxType);
        }

        // ── Parsing Method ────────────────────────────────────────────────────────

        private void DrawSectionParsing()
        {
            string label = _result.UsedDirectFileRead
                ? "Parsing Method"
                : "Parsing Method  ⚠";

            _foldParsing = EditorGUILayout.Foldout(_foldParsing, label, toggleOnLabelClick: true);
            if (!_foldParsing) return;

            EditorGUI.indentLevel++;

            if (_result.UsedDirectFileRead)
            {
                EditorGUILayout.HelpBox(
                    "Glyphs verified by direct font file parsing (no system font substitution).",
                    MessageType.None);
            }
            else if (!_result.FontFileReaderInvoked)
            {
                EditorGUILayout.HelpBox(
                    "Font file was not read — Source Font File is not assigned on this asset.\n\n" +
                    "Results are based on the current atlas cache only. Characters not yet rendered\n" +
                    "will appear as missing even if the source font contains them.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Font file could not be parsed directly — fell back to Unity's font API.\n\n" +
                    "  • Characters shown as MISSING are reliably missing.\n" +
                    "  • Characters shown as PRESENT may include false positives\n" +
                    "    from system font substitution (e.g. Arial, Noto).",
                    MessageType.Warning);

                GUILayout.Space(2);
                DrawDiagnosticDetail();
            }

            EditorGUI.indentLevel--;
        }

        private void DrawDiagnosticDetail()
        {
            bool hasDiag = !string.IsNullOrEmpty(_result.FontReadDiagnostic);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                hasDiag ? "Parse failure detail:" : "No diagnostic available — check the Console.",
                EditorStyles.miniLabel);
            if (GUILayout.Button("Open Console", GUILayout.Width(108)))
                EditorApplication.ExecuteMenuItem("Window/General/Console");
            EditorGUILayout.EndHorizontal();

            if (hasDiag)
            {
                DrawSelectableLabelWithCopy(_result.FontReadDiagnostic, "Copy");
            }
        }

        // ── Atlas & Settings ──────────────────────────────────────────────────────

        private void DrawSectionAtlas()
        {
            bool hasContent = _result.IsDynamic || _result.AtlasWarnings.Count > 0;
            if (!hasContent) return;

            GUILayout.Space(2);
            _foldAtlas = EditorGUILayout.Foldout(_foldAtlas, "Atlas & Settings", toggleOnLabelClick: true);
            if (!_foldAtlas) return;

            EditorGUI.indentLevel++;

            if (_result.IsDynamic)
            {
                var (msg, type) = BuildDynamicMessage();
                EditorGUILayout.HelpBox(msg, type);
            }

            foreach (var w in _result.AtlasWarnings)
            {
                var t = w.Level == ValidationResult.Severity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(w.Message, t);
            }

            EditorGUI.indentLevel--;
        }

        private (string message, MessageType type) BuildDynamicMessage()
        {
            if (_result.MissingCount == 0)
            {
                return _result.HasFallbacks
                    ? ("Dynamic atlas — all characters are present (some via fallback fonts).", MessageType.Info)
                    : ("Dynamic atlas — all characters are present in the source font and will be generated at runtime.", MessageType.Info);
            }

            if (!_result.UsedDirectFileRead)
            {
                return (
                    "Dynamic atlas — some characters were not found in the current atlas cache. " +
                    "The source font file could not be read directly, so these may still render at runtime " +
                    "if the source font contains them.",
                    MessageType.Warning);
            }

            return (
                "Dynamic atlas — some characters are absent from the source font file and will never render. " +
                "See 'Missing Characters' below.",
                MessageType.Error);
        }

        // ── Fallback Coverage ─────────────────────────────────────────────────────

        private void DrawSectionFallback()
        {
            if (!_result.HasFallbacks) return;

            GUILayout.Space(2);
            _foldFallback = EditorGUILayout.Foldout(_foldFallback,
                $"Fallback Coverage  ({_result.FallbackCoveredCount} chars)",
                toggleOnLabelClick: true);
            if (!_foldFallback) return;

            EditorGUI.indentLevel++;
            foreach (var f in _result.Fallbacks)
            {
                var chars = string.Join(" ", f.Chars.OrderBy(c => c));
                EditorGUILayout.LabelField($"{f.FontName}  ({f.Chars.Count})", EditorStyles.boldLabel);
                DrawSelectableLabelWithCopy(chars, "Copy");
                GUILayout.Space(2);
            }
            EditorGUI.indentLevel--;
        }

        // ── Missing Characters ────────────────────────────────────────────────────

        private void DrawSectionMissing()
        {
            if (_result.MissingCount == 0) return;

            GUILayout.Space(2);

            string label = _result.UsedDirectFileRead
                ? $"Missing Characters  ({_result.MissingCount})"
                : _result.IsDynamic
                    ? $"Not in Atlas Cache  ({_result.MissingCount})"
                    : $"Not Found  ({_result.MissingCount})";

            _foldMissing = EditorGUILayout.Foldout(_foldMissing, label, toggleOnLabelClick: true);
            if (!_foldMissing) return;

            EditorGUI.indentLevel++;

            if (_result.UsedDirectFileRead)
            {
                EditorGUILayout.HelpBox(
                    "Physically absent from the font file — will never render regardless of atlas settings.\n" +
                    "A different font is needed for these characters.",
                    MessageType.Error);
            }
            else
            {
                string note = _result.IsDynamic
                    ? "Absent from current atlas cache. May still render at runtime if the source font contains them."
                    : "Not found via Unity's font API. Characters not listed may still be false positives\nfrom system font substitution.";
                EditorGUILayout.HelpBox(note, MessageType.Warning);
            }

            var missingStr = string.Join(" ", _result.MissingChars.OrderBy(c => c));
            DrawSelectableLabelWithCopy(missingStr, "Copy");

            EditorGUI.indentLevel--;
        }

        // ── Not Baked into Atlas ──────────────────────────────────────────────────

        private void DrawSectionNotBaked()
        {
            if (_result.NotBakedIntoAtlasCount == 0) return;

            GUILayout.Space(2);
            _foldNotBaked = EditorGUILayout.Foldout(_foldNotBaked,
                $"Not Baked into Atlas  ({_result.NotBakedIntoAtlasCount})",
                toggleOnLabelClick: true);
            if (!_foldNotBaked) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Present in the source font file but not baked into the current static atlas.\n" +
                "Use 'Regenerate Atlas' in the TMP Font Asset inspector to include them.",
                MessageType.Warning);

            var notBakedStr = string.Join(" ", _result.NotBakedIntoAtlas.OrderBy(c => c));
            DrawSelectableLabelWithCopy(notBakedStr, "Copy");

            EditorGUI.indentLevel--;
        }

        // ── Shared helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Renders a word-wrapped selectable label with a fixed-width Copy button to its right.
        /// Height is calculated from actual content to avoid layout jumps.
        /// </summary>
        private void DrawSelectableLabelWithCopy(string text, string copyLabel = "Copy")
        {
            if (string.IsNullOrEmpty(text)) return;

            float indent    = EditorGUI.indentLevel * 15f;
            float available = Mathf.Max(80f, position.width - indent - 70f);
            float height    = EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(text), available);
            height = Mathf.Max(height, EditorGUIUtility.singleLineHeight);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.SelectableLabel(text,
                EditorStyles.wordWrappedLabel,
                GUILayout.Height(height),
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button(copyLabel, GUILayout.Width(50), GUILayout.Height(height)))
                EditorGUIUtility.systemCopyBuffer = text;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCloseButton()
        {
            GUILayout.Space(4);
            if (GUILayout.Button("Close", GUILayout.Height(24)))
                Close();
        }
    }
}