# Core and directed scenario v0.2

The independent .NET Standard 2.1 package has no Unity dependency. Rules.cs contains the fixed rules, immutable snapshots, sparse coordinate dictionaries, exact occupancy predicates and a single-commit transaction. Cpu.cs contains observation construction and the CPUs. Scenario.cs contains guards, live/replay APIs and a separate transition verifier.

This is the approved fixed 4D demonstration ruleset, not standard shogi. All eight unpromoted kinds are implemented. Both occlusion policies are immutable for a run. Closed Supercover uses rational interval comparisons, including boundary contact; lattice-line checks intermediate integer points. One-step moves and knights do not use slider blocking.

## Actual CPU behavior and disclosure

DirectedLive evaluates candidates at each turn. A evaluates all pawn candidates, with non-pawn rescue only if no pawn move exists. B constructs a three-coordinate search board from Observation3D and evaluates every legal root plus a stable, capture-prioritized, equally budgeted set of opponent replies. The budget is 5,000 evaluation nodes; elapsed time does not choose the result. Each candidate, its tactical score, directed score, reply-node count, selected move and input hash are logged.

The scenario preferences are deliberately strong: +100,000 for the selected observed piece and −10,000 per Manhattan cell from its goal. A applies the preference to its central pawn. B applies it to a visible pursuer or an ordinary development goal. These preferences can override tactical caution; B is not claimed to be optimally strong. This demonstrates deterministic directed CPU play, not an AI independently discovering the story. The preferences are position-based goals, not a live lookup of the 27 recorded moves. A different legal position can change the selection or cause ScenarioMismatch.

B's goals depend only on its observation: approach a visible central invader and match its visible height; otherwise develop silvers and golds, open the rook file, advance the rook, and open the opposite flank. The same development policy chooses B's silver on ply 2, before any W escape. Completed goals and goals with no legal progress are skipped using observed occupancy. No Director signal conditioned on the hidden hero, hidden piece count, W coordinate, or escape-ply trigger enters B. Observation3D contains only Pieces, Turn, PublicPly and its own hash. Its pieces and search tree have Position3, with no W coordinate or full-state reference. Disappearing pieces are absent from every newly constructed active board. Full-state diagnostics are separate from CPU input. A dedicated counterfactual test changes PublicPly without changing the observed position and confirms identical ordinary-development moves, candidate scores and node counts.

## Initial position and full route

Coordinates are (f,u,v,w). A's back rank is f=−4, pawns f=−2, rook (−3,−3), bishop (−3,3); B is rotated 180 degrees. Back ranks from u=−4 to 4 are lance, knight, silver, gold, king, gold, silver, knight, lance. All start at v=w=0. Scenario `central-pawn-directed/2` implements the user's revised sequence in 27 plies. The ruleset remains `showcase-asymmetric-4d-king-capture/0.1`; the scenario revision does not change legality or B's capabilities.

| Phase | Plies and actual moves |
|---|---|
| Opening2D | 1–4: A's central pawn reaches f=0; B develops a silver, then the central pawn establishes a real attack. |
| Escape3D / Pressure3D | 5/6, 7/8, 9/10: A escapes to v=1, 2, 3 and B answers at the observed height each time. Authoritative attacks confirm renewed danger after 6, 8 and 10. |
| Rotate4D | 11: A moves from w=0 to w=1; 12: B develops a gold in its ordinary w=0 position. |
| Attack4DEnding | 13–19 odd plies advance A to f=4, 21–25 descend to v=0 while w=1, and 27 returns to w=0 and captures B's king. B answers each intervening even ply with gold, silver, rook or pawn moves in its observed 3D position. |

All 14 A moves use `A-Pawn-0`; non-pawn rescues are zero. All 13 B turns are executed. Only the king is captured, leaving 39 pieces. The lattice-line run evaluates 65,246 nodes in total; every move stays within its 5,000-node budget. The initial hash is `68b1ef1c7742d934f392aeeea45b669fed3a3fd9e361e43c00d9815e5d14786a`, final hash `64f11ef90c9c09d48b4af7c48874c44a60b8e2d23b27e10c4578131403066510`, and transcript hash `8aeb2a56571719a6b1ea74424da8279a44864edd25c3d766a6680800d991f466`.

## Runtime integration

Create a ScenarioRunner with Bootstrap.CreateScenario. For each live turn: runner.DecideNext(transaction.State), transaction.Reserve(decision.Move), animate the accepted move, transaction.Commit(), then runner.RecordCommitted(before, after, decision). Reserve does not mutate the state. Cancel restores the unchanged snapshot. Commit twice fails. Restart cancels outstanding work. Display changes do not enter these APIs.

VerifiedReplay uses a separately labeled, hash-checked `.ndsr` fixture. runner.LoadReplay(text) verifies the entire fixture independently; runner.DecideReplayNext(state, verifiedReplay) returns a stored move with zero search nodes. Runtime still reserves, validates and commits every move. There is no automatic live/replay fallback.

Presentation metadata is published by ScenarioRunner: `First3DPly=5`, `ThreeDExchangeCount=3`, `First4DPly=11`, `ExpectedPlies=27`, and `ExpectedRemainingPieceCount=39`. `Requires4DViewForPly` is true for the nine odd plies 11–27. The director shows exactly one A move at H90, returns to H0, then shows the next B reply. The final −w king capture also moves at H90 before returning to H0 for the terminal light. These are display instructions under timeline `showcase-alternating-dimensions/0.2`; they never enter RuleCore or B's observation. Core tests verify the metadata and alternating turns. Actual rendered angles and event ordering require new Standalone evidence, and visual clarity remains subject to human review.

## Verification evidence

The package-free net9.0 harness checks piece predicates, both occlusions, hidden occupancy/attacks, terminal behavior, transactional interruption, full directed and replay runs, immutable configurations, counterfactual hidden positions/kinds/counts, rejected full-state/hash/endpoint input envelopes, injected hidden-dependent candidate scores, illegal recorded moves and state warps. The independent verifier does not call RuleCore, Movement, Occlusion or Apply. It rebuilds each transition from an independently checked initial arrangement. Its lattice check uses integer cross-products; its separate Supercover check uses bounded decimal intervals.

The revised harness has [36 passing tests](../evidence/core-v2/tests.json). It also verifies three 3D exchanges, rejects an otherwise legal early W escape that skips those exchanges, checks mixed-piece B development after the escape, and verifies the per-move presentation metadata. [DirectedLive](../evidence/core-v2/directed-live.json) and [VerifiedReplay](../evidence/core-v2/verified-replay.json) produce the same transcript hash. The [separate Supercover run](../evidence/core-v2/supercover/summary.json) also completes 27 plies with independent verification. Its transcript hash differs because the occlusion policy is part of the state identity.

`dotnet run --project tests/Showcase.Core.Tests -- evidence/core-v2` writes evidence/core-v2/tests.json. `dotnet run --project tools/Showcase.Console -- --output evidence/core-v2` writes the full two-mode run data and a portable replay fixture. Add `--occlusion supercover-closed/1 --output evidence/core-v2/supercover` for the separate policy. The current configuration and portable fixture are [central-pawn-directed-v2.json](../scenarios/central-pawn-directed-v2.json) and [central-pawn-lattice-v2.ndsr](../scenarios/central-pawn-lattice-v2.ndsr). Core build artifacts remain outside the Unity package in artifacts/bin and artifacts/obj. Earlier evidence/core and v1 fixtures remain historical; their 19-ply Standalone results do not establish the revised director's behavior.

These tests cover the pure core contract. They do not establish visual quality, render-pass consistency, measured Unity frame performance, or human acceptance.
