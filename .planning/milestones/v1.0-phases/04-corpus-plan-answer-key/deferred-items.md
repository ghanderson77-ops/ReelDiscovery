# Phase 04 Deferred Items

Out-of-scope discoveries logged during plan execution (not fixed by the plan that found them).

| Found in | Item | Notes |
|----------|------|-------|
| 04-12 | `Padding = 1` cards show square inner corners | A `CardPanel` with `Padding = 1` and a full-bleed white child (TLP, grid) covers the 8 px rounded corners, so the card reads as square with the border broken at each corner. Pre-existing Phase 3 pattern (visible in `shared-helpers-1100x750.png`); the histogram and tables cards on Review Plan (04-18) will inherit it. Possible fix: clip the child with a rounded `Region`, or inset content by the radius at the corners. |
