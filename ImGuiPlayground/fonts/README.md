# Default UI font

`JetBrainsMono-Regular.ttf` is an unmodified copy of the game's default font asset,
from `ksa-linux/Content/Core/JetBrainsMono-Regular.ttf` as supplied for this work.
Only the font and its accompanying license were copied; no old game binaries are used.

- Upstream: https://github.com/JetBrains/JetBrainsMono
- Copyright 2020 The JetBrains Mono Project Authors
- License: SIL Open Font License 1.1 — `JetBrainsMono-Regular-license.txt`
- SHA-256: `a0bf60ef0f83c5ed4d7a75d45838548b1f6873372dfac88f71804491898d138f`

The project copies these assets into build/publish output. `PlaygroundHost` loads
this font through BRUTAL's font atlas and selects it as the default at 18 logical
pixels. This matches the font face, not a claim of exact KSA theme/DPI/size parity.
