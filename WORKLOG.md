# Worklog

## 2026-11-06: planning

Planned the game with Matthew over three rounds of questions. Decisions are in PLAN.md.

Read every `.bsp` header and entity lump in `maps/` while content was still being copied in.
Matthew expected a mix of games, but it's 363 Quake 2 (IBSP 38) and one Quake 1 (`rene40q1.bsp`).
`q3dm7.bsp` is a Quake 2 format map by Gokart despite its name, so names can't be trusted and the
header is what counts.

Eight maps carry the titles of the original Quake 2 levels (`q2dm1` to `q2dm8`), and `maps/` holds many
edits of The Edge as well. Those stay out of the test and tuning set.

The texture check isn't meaningful yet. On the first pass `textures/` was flat and only 5% of references
resolved. 265 subfolders appeared after that, so the copy was still running. 512 bare texture names are
shared by more than one folder (`e2u1/metal37_2` and `e2u2/metal37_2`), so the folder structure has to be
kept. Matching on bare names would pick the wrong one.

No palette for `.wal` files found yet.

## 2026-11-06: milestone 0, setup

Matthew ran `git init` and made the GitHub repo. Added the Core, Tests and Godot projects, laid out
like Overburden: net8.0, Godot.NET.Sdk 4.7.1, Debug with the JIT optimising, and the Godot project in
its own subfolder so it never tries to import the content.

Checked: the solution builds with no warnings, 7 tests pass, and headless Godot imports and passes
`--smoke-test`. `start.cmd --smoke-test` passes too, so the folder name's `=` and spaces are fine. That
windowed run reported Forward+ on the RTX 3070 Laptop GPU.

The only real code so far is `BspFormat`, which reads a map's type from its header. It's there to prove
Core, Tests and Game connect, and milestone 1 builds on it.

Not done: content hasn't moved into `content/`, because textures were still copying. Modified times
come from the source files when copying, so they say nothing about progress. Creation times do.
