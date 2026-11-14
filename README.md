# KSP Mesh Mixer

B9PartSwitch for KSP 2 Redux. It keeps **existing KSP 1 `ModuleB9PartSwitch` configs working** after
Project Shakespeare imports the parts — no cfg edits, no repacking, nothing to fix on the parts.

## Why

The KSP 1 importer already converts B9 switches, but only when a subtype names a transform, texture or
node. That means:

* fuel-only subtypes (`tankType`, `RESOURCE`) are dropped, so the switch never appears;
* switching never changes the fuel, and never applies a subtype's `mass`;
* subtype `MODULE` blocks are ignored.

KSP Mesh Mixer takes that conversion over and emits the full V-SwiFT variant data instead.

## What a subtype can switch

| B9 config | Result in KSP 2 |
| --- | --- |
| `transform = name` (one or more) | Meshes toggle. Every subtype's transforms start hidden and the active subtype's are enabled. |
| `GAMEOBJECTS` (stock variants) | The same mesh switch for `ModulePartVariants` styles: one mesh per style, all others hidden. |
| `TEXTURE { ... }` / `_MainTex = path` | Texture swap on the named transforms, including `_BumpMap` normal maps. |
| `primaryColor` / `secondaryColor` | Written to KSP 2's paint slots. Only materials that have `_PaintA`/`_PaintB` change; imported KSP 1 shaders do not, so nothing is visibly tinted unless `TintMaterials` is turned on (off by default — it double-tints textures that already carry the style's colour). |
| `NODE { name; position }` | Attach nodes move with the subtype (scaled by the part's `rescaleFactor`). |
| `tankType = X` and/or `RESOURCE { ... }` | Tank contents are cleared and refilled from the subtype's resources (`unitsPerVolume × baseVolume`, `percentFilled`). |
| `mass = X` or `addedMass = X` | Applied as a delta against the part's `mass`. |
| `ModuleB9DisableTransform` | Those meshes stay hidden for every subtype. |

Defaults follow B9: `currentSubtype`, else the highest `defaultSubtypePriority`, else the first subtype.
Switches with a single subtype are left alone.

## Stock `ModulePartVariants`

Stock, DLC and ReStock parts do not use B9 at all — they use KSP 1's own `ModulePartVariants`, which
the importer reports as "unsupported". Those parts now get a **Style** dropdown with the part's base
look plus one entry per `VARIANT` block, applying each variant's

* `GAMEOBJECTS` block — **which mesh object that style uses**. Every style's objects are hidden on the
  prefab, so only one hull is ever visible: the grid thumbnails and the placed part come out clean
  instead of z-fighting, and selecting a style switches exactly one mesh on. This is the same
  hide-all/activate-one mechanism the B9 `transform` switch uses.
* `primaryColor` / `secondaryColor`,
* `TEXTURE` blocks (`mainTextureURL`, `_MainTex`, `_BumpMap`).

`baseVariant` decides which style a freshly built part starts in (no synthetic "Default" entry is added
when it is set). Without `baseVariant`, the module's own colours are offered as `Default` first.

## Install

Drop `KSPMeshMixer` (or the built zip) into `<KSP 2 install>\mods\`. Nothing else to configure.

## Settings

`KSPMeshMixer-config.json` next to the dll:

| Key | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Off hands every part back to the importer's own B9 handling. |
| `SwitchTextures` | `true` | Apply `TEXTURE` blocks (B9 and `ModulePartVariants`). |
| `SwitchColors` | `true` | Apply `primaryColor`/`secondaryColor`. |
| `TintMaterials` | `false` | Tint the part's `_Color` as KSP 1's shader did. **Off by default**: KSP 1 variant colours are often a shade of the style (an orange style's `#f49841`), and multiplying an already-orange texture leaves a visible double-orange cast. Turn it on only for parts whose materials are neutral (`_Color` = white). |
| `SwitchResources` | `true` | Swap tank contents per subtype. |
| `SwitchMass` | `true` | Apply subtype `mass`/`addedMass` deltas. |
| `VerboseLogging` | `false` | Log every converted part, not just the summary. |

## Log

Every converted part logs one line to `Ksp2.log`:

```
[KSP Mesh Mixer] 'opt_s2intake': built 1 B9 switch(es) from 1 ModuleB9PartSwitch block(s).
[KSP Mesh Mixer] Applied 2 of 2 Harmony hook(s).
```

If a part behaves differently from KSP 1, set `VerboseLogging: true` and check that line first — a part
that logs nothing has no `ModuleB9PartSwitch` in its config (or only one subtype).

## Compatibility

* Reads the B9 syntax as written: `moduleID`, `switcherDescription`, `baseVolume`, `switchInFlight`,
  `currentSubtype`, `defaultSubtypePriority`, `SUBTYPE`, `TANK_TYPE`/`tankType`, `RESOURCE`, `NODE`,
  `TEXTURE`, `primaryColor`, `secondaryColor`, `mass`, `addedMass`, `crashTolerance`, `maxTemp`,
  `ModuleB9DisableTransform` — plus stock `ModulePartVariants` (`VARIANT`, `mainTextureURL`,
  `baseDisplayName`).
* Tolerates ModuleManager decorations on node names (`@MODULE[...]`, `%SUBTYPE`, `B9_TANK_TYPE:NEEDS[...]`).
* Tank types come from the importer's finalized config database, so `B9_TANK_TYPE` definitions from any
  installed mod are used. `LFO`, `LF`, `OX`, `LM`, `LMOx` and `LiquidFuel` are built in.
* Nothing is written into `mods\KSP1`.

### Not supported yet

* Subtype `MODULE` blocks (`moduleActive`, e.g. OPT's ablator on the "Super" style) — parsed and
  counted, not applied.
* `crashTolerance` / `maxTemp` / `skinMaxTemp` per subtype.
* `switchInFlight` (KSP 2 variants are editor-side).
* `ModulePartVariants` `MODULE` override blocks inside a `VARIANT`, and `useMultipleDragCubes`.

## Build

```powershell
.\build.ps1                      # tests, builds, packages dist\KSPMeshMixer-<version>.zip
.\build.ps1 -SkipTests -SkipPackage
.\build.ps1 -Ksp2Dir "D:\Steam\steamapps\common\Kerbal Space Program 2"
.\install.ps1                    # copy dist into the game's mods folder
```

Targets .NET Framework 4.7.2 and references the game assemblies from the install.

The test project (`tests\KSPMeshMixer.Tests`) compiles the B9 reading layer without the game and checks
it two ways: synthetic configs for the exact B9 semantics (transforms, textures, defaults, percent
filled, node moves), and a scan of the real `mods\KSP1` configs to prove the parser copes with what
mods actually ship.

## How it works

Two Harmony hooks into the KSP 1 importer:

* `Ksp1B9PartSwitchVariantBuilder.TryBuild` — prefix that builds the `Data_PartSwitch` itself and
  returns false, replacing the importer's narrower conversion. Any failure falls back to the original.
  It converts both `ModuleB9PartSwitch` and stock `ModulePartVariants` into one variant data set.
* `Ksp1B9PartSwitchVariantBuilder.AddLocalizationTerms` — postfix that registers the labels for switches
  the importer's own pass skipped, using the same `KSP1/Parts/<part>/Variants/...` key scheme.

Colour switching needs a transformer V-SwiFT does not ship, so the mod adds its own
(`[Transformer("MeshMixerColor")]`). V-SwiFT builds its transformer registry by scanning every loaded
assembly for that attribute, which is why a mod-provided transformer round-trips through the part JSON
like the built-in ones.

The mod targets netstandard 2.1 rather than net472: `ITransformer` has default interface members, which
the .NET Framework compiler cannot implement.
