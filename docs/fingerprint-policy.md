# Fingerprint Policy

Documented before implementation. Mirrored in `config/fingerprint-policy.default.json`.

## Purpose

Detect unintended drift between the artifacts used during verification and the
artifacts present at approval — i.e., prove "what was verified is what was approved."

## Algorithm

- SHA-256 over normalized file content.
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