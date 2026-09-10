# GD&T support and capability contract

**Document version:** 2.0  
**Effective date:** 2026-09-10  
**Status:** Draft — stakeholder sign-off pending

This is the canonical, versioned description of the GD&T controls DrawVerify
documents for the MVP and the capabilities currently evidenced in the
repository. “Documented target” does not mean “implemented verifier support.”

## Standards and interpretation

The proposed primary alignment is **ASME Y14.5-2018**. It governs the symbols,
datum rules, and tolerance interpretation used by this document. **ISO 1101**
is an applicable cross-reference for international drawings; an exact
company-approved ASME/ISO crosswalk and any edition-specific interpretation
remain pending stakeholder confirmation. Symbols below are the conventional
ASME notation (several have ISO equivalents).

## MVP target scope

The requested documentation target is all 14 controls in the five categories
below. The current executable baseline is only **Position, Flatness,
Parallelism, and Perpendicularity**, as reflected by
`config/gdt-subset.default.json` and the four rule classes in
`src/DrawVerify.Application/Verification/GdtRules/`. The remaining controls
are documented target scope, not a claim of current extraction or verification.

### Form

| Control | Symbol | What it constrains | Datum references | Illustrative feature-control-frame example and representation |
|---|---|---|---|---|
| Straightness | ⏤ | A line element or derived median line within a straightness zone. | Not applicable for surface straightness; optional for axis straightness. | `⏤ 0.05` or `⏤ ⌀0.10 A`. Illustrative FCF for a STEP PMI or 2D drawing annotation; not currently extracted. |
| Flatness | ⏥ | A surface between two parallel planes. | Not applicable. | `⏥ 0.03`. Documented/configured control; the DXF parser is dimension-only. |
| Circularity | ○ | Each circular cross-section within two concentric circles. | Not applicable. | `○ 0.02`. Illustrative STEP PMI/2D FCF; runtime extraction and verification are not evidenced. |
| Cylindricity | ⌭ | A cylindrical surface within two coaxial cylinders. | Not applicable. | `⌭ 0.05`. Illustrative FCF; not currently supported by the verifier. |

### Profile

| Control | Symbol | What it constrains | Datum references | Illustrative feature-control-frame example and representation |
|---|---|---|---|---|
| Profile of a Line | ⌒ | The line profile of each section within a uniform or composite zone. | Optional; required when the profile is oriented or located to datums. | `⌒ 0.10 A B`. Illustrative STEP PMI or 2D drawing FCF; not implemented. |
| Profile of a Surface | ⌓ | The entire surface within a profile tolerance zone. | Optional in principle; required for the documented limitation when orientation/location is datum-related. | `⌓ 0.20 A B C`. Documented target only with datum references; no parser/verifier evidence. |

### Orientation

| Control | Symbol | What it constrains | Datum references | Illustrative feature-control-frame example and representation |
|---|---|---|---|---|
| Angularity | ∠ | A feature at a specified basic angle to a datum. | Required. | `∠ 0.08 A` with basic `30°`. Illustrative STEP PMI/2D FCF; not currently verified. |
| Perpendicularity | ⊥ | A feature at 90° to a datum within the tolerance zone. | Required for orientation; surface form may be datum-independent by policy. | `⊥ 0.05 A`. Current rule-backed control; STEP PMI/DXF GD&T extraction is unavailable. |
| Parallelism | ∥ (often `//`) | A feature parallel to a datum within the tolerance zone. | Required for orientation; may be omitted for a form-only surface use. | `∥ 0.04 A`. Current rule-backed control; annotation extraction is not evidenced. |

### Location

| Control | Symbol | What it constrains | Datum references | Illustrative feature-control-frame example and representation |
|---|---|---|---|---|
| Position | ⌖ (often preceded by `⌀`) | The location of a feature axis, center, or median plane relative to basic dimensions and datums. | Required for a datum-referenced location; datumless position is a limited special case. | `⌀0.10 ⌖ M A B C` (MMC `M`). Current config/rule-backed scope; PMI/DXF FCF extraction is not evidenced. |
| Concentricity | ◎ | The median points of a feature relative to a datum axis. | Required. | `◎ 0.05 A`. Illustrative STEP PMI/2D FCF; not currently supported. |
| Symmetry | ⌯ | A derived median plane or line symmetrically located about a datum center plane/axis. | Required. | `⌯ 0.10 A`. Illustrative FCF; not currently supported. |

### Runout

| Control | Symbol | What it constrains | Datum references | Illustrative feature-control-frame example and representation |
|---|---|---|---|---|
| Circular Runout | ↗ | Variation of a surface element during one rotation about a datum axis. | Required. | `↗ 0.03 A`. Illustrative STEP PMI/2D FCF; not currently extracted or verified. |
| Total Runout | ⌰ | Total variation of the entire surface during rotation about a datum axis. | Required. | `⌰ 0.08 A`. Illustrative STEP PMI/2D FCF; not currently extracted or verified. |

The examples are notation references, not tested integrations. `⌀`, `M`
(MMC), and `L` (LMC) are shown only where illustrative; advanced modifier
semantics are not part of the current executable baseline.

## Out of scope and future extensions

Out of scope for the current phase are composite feature-control frames,
multiple-segment/composite position behavior, advanced modifiers including
LMC and statistical modifier `S`, datum reference frame modifiers beyond the
current four-control forms, and executable support for the ten documented
target controls until implemented and approved. Dynamic profile and the
tangent-plane modifier are planned for a future phase. These classifications
may change only through stakeholder review and a new document version.

## Extraction and verification capability matrix

| Control(s) | STEP PMI extraction | 2D DXF GD&T annotation extraction | 3D-to-2D GD&T matching/verification | Current evidence and limitation |
|---|---|---|---|---|
| Position, Flatness, Parallelism, Perpendicularity | Unsupported/unverified; XCAF/PMI is an extension point. | Unsupported; `DxfDimensionParser` reads DIMENSION entities and tolerance text, not FCF annotations. | Unsupported/unverified for GD&T; current rules/config are the four-control baseline, not a 3D/2D annotation bridge. | `config/gdt-subset.default.json`, `GdtRules/`, `GdtValidator.cs`, and OCCT docs. |
| Straightness, Circularity, Cylindricity | Unsupported/unverified. | Unsupported. | Unsupported/unverified. | No repository extraction, rule, or matching evidence. |
| Profile of a Line, Profile of a Surface | Unsupported/unverified; Profile of a Surface is a documented target only with datum references. | Unsupported. | Unsupported/unverified. | PMI/DXF GD&T foundation is outside the current OCCT integration. |
| Angularity, Concentricity, Symmetry | Unsupported/unverified. | Unsupported. | Unsupported/unverified. | No repository extraction, rule, or matching evidence. |
| Circular Runout, Total Runout | Unsupported/unverified. | Unsupported. | Unsupported/unverified. | No repository extraction, rule, or matching evidence. |

The OCCT integration documentation explicitly places XCAF, PMI, GD&T, and DXF
outside the current foundation. This page must not be read as claiming native,
OCCT, P/Invoke, STEP PMI, DXF FCF, or 3D-to-2D support.

## Known limitations

- The documented 14-control target exceeds the current four-control
  configuration/rule baseline; `GdtValidator` currently contains no executable
  validation logic beyond that baseline’s surrounding design.
- DXF support currently parses standard `DIMENSION` entities, not GD&T feature
  control frames or drawing annotation semantics.
- STEP PMI/XCAF extraction and GD&T matching are extension points, not current
  dependencies or capabilities.
- Tolerance values and modifiers in the examples are representative notation,
  not promises of supported parsing or evaluation.

## Stakeholder review and sign-off

**Approval status:** Pending — no authorized stakeholder approval evidence was
provided with this revision. No consensus or sign-off is being fabricated.

| Required record | Current value |
|---|---|
| Approver | Pending stakeholder assignment |
| Review date | Pending |
| Decision | Pending confirmation of ASME Y14.5-2018 primary alignment and 14-control target wording |
| Feedback received | None recorded in repository/story context |
| Feedback disposition | Pending review; future feedback will be incorporated or explicitly dispositioned in a subsequent revision |

This document is ready for engineering review. The next approved revision
must replace the pending fields with the authorized decision and record how
stakeholder feedback changed or confirmed the scope.
