# Project ancestry

RyGT7 preserves the history of https://github.com/mlgprorektm8/RyGT, which derives from
https://github.com/aarvsn/Ryty. The GT7 branch started from RyGT commit `882312d`.
Its launcher incorporates upstream changes through `a42bb5298dc743788bdbc0c59763bac16751c654`.

shadGT7 preserves https://github.com/mlgprorektm8/shadGT history. Its GT7 branch started from
`087e2a70` on `ryty-gpu`, and includes the intervening GT7 compatibility work and rendering fixes.

These are separate repositories under the same account as ryGT/shadGT, with upstream history retained.
Existing upstream repositories are not renamed or overwritten.

## Ryty upstream syncs

RyGT took https://github.com/aarvsn/Ryty through `1ca5686` (PR #23, 2026-10-28) by ordinary merges.
Ryty then rewrote its history: `main` now starts at a single squashed commit, `8285302`
("release: 2.0.0"), which shares no commits with RyGT. Ryty 2.0.0-2.2.0 (`7c01144`) was merged
with `8285302` grafted onto `1ca5686` for the merge only (`git replace --graft 8285302 1ca5686`,
removed afterwards), so git merged just upstream's changes since `1ca5686`. The merge commit's
second parent is `7c01144`, so the next sync merges `aarvsn/main` normally, with no graft.

Kept from RyGT over upstream in that merge: the RyGTServer HTTP bridge (`libSceHttp`), the JPEG
encoder (upstream rejects the 4:2:2 that GT7's gallery export asks for), PE DllCharacteristics
without ASLR, the console subsystem as the Windows default, RyGT's own CI (upstream's
`.github/workflows/CI.yml` stays deleted), and `--app0` (moved into upstream's `PortOne.cpp`).
