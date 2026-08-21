# MVP GD&T Subset

Documented before implementation. Mirrored in `config/gdt-subset.default.json`.

## Supported controls

| Control        | Symbol | Schema              | Evaluation |
|----------------|--------|---------------------|------------|
| Position       | ⌀⌖     | `⌀t(L|M) datum[A-Z]…` | Measured true position within the tolerance zone |
| Flatness       | ⏥     | `⏥ t`               | Surface between two parallel planes `t` apart |
| Parallelism    | //     | `// t datum[A-Z]`    | Feature within zone parallel to datum |
| Perpendicularity| ⊥     | `⊥ t datum[A-Z]`     | Feature within zone perpendicular to datum |

## Explicitly out of scope (MVP)

- Runout (circular/total), concentricity, symmetry, angularity, profile of a surface.
- Composite position and multiple single-segment position frames.
- Datum reference frame modifiers beyond the listed single-datum forms.

## Rule placement

Each rule lives in `src/DrawVerify.Application/Verification/GdtRules/` as a
self-contained class (`PositionRule`, `FlatnessRule`, `ParallelismRule`,
`PerpendicularityRule`), invoked by `GdtValidator` and aggregated by
`VerificationEngine`. The supported subset is config-driven.