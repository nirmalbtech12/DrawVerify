# LGPL Compliance — OCCT

DrawVerify links against Open CASCADE Technology (OCCT).

## Obligations

- OCCT is licensed under LGPL-2.1 with additional terms (the "OCCT Public License").
- The native OCCT libraries distributed with DrawVerify may be LGPL; when we link
  dynamically and allow relinking, the LGPL obligations apply to the OCCT components
  and must be recorded here.
- This project is itself distributed under LGPL-3.0 (see `LICENSE.txt`).

## Distribution requirements

1. Provide the OCCT source or a link to it (OCCT version tagged in
   `docs/deployment.md` and `src/DrawVerify.OcctWrapper/README.md`).
2. Retain OCCT copyright/license notices in the distributed native binaries.
3. Ensure the native OCCT libraries can be relinked/replaced by the end user
   (dynamic linking, no static-linking of LGPL code).
4. Do not relicense OCCT-derived code; modifications to OCCT itself remain subject
   to the OCCT Public License terms.

## Contact

Compliance questions: record decisions and changes here; review before any release.