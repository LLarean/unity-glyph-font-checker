using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LLarean.GlyphFontChecker
{
    public class ResultWindow : EditorWindow
    {
        private ValidationResult _result;
        private Vector2 _missingScrollPos;
        private Vector2 _fallbackScrollPos;

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
            DrawDynamicSummary();
            DrawAtlasWarnings();
            DrawFallbackInfo();
            DrawMissingChars();
            DrawCloseButton();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField($"{_result.AssetName} ({_result.AssetType})", EditorStyles.boldLabel);

            var stats = $"Unique chars: {_result.TotalChars}   Present: {_result.PresentChars}";
            if (_result.HasFallbacks)
                stats += $"   Fallback: {_result.FallbackCoveredCount}";
            stats += $"   Missing: {_result.MissingCount}";

            EditorGUILayout.LabelField(stats);
        }

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
                    : ("Dynamic atlas — all characters are already cached.", MessageType.Info);
            }

            return _result.SourceFontCoversAllMissing switch
            {
                true  => ("Dynamic atlas — missing chars will be generated at runtime (source font supports them).", MessageType.Info),
                false => ("Dynamic atlas — some missing chars are absent from the source font and may not render!", MessageType.Error),
                null  => ("Dynamic atlas — source font not assigned; cannot verify runtime glyph generation.", MessageType.Warning),
            };
        }

        private void DrawAtlasWarnings()
        {
            foreach (var w in _result.AtlasWarnings)
            {
                var type = w.Level == ValidationResult.Severity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(w.Message, type);
            }
        }

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

        private void DrawMissingChars()
        {
            if (_result.MissingCount == 0) return;

            GUILayout.Space(8);
            EditorGUILayout.LabelField("Missing characters:", EditorStyles.boldLabel);

            var missingStr = string.Join(" ", _result.MissingChars.OrderBy(c => c));
            _missingScrollPos = EditorGUILayout.BeginScrollView(_missingScrollPos, GUILayout.Height(80));
            EditorGUILayout.TextArea(missingStr);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Copy to Clipboard"))
                EditorGUIUtility.systemCopyBuffer = missingStr;
        }

        private void DrawCloseButton()
        {
            GUILayout.Space(8);
            if (GUILayout.Button("Close"))
                Close();
        }
    }
}