# Glyph Font Checker

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
![stability-experimental](https://img.shields.io/badge/stability-experimental-orange.svg)

> [!WARNING]
> This is an AI-generated prototype under active manual refinement. Results may be inaccurate in edge cases — always validate against a real device or a dedicated font inspection tool before making production decisions.

Editor utility for checking character coverage in TMP and Unity fonts. Detects missing glyphs, walks fallback font chains, and validates dynamic atlas settings that may cause characters to fail at runtime.

## Quick Start

1. Install via Package Manager: `https://github.com/LLarean/unity-glyph-font-checker.git`
2. Open `Tools > Font Localization Checker`
3. Paste your localized text or load a TextAsset
4. Drag a TMP Font Asset or Unity Font into the font field
5. Click **Check**

The result shows present, fallback-covered, and missing characters — plus atlas setting warnings for dynamic fonts.

## Installation

- *(via Package Manager)* Select **Add package from git URL** and enter:
  - `https://github.com/LLarean/unity-glyph-font-checker.git`
- *(via Package Manager)* Add to `Packages/manifest.json`:
  - `"com.llarean.glyphfontchecker": "https://github.com/LLarean/unity-glyph-font-checker.git"`
- Clone or [download](https://github.com/LLarean/unity-glyph-font-checker/archive/main.zip) and place into your project's *Assets* folder

## Usage

1. Open `Tools > Font Localization Checker`
2. Load your text:
   - Drag a **TextAsset** (.txt file from the project) into the *Text Asset* field
   - Click **Paste from Clipboard** — recommended for large texts
   - Type directly in the text area (safe for short inputs up to ~500 characters)
3. Drag a **TMP Font Asset** or **Unity Font** into the font field
4. Click **Check**

The result window shows:
- **Unique / Present / Fallback / Not baked / Missing** — full character breakdown
- **Read method note** — whether results came from direct font file parsing or Unity API fallback; includes diagnostic details when parsing failed
- **Dynamic atlas summary** — whether missing chars can be generated at runtime via the source font
- **Atlas and script warnings** — atlas capacity estimate, script compatibility, fallback chain depth, font file bundling
- **Fallback coverage** — which chars are rescued by each fallback font in the chain
- **Missing from font file** — chars physically absent from the font; sorted, with copy-to-clipboard
- **In font but not in atlas** — chars present in the source font but not baked into the static atlas; sorted, with copy-to-clipboard

## Checks Reference

### Direct Font File Parsing

The tool reads the OpenType/TrueType `cmap` table directly — bypassing Unity's font API and system font substitution.

**Supported containers:** TTF, OTF, TTC (all sub-fonts merged), WOFF (zlib-compressed tables decompressed automatically).

**Supported cmap formats:** 2 (mixed single/double-byte), **4** (BMP segmented), **6** (trimmed), **10** (trimmed 32-bit), **12** (full Unicode), **13** (many-to-one range).

**Not supported:** WOFF2 (Brotli compression requires .NET 6+) — falls back to Unity's API with a diagnostic reported in the result window.

### Static TMP Atlas

| Check | Issue | Severity |
|---|---|---|
| Atlas texture is null | Font has no baked texture — nothing will render | ⚠ Warning |
| Character table is empty | No glyphs baked — regenerate atlas | ⚠ Warning |
| Source font not assigned | Cannot distinguish "not baked" from "missing" | ⚠ Warning |
| Chars in source font but not in atlas | Need Regenerate Atlas | ⚠ Warning |
| Chars absent from source font file | Need a different font | ✖ Error |

### Dynamic TMP Atlas

| Check | Issue | Severity |
|---|---|---|
| Source font not assigned | Runtime glyph generation impossible | ⚠ Warning |
| Source font in `Editor/` folder | Will not be included in builds | ⚠ Warning |
| `Clear Dynamic Data On Build` = true | Glyphs cleared on build, regenerated at runtime | ⚠ Warning |
| Atlas < 256×256 px | Extremely small — near-certain overflow | ⚠ Warning |
| Atlas < 512×512 px + SDF mode | SDF padding reduces effective capacity | ⚠ Warning |
| Estimated capacity < 80% after input | Atlas approaching full | ⚠ Warning |
| Estimated capacity overflow | Input will not fit in atlas | ⚠ Warning |
| Multi-atlas disabled + atlas > 80% full | Glyphs will fail when atlas overflows | ⚠ Warning |
| Multi-atlas disabled | Atlas cannot grow | ⚠ Warning |
| Atlas > 90% full (multi-atlas on) | New texture page will be allocated | ℹ Info |
| Fallback chain depth > 3 | Per-frame CPU cost for missing glyph lookups | ℹ Info |
| Dynamic fallbacks in chain | Glyph generation stutter on first use | ℹ Info |

### Script Compatibility (Dynamic TMP)

| Script | Issue |
|---|---|
| CJK / Hiragana / Katakana / Hangul | Large atlas (≥ 1024×1024) required; static atlas recommended for large sets |
| Thai | Complex shaping not supported by standard TMP — requires TextShaper |
| Arabic / Persian / Urdu | RTL + ligature shaping not supported — requires TextShaper or RTL plugin |
| Hebrew / Syriac | RTL + nikud positioning not supported — requires TextShaper |
| Devanagari / Bengali / Tamil / Telugu / Kannada / Malayalam / Gujarati / Gurmukhi / Odia | Conjunct shaping not supported — requires TextShaper |

### Unity Font

| Check | Issue | Severity |
|---|---|---|
| Import charset = ASCII | Non-ASCII characters not baked | ⚠ Warning |
| Import charset = Custom Set, chars missing | Input chars not in the custom set | ⚠ Warning |
| Rendering mode = OS Default | Inconsistent appearance across platforms | ℹ Info |

## Requirements

- Unity 2021.3+
- TextMeshPro (included in `com.unity.ugui >= 2.0.0`)

## Contributing

- **Bug reports**: [Open an issue](https://github.com/LLarean/unity-glyph-font-checker/issues) with your font file and a description of the unexpected result
- **Feature requests**: Describe your use case in an issue
- **Pull requests**: For bug fixes or improvements

---

<div align="center">

**Made with ❤️ for the Unity community**

⭐ If this project helped you, please consider giving it a star!

</div>
