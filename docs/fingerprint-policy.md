# Fingerprint Policy

Documented before implementation. Mirrored in `config/fingerprint-policy.default.json`.

## Purpose

Detect unintended drift between the artifacts used during verification and the
artifacts present at approval — i.e., prove "what was verified is what was approved."

## Algorithm

- SHA-256 over a versioned UTF-8 manifest containing document/version identifiers,
  required metadata, and all submitted artifact bytes in ordinal deterministic
  relative-name order using the documented UTF-8 `name=value` manifest format;
  artifact lengths are validated against bytes read. The canonicalization
  version is persisted with each submission.
- In `Application.Fingerprinting`, `DocumentFingerprintService` computes fingerprints;
  `FingerprintPolicy` describes the rules; both are config-driven.

## Stages

| Stage          | Files fingerprinted |
|----------------|---------------------|
| OnImport       | `*.step`, `*.stp`, `*.iges`, `*.igs` |
| OnVerification | `*.step`, `*.stp`, `*.iges`, `*.igs` |
| OnApproval     | `*.step`, `*.stp`, `*.iges`, `*.igs`, `*.pdf` |

## Behavior

- A fingerprint mismatch between verification and approval blocks the
  `Approved` workflow transition (see `config/workflow.default.json`).
- Fingerprints are recorded in the audit trail via `IAuditLogger`.
