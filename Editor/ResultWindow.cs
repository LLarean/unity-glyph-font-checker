using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LLarean.GlyphFontChecker
{
    public class ResultWindow : EditorWindow
    {
        private ValidationResult _result;
        private Vector2 _missingFileScrollPos;
        private Vector2 _notBakedScrollPos;
        private Vector2 _fallbackScrollPos;
        private Vector2 _diagnosticScrollPos;
        private bool    _diagnosticExpanded;

        public static void Show(ValidationResult result)
        {
            var window = GetWindow<ResultWindow>(true, "Font Check Result");
            window._result = result;
            window.Show();
        }

        private void OnGUI()
        {
            if (_result == null) return;

            if (_result.HasError)
            {
                EditorGUILayout.HelpBox(_result.Error, MessageType.Error);
                DrawCloseButton();
                return;
            }

            DrawHeader();
            DrawReadMethodNote();
            DrawDynamicSummary();
            DrawAtlasWarnings();
            DrawFallbackInfo();
            DrawMissingFromFontFile();
            DrawNotBakedIntoAtlas();
            DrawCloseButton();
        }

        // ── Header ───────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.LabelField($"{_result.AssetName} ({_result.AssetType})", EditorStyles.boldLabel);

            var stats = $"Unique chars: {_result.TotalChars}   Present: {_result.PresentChars}";
            if (_result.HasFallbacks)
                stats += $"   Fallback: {_result.FallbackCoveredCount}";
            if (_result.NotBakedIntoAtlasCount > 0)
                stats += $"   Not baked: {_result.NotBakedIntoAtlasCount}";
            stats += $"   Missing: {_result.MissingCount}";

            EditorGUILayout.LabelField(stats);
        }

        // ── Read method note ─────────────────────────────────────────────────────

        private void DrawReadMethodNote()
        {
            if (_result.UsedDirectFileRead)
            {
                EditorGUILayout.HelpBox(
                    "Glyphs verified by direct font file parsing (no system font substitution).",
                    MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                "Font file could not be parsed directly — fell back to Unity's font API.\n\n" +
                "What this means:\n" +
                "  • Characters shown as MISSING are reliably missing.\n" +
                "  • Characters shown as PRESENT may be false positives from system font substitution\n" +
                "    (e.g. Arial or Noto covering the character instead of this font file).",
                MessageType.Warning);

            DrawFontReadDiagnostic();
        }

        // ── Font read diagnostic ──────────────────────────────────────────────────

        private void DrawFontReadDiagnostic()
        {
            bool hasDiag = !string.IsNullOrEmpty(_result.FontReadDiagnostic);

            EditorGUILayout.BeginHorizontal();
            _diagnosticExpanded = EditorGUILayout.Foldout(_diagnosticExpanded,
                hasDiag ? "Why did parsing fail? (details)" : "Why did parsing fail?",
                toggleOnLabelClick: true);

            if (GUILayout.Button("Open Console", GUILayout.Width(110)))
                EditorApplication.ExecuteMenuItem("Window/General/Console");
            EditorGUILayout.EndHorizontal();

            if (!_diagnosticExpanded) return;

            if (hasDiag)
            {
                GUILayout.Space(2);
                _diagnosticScrollPos = EditorGUILayout.BeginScrollView(_diagnosticScrollPos, GUILayout.Height(100));
                EditorGUILayout.SelectableLabel(_result.FontReadDiagnostic,
                    EditorStyles.wordWrappedLabel,
                    GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Copy diagnostic to clipboard"))
                    EditorGUIUtility.systemCopyBuffer = _result.FontReadDiagnostic;
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No detailed diagnostic available. Check the Unity Console for a [FontFileReader] warning " +
                    "— it contains the exact path that was attempted and the reason for the failure.",
                    MessageType.None);
            }
        }

        // ── Dynamic summary ───────────────────────────────────────────────────────

        private void DrawDynamicSummary()
        {
            if (!_result.IsDynamic) return;

            GUILayout.Space(4);
            var (msg, type) = BuildDynamicMessage();
            EditorGUILayout.HelpBox(msg, type);
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
                    "The source font file could not be read directly, so these may still render at runtime if the source font contains them. " +
                    "See warnings below.",
                    MessageType.Warning);
            }

            return (
                "Dynamic atlas — some characters are absent from the source font file and will never render. " +
                "See 'Missing from font file' below.",
                MessageType.Error);
        }

        // ── Atlas warnings ────────────────────────────────────────────────────────

        private void DrawAtlasWarnings()
        {
            foreach (var w in _result.AtlasWarnings)
            {
                var type = w.Level == ValidationResult.Severity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(w.Message, type);
            }
        }

        // ── Fallback coverage ─────────────────────────────────────────────────────

        private void DrawFallbackInfo()
        {
            if (!_result.HasFallbacks) return;

            GUILayout.Space(8);
            EditorGUILayout.LabelField("Covered by fallback fonts:", EditorStyles.boldLabel);

            _fallbackScrollPos = EditorGUILayout.BeginScrollView(_fallbackScrollPos, GUILayout.Height(80));
            foreach (var f in _result.Fallbacks)
            {
                var chars = string.Join(" ", f.Chars.OrderBy(c => c));
                EditorGUILayout.HelpBox($"{f.FontName} ({f.Chars.Count}):  {chars}", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        // ── Missing from font file ────────────────────────────────────────────────

        private void DrawMissingFromFontFile()
        {
            if (_result.MissingCount == 0) return;

            GUILayout.Space(8);

            if (_result.UsedDirectFileRead)
            {
                EditorGUILayout.LabelField($"Missing from font file ({_result.MissingCount}):", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "These characters are physically absent from the font file. " +
                    "They will never render regardless of atlas settings — a different font is needed.",
                    MessageType.Error);
            }
            else
            {
                // Font file could not be parsed — fallback results based on atlas cache / HasCharacter()
                string label = _result.IsDynamic
                    ? $"Not found in atlas cache ({_result.MissingCount}):"
                    : $"Not found via system API ({_result.MissingCount}):";
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

                string explanation = _result.IsDynamic
                    ? "Font file could not be read directly — these characters are absent from the current atlas cache.\n" +
                      "They may still render at runtime if the source font contains them and the atlas has capacity.\n" +
                      "Check the Unity Console for a [FontFileReader] warning to see the exact path and failure reason."
                    : "Font file could not be read directly — results are based on Unity's HasCharacter() API.\n" +
                      "Characters listed here are reliably missing (Unity couldn't find them anywhere).\n" +
                      "However, characters NOT listed may include false positives from system font substitution.\n" +
                      "Check the Unity Console for a [FontFileReader] warning to see the exact path and failure reason.";
                EditorGUILayout.HelpBox(explanation, MessageType.Warning);
            }

            var missingStr = string.Join(" ", _result.MissingChars.OrderBy(c => c));
            _missingFileScrollPos = EditorGUILayout.BeginScrollView(_missingFileScrollPos, GUILayout.Height(80));
            EditorGUILayout.TextArea(missingStr);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Copy to clipboard"))
                EditorGUIUtility.systemCopyBuffer = missingStr;
        }

        // ── Not baked into atlas ──────────────────────────────────────────────────

        private void DrawNotBakedIntoAtlas()
        {
            if (_result.NotBakedIntoAtlasCount == 0) return;

            GUILayout.Space(8);
            EditorGUILayout.LabelField($"In font but not in atlas ({_result.NotBakedIntoAtlasCount}):", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These characters exist in the source font file but are not baked into the current static atlas. " +
                "Use 'Regenerate Atlas' in the TMP Font Asset inspector to include them.",
                MessageType.Warning);

            var notBakedStr = string.Join(" ", _result.NotBakedIntoAtlas.OrderBy(c => c));
            _notBakedScrollPos = EditorGUILayout.BeginScrollView(_notBakedScrollPos, GUILayout.Height(80));
            EditorGUILayout.TextArea(notBakedStr);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Copy not-baked to clipboard"))
                EditorGUIUtility.systemCopyBuffer = notBakedStr;
        }

        // ── Close ─────────────────────────────────────────────────────────────────

        private void DrawCloseButton()
        {
            GUILayout.Space(8);
            if (GUILayout.Button("Close"))
                Close();
        }
    }
}
