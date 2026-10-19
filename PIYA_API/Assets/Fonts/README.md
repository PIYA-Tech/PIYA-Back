# Embedded PDF fonts

These unmodified Liberation Sans 2.1.5 font files are distributed under the SIL Open Font License 1.1; see `LICENSE` for the complete copyright and permission notice. The license is copied into build/publish output alongside the application, and the four font faces are embedded in the API assembly.

Upstream release: https://github.com/liberationfonts/liberation-fonts/releases/tag/2.1.5

Source archive: https://github.com/liberationfonts/liberation-fonts/files/7261482/liberation-fonts-ttf-2.1.5.tar.gz

| File | SHA-256 |
| --- | --- |
| LiberationSans-Regular.ttf | `76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8` |
| LiberationSans-Bold.ttf | `788abee4c806d660e8aee46689dd8540cd4bb98da03dcc9d171ce3efd99a9173` |
| LiberationSans-Italic.ttf | `e5bae5c4cde31f22142753855f4f8fb86da6ff39955ed3c0a11248b0d16948b0` |
| LiberationSans-BoldItalic.ttf | `698da70fc191cc5f33ad4d6d3fe830fe4624b898ea2e3169955928b7c491f1ee` |

`PiyaPdfFontResolver` selects these faces explicitly. It does not load uploaded fonts or depend on operating-system font discovery. All five PDF exports use this family; changing the font assets requires testing both text extraction and rendered layout, including Azerbaijani characters.
