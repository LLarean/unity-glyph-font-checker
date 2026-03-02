# ![unity](https://img.shields.io/badge/Unity-100000?style=for-the-badge&logo=unity&logoColor=white) Glyph Font Checker

[![Releases](https://img.shields.io/github/v/release/llarean/unity-glyph-font-checker)](https://github.com/LLarean/unity-glyph-font-checker/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/LLarean/unity-glyph-font-checker/blob/main/LICENSE.md)
![stability-stable](https://img.shields.io/badge/stability-stable-green.svg)
[![CodeFactor](https://www.codefactor.io/repository/github/llarean/unity-glyph-font-checker/badge)](https://www.codefactor.io/repository/github/llarean/unity-glyph-font-checker)
[![Support](https://img.shields.io/badge/support-active-brightgreen)](https://github.com/llarean/unity-glyph-font-checker/graphs/commit-activity)
[![Downloads](https://img.shields.io/github/downloads/llarean/unity-glyph-font-checker/total)](https://github.com/LLarean/unity-glyph-font-checker/archive/refs/heads/main.zip)

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
- **Unique / Present / Fallback / Missing** — full character breakdown
- **Dynamic atlas summary** — whether missing chars can be generated at runtime via the source font
- **Fallback coverage** — which chars are rescued by each fallback font in the chain
- **Atlas setting warnings** — potential issues with atlas size, render mode, multi-atlas, or `Clear Dynamic Data On Build`
- **Missing characters list** — sorted, with a copy-to-clipboard button

## Atlas Checks (Dynamic TMP Fonts Only)

| Setting | Issue | Severity |
|---|---|---|
| `Clear Dynamic Data On Build` = true | Glyphs cleared on build, must regenerate at runtime | ⚠ Warning |
| Atlas < 256×256 px | Very likely to overflow | ⚠ Warning |
| Atlas < 512×512 px + SDF render mode | SDF adds significant per-glyph padding — increased risk | ⚠ Warning |
| Atlas < 512×512 px | May overflow with large character sets | ℹ Info |
| Multi-atlas disabled + atlas > 80% full | Near overflow with no recovery path | ⚠ Warning |
| Multi-atlas disabled | Atlas cannot grow when full | ⚠ Warning |
| Atlas > 90% full (multi-atlas enabled) | Will allocate a new texture on overflow | ℹ Info |

## Requirements

- Unity 2021.3+
- TextMeshPro (included in `com.unity.ugui >= 2.0.0`)

## Project Status

This project is in **active development**. Core functionality is stable and production-ready.

**Current focus:**
- **Bug fixes**: Addressed as reported
- **Improvements**: Ongoing based on real-world localization use cases
- **New features**: In consideration

**Need a feature?** Feel free to open an issue with your use case!

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