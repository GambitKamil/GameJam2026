# Cell Phase 2 bugs

| ID | Severity | Repro | Expected | Actual | Evidence | Owner | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CELL-201 | P1 | Start Cell and watch the step off the bunk around 2.4 s. | Boots pass over the rod and plant without intersecting it. | A boot enters the stationary rod. Side and top views plus oriented shape checks show genuine 3D overlap. | `Phase2Before/Step-peak-side.png`, `Step-peak-top.png`, 119.50 mm overlap proxy at 2.400 s. | Rod and foot | In progress |
| CELL-202 | P1 | Open the gate with the retrieved key and wait on the cell side. | Right edge reveals 10–15% of the previous frame width, with an eased transition. | The camera retains its normal follow framing after the gate opens. | `Phase2Before/SHOT-2.png`. | Camera | In progress |
| CELL-203 | P1 | Compare the gate opening with the key still in play and after unlocking. | Readable near and far corridor planes and a local light on the key. | Post-gate frame shows mostly one wall plane and the far edge of the props. The key has no local light track. | `Phase2Before/SHOT-2.png` and scene inspection. | Depth and lighting | Open |
| CELL-204 | P2 | Run the first Phase 2 camera probe to the gate. | Both lane bounds produce a measurable reveal ratio. | The pre-open sample watched the route phase before the key interaction actually ran. | `Phase2Before/verification.log`, closed lane bounds were NaN. | QA | Verified in Phase2Baseline |
| CELL-205 | P2 | Capture SHOT 1 or SHOT 2 while using a non-16:9 camera aspect. | Export matches the tested aspect. | QA capture always exports 960x540 and stretches diagnostic images at other ratios. | Current Capture render target dimensions. | QA | In progress |
