# HealthBars — PoE2

Configurable entity health overlay for ExileCore2. It displays life, energy
shield, mana, optional Ward, DPS, cast stages, and boss information.

## Logic

1. `Tick` reads valid player/monster/actor components and applies local JSON
   treatment rules and per-entity configuration.
2. It caches finite percentages, labels, cast state, and boss data while
   pruning invalid entities.
3. `Render` projects cached models through the camera and draws bars, labels,
   cast stages, and the boss panel.
4. Config/settings callbacks are named, guarded, and detached on teardown.

Ward remains an optional reflection boundary because the pinned binary has no
stable compile-time `Life.Ward` contract; missing Ward is treated as zero.

The Tick/Render paths enumerate monster and player buckets defensively, so a
bucket that is not yet present during an area transition or menu simply produces
no bars for that frame. Combined bars also skip the fill pass while all maxima
are zero, and the EHP percentage reports zero instead of emitting NaN geometry.

## Status

Build: **PASS**. Classification: **CURRENT_WITH_WARNINGS**; live entity and
Ward semantics remain unverified.

Detailed report: [PoE2 plugin catalog](../../README.md) ·
[audit](../../../docs/plugins/HealthBars/AUDIT.md).
