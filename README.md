# ![unity](https://img.shields.io/badge/Unity-100000?style=for-the-badge&logo=unity&logoColor=white) Glyph Font Checker

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
![stability-experimental](https://img.shields.io/badge/stability-experimental-orange.svg)

> [!WARNING]
> **This utility is an AI-generated prototype.**
> The current implementation was produced with the assistance of an AI coding tool and has not yet been fully reviewed or hardened by hand.
> It will be refined manually in future iterations.
>
> **Use at your own risk.** Results may be inaccurate in edge cases — always validate findings against a real device or a dedicated font inspection tool (e.g. FontForge, Windows Character Map) before making production decisions.

Editor utility for checking character coverage in TMP and Unity fonts. Detects missing glyphs, walks fallback font chains, and validates dynamic atlas settings that may cause characters to fail at runtime.

Essential for localization workflows where a missing character can go unnoticed until it reaches production.

**Technical details**: TextMeshPro renders characters by looking them up in a font atlas — a texture containing pre-rendered glyph bitmaps. Dynamic atlases generate glyphs on demand at runtime, but only if the source font is available and the atlas has enough space. Static atlases must contain all required characters upfront. Learn more in Unity's [TMP documentation](https://docs.unity3d.com/Packages/com.unity.textmeshpro@4.0/manual/index.html).

## Quick Start
1. Install via Package Manager: `https://github.com/LLarean/unity-glyph-font-checker.git`
2. Open `Tools > Font Localization Checker`
3. Paste your localized text or load a TextAsset
4. Drag a TMP Font Asset or Unity Font into the font field
5. Click **Check**

**Result**: A breakdown of present, fallback-covered, and truly missing characters — plus atlas setting warnings for dynamic fonts.

## INSTALLATION

There are 3 ways to install this utility:

- clone/[download](https://github.com/LLarean/unity-glyph-font-checker/archive/main.zip) this repository and place the contents into your Unity project's *Assets* folder
- *(via Package Manager)* Select **Add package from git URL** from the add menu and enter:
  - `https://github.com/LLarean/unity-glyph-font-checker.git`
- *(via Package Manager)* add the following line to *Packages/manifest.json*:
  - `"com.llarean.glyphfontchecker": "https://github.com/LLarean/unity-glyph-font-checker.git"`

## HOW TO

1. Open the utility via `Tools > Font Localization Checker`
2. Load your text — 3 ways:
   - Drag a **TextAsset** (.txt file from the project) into the *Text Asset* field
   - Click **Paste from Clipboard** — recommended for large texts (no TextArea rendering overhead)
   - Type directly in the text area (safe for short inputs up to ~500 characters)
3. Drag a **TMP Font Asset** or **Unity Font** into the font field
4. Click **Check**

The result window shows:
- **Unique / Present / Fallback / Not baked / Missing** — full character breakdown
- **Read method note** — whether results came from direct font file parsing or Unity API fallback; includes diagnostic details and a copy button when parsing failed
- **Dynamic atlas summary** — whether missing chars can be generated at runtime via the source font
- **Atlas and script warnings** — atlas capacity estimate, script compatibility, fallback chain depth, font file bundling
- **Fallback coverage** — which chars are rescued by each fallback font in the chain
- **Missing from font file** — chars physically absent from the font; sorted, with copy-to-clipboard
- **In font but not in atlas** — chars present in the source font but not baked into the static atlas; sorted, with copy-to-clipboard

## Checks Reference

### Direct font file parsing (all font types)

The tool reads the OpenType/TrueType `cmap` table directly from the `.ttf`/`.otf`/`.ttc` file — bypassing Unity's font API and system font substitution. Supported cmap formats: **4** (BMP), **6** (trimmed), **12** (full Unicode).

If the file cannot be parsed (Packages/ path, WOFF/WOFF2, unsupported format), the tool falls back to Unity's `HasCharacter()` API and reports the exact reason in the result window with a copy button.

### Static TMP atlas checks

| Check | Issue | Severity |
|---|---|---|
| Atlas texture is null | Font has no baked texture — nothing will render | ⚠ Warning |
| Character table is empty | No glyphs baked — regenerate atlas | ⚠ Warning |
| Source font not assigned | Cannot distinguish "not baked" from "missing" | ⚠ Warning |
| Chars in source font but not in atlas | Need Regenerate Atlas | ⚠ Warning |
| Chars absent from source font file | Need a different font | ✖ Error |

### Dynamic TMP atlas checks

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

### Script compatibility (dynamic TMP)

| Script | Issue |
|---|---|
| CJK / Hiragana / Katakana / Hangul | Large atlas (≥ 1024×1024) required; static atlas recommended for large sets |
| Thai | Complex shaping not supported by standard TMP — requires TextShaper |
| Arabic / Persian / Urdu | RTL + ligature shaping not supported — requires TextShaper or RTL plugin |
| Hebrew / Syriac | RTL + nikud positioning not supported — requires TextShaper |
| Devanagari / Bengali / Tamil / Telugu / Kannada / Malayalam / Gujarati / Gurmukhi / Odia | Conjunct shaping not supported — requires TextShaper |

### Unity Font checks

| Check | Issue | Severity |
|---|---|---|
| Import charset = ASCII | Non-ASCII characters not baked | ⚠ Warning |
| Import charset = Custom Set, chars missing | Input chars not in the custom set | ⚠ Warning |
| Rendering mode = OS Default | Inconsistent appearance across platforms | ℹ Info |

## Requirements

- Unity 2021.3+
- TextMeshPro (included in `com.unity.ugui >= 2.0.0`)

## Project Status

This project is an **experimental AI-generated prototype** under active manual refinement.

**Current state:**
- Core logic (font file parsing, atlas checks, script compatibility) is functional but has not been exhaustively tested across all font formats and Unity versions
- Results should be treated as **informational hints**, not ground truth — always verify on a real device
- Edge cases (variable fonts, WOFF/WOFF2, obscure cmap formats, non-standard TMP asset configurations) may produce incorrect output

**Roadmap:**
- Manual code review and hardening of the OpenType cmap parser
- Expanded test coverage across font formats (OTF, TTC, variable fonts)
- WOFF/WOFF2 support (currently falls back to Unity API with a warning)
- CI validation against known font files

**Need a feature or found a bug?** Open an issue with your font file and a description of the unexpected result.

## Contributing

Contributions are welcome:
- **Bug reports**: [Open an issue](https://github.com/LLarean/unity-glyph-font-checker/issues)
- **Feature requests**: Describe your use case in an issue
- **Pull requests**: For bug fixes or improvements

---

<div align="center">

**Made with ❤️ for the Unity community**

⭐ If this project helped you, please consider giving it a star!

</div>
