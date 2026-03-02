# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-03-02

### Added
- Three text input modes: TextAsset field, "Paste from Clipboard" button, and manual TextArea
- Soft limit (500 chars) for TextArea: large text is evacuated immediately — unique chars are preserved, TextArea is cleared on the next frame
- Hard limit (2000 chars): absolute truncation guard to prevent IMGUI layout crash on large one-frame pastes
- Char count summary displayed after any input: `Source: X | N unique chars (from M total)`

### Changed
- Text input no longer stores large strings in the TextArea render loop; all heavy text is processed once and discarded
- "Check" button is now enabled based on `_chars.Count > 0` instead of raw string length
- All classes wrapped in `namespace LLarean.GlyphFontChecker`

### Fixed
- `Object` ambiguous reference resolved via `using Object = UnityEngine.Object`
- `clearDynamicDataOnBuild` (internal in TMP) now read via `SerializedObject.FindProperty("m_ClearDynamicDataOnBuild")`

---

## [1.1.0] - 2026-03-02

### Added
- Fallback font chain check: characters not found in the primary font are looked up recursively through `fallbackFontAssetTable`; result distinguishes "covered by fallback" from "truly missing"
- Circular reference guard during fallback traversal
- Atlas utilization percentage check (sum of glyph rects vs total atlas area); warns when atlas is nearly full and multi-atlas is disabled
- Render mode check: SDF/MSDF modes require significant per-glyph padding; warns when combined with a small atlas

### Changed
- `MissingChars` now contains only truly missing characters (not covered by any fallback)
- `ValidationResult` extended with `Fallbacks` list and `FallbackCoveredCount`
- Result window header shows "Fallback: N" count when fallbacks are present
- Atlas size and multi-atlas warnings now also report current utilization percentage
- `SourceFontCoversAllMissing` is now evaluated against truly missing chars only

---

## [1.0.0] - 2026-03-02

### Added
- Character coverage check for TMP Font Assets and Unity Fonts
- Dynamic atlas validation: checks if the source font can generate missing glyphs at runtime
- Atlas setting warnings: `Clear Dynamic Data On Build`, atlas size, multi-atlas disabled
- Result window with missing character list and copy-to-clipboard
- UPM package structure (`package.json`, `Editor/` assembly definition)