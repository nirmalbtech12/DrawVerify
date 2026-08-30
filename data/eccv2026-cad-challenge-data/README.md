# ECCV 2026 CAD challenge samples

This directory contains the two matched training samples requested from the public Hugging Face dataset [`jingwei-xu-00/eccv2026-cad-challenge-data`](https://huggingface.co/datasets/jingwei-xu-00/eccv2026-cad-challenge-data).

Each directory is keyed by the dataset `sample_id`; the DXF and STEP files therefore remain paired by ID:

| Sample | 2D drawing | Equivalent 3D model |
|---:|---|---|
| 0 | `train/sample-000000/000000.dxf` | `train/sample-000000/000000.step` |
| 1 | `train/sample-000001/000001.dxf` | `train/sample-000001/000001.step` |

Source paths are the dataset's `train/techdraw/dxf/{id}.dxf` and `train/target_step/{id}.step` entries. Files were downloaded from the dataset `main` revision on 2026-08-30.

SHA-256 checksums:

```text
9773d3f006f60041aad034379404818d87e80b3a620c89d24ec6f7052e6acc6e  train/sample-000000/000000.dxf
c7a896297fcd14e9094db34b316601caf880fa6e9be346a9894f758df06767e0  train/sample-000000/000000.step
c26e7b7f8600c9409627adceaa1afc895dec21e7fc6459318305ca7994f87394  train/sample-000001/000001.dxf
d4c5e42b571eb859ff1f2c8d5090c905df3ef3c98989d7b611305cbda542bd1d  train/sample-000001/000001.step
```

Dataset licensing and usage terms are governed by the upstream dataset card and must be reviewed before redistribution.
