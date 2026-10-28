# Generated power icons

`neow_ciallo_64.png` is the status/hover-tip icon. `neow_ciallo_256.png` is used by power application, removal and flash effects.

Both are transparent square PNG exports of the approved v10 Ciallo Neow illustration, generated with OpenAI's built-in image tool using the user's character and Ciallo references. The 1469 × 1071 source was scaled proportionally into each square with transparent padding; the character is not stretched or cropped.

The PNGs are embedded in `NeowsCompany.dll`. Godot decodes each texture on first UI access and reuses it for all generated rule powers. Native powers retain their own textures. Valid native fallback paths remain available for model metadata; the generated power's `Icon` and `BigIcon` getters supply these embedded textures directly.
