# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-03-03

### Added
- **`FontFileReader`** — new Editor-only OpenType/TrueType binary parser; reads the `cmap` table directly from the font file with no system font substitution. Supported formats: cmap Format 4 (BMP), Format 6 (trimmed table), Format 12 (full Unicode / supplementary planes). Handles TTF, OTF, and TTC (uses first sub-font in collection)
- `FontFileReader.ReadCodePoints(Font, out string diagnostic)` and `ReadCodePoints(string, out string diagnostic)` overloads — capture a structured human-readable failure reason (path attempted, likely causes, suggested actions) without requiring the user to open the Console
- **Static TMP**: cross-check of chars missing from atlas against the source font file — distinguishes `NotBakedIntoAtlas` (need Regenerate Atlas) from `MissingFromFontFile` (need a different font)
- **Static TMP**: `CheckStaticAtlasHealth` — warns when atlas texture is null or character lookup table is empty, with step-by-step action instructions
- **Dynamic TMP**: `CheckFontFileBundling` — detects source font inside an `Editor/` folder (will not be included in builds) and warns when font is outside `Resources/` or `StreamingAssets/`
- **Dynamic TMP**: `CheckDynamicAtlasCapacity` — estimates maximum glyph count from atlas dimensions, sample point size, and SDF padding; reports current cache + input chars as a percentage of estimated capacity with overflow warnings; auto-infers default SDF padding when `m_Padding` is 0
- **Dynamic TMP**: `CheckFallbackChain` — measures fallback chain depth (warns if > 3 levels) and identifies dynamic fallback fonts in the chain (warns about runtime glyph generation stutter)
- **Script compatibility** expanded: Hebrew/Syriac (RTL, nikud), Indic scripts (Devanagari, Bengali, Gujarati, Gurmukhi, Odia, Tamil, Telugu, Kannada, Malayalam), extended Arabic ranges (Presentation Forms A/B), CJK large-set recommendation for static atlas
- **Unity Font**: `CheckUnityFontImportSettings` — checks `FontTextureCase` (warns on ASCII-only; reports specific missing chars for Custom Set) and `FontRenderingMode` (warns on OS Default)
- **Result window**: `DrawFontReadDiagnostic` — foldout section shown when direct file parsing failed; displays structured diagnostic text in a selectable label, "Copy diagnostic to clipboard" button, and "Open Console" button
- `ValidationResult.FontReadDiagnostic` — stores the parser failure message so it survives the check and is shown in the result window without needing the Console
- `ValidationResult.MissingFromFontFile` — chars physically absent from the font file
- `ValidationResult.NotBakedIntoAtlas` — chars in source font but not baked into static atlas
- `ValidationResult.UsedDirectFileRead` — flag indicating whether results came from direct binary parsing or Unity API fallback
- All warning messages now include an explicit `Action:` recommendation with specific menu paths, package names, or steps

### Changed
- `ValidateTmpFont` split into `ValidateStaticTmpFont` and `ValidateDynamicTmpFont` with separate validation logic
- Static TMP now uses `font.characterLookupTable.ContainsKey(c)` directly instead of `font.HasCharacter(c)` — avoids implicit fallback traversal
- Dynamic TMP now uses `FontFileReader` on `sourceFontFile` instead of `font.HasCharacter(c, true)` — checks the font file rather than the current atlas cache
- `CheckFallbacks` now uses `characterLookupTable` for static fallbacks and `FontFileReader` for dynamic fallbacks, matching each font's actual data source
- `CheckDynamicAtlasSettings` warning messages revised to include `Action:` steps
- `CheckScriptCompatibility` now covers CJK large-set count and is called only for chars confirmed present in the source font
- `DrawReadMethodNote` expanded: explains the reliability difference between missing vs. present results when falling back to Unity API; foldout replaced by inline diagnostic block
- `DrawMissingFromFontFile` label and explanation text change based on `UsedDirectFileRead` and `IsDynamic` — avoids showing "physically absent from file" when the file was not actually read
- `BuildDynamicMessage` differentiates between confirmed-missing (font file read succeeded) and cache-miss (fallback path)
- `FontFileReader` path resolution changed from `Path.GetFullPath(assetPath)` to `Path.GetFullPath(Path.Combine(projectRoot, assetPath))` — correctly resolves both `Assets/` and `Packages/` paths on all platforms
- `GlyphRenderMode` enum reference replaced with `.ToString().IndexOf("SDF")` string check — avoids `CS0103` compile error under some TMP package versions
- `font.padding` replaced with `SerializedObject.FindProperty("m_Padding")` — `padding` is not a public property in all TMP versions

### Fixed
- Dynamic TMP: `font.HasCharacter(c, true)` was checking the current atlas cache, causing uncached-but-valid characters to appear as missing; replaced by `FontFileReader` which reads the source font file directly
- Static TMP: `font.HasCharacter(c)` could traverse fallback fonts silently; replaced by direct `characterLookupTable` lookup
- All font checks: `Font.HasCharacter()` could return `true` for characters covered by system fallback fonts (not the font file itself); replaced by `FontFileReader` with Unity API as fallback only when the file cannot be parsed
- `CS1061`: `TMP_FontAsset` does not contain `padding` — fixed by reading via `SerializedObject`
- `CS0103`: `GlyphRenderMode` not in scope — fixed by string comparison

---

## [1.2.1] - 2026-03-02

### Fixed
- Replaced target-typed `new()` field initializers with explicit constructors — C# 9 feature not supported under .NET Standard 2.0 (CS8124)
- Replaced `string[..n]` range expression with `string.Substring(0, n)` — `System.Range` unavailable under .NET Standard 2.0 (CS0518)

---

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