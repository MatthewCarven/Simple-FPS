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

## 2026-11-06: milestone 1, see a map

Copying finished and content moved into `content/`. The recount found 4,174 of 4,527 texture names at
their exact path, and 330 of 363 maps fully covered.

No `colormap.pcx` arrived, so the palette came from the content itself. `textures/new/q2.act` looked
right but has ten colours blanked to white, eight of them browns. The 887 PCX files that agree with each
other have the real values, which are now in `content/palette.act`.

Core now reads Quake 2 maps, meshes them, packs baked light into one atlas and decodes WAL textures. The
game draws the world and the moving brushes, with a fly camera. `--shot` saves a picture.

The whole-collection test earned its keep. It found 5,115 reversed triangles (0.15%) that a plain fan
would have made, and 155 of them were visible. Two causes:
- Some compilers merge surfaces into non-convex faces, such as an L shape in `revnem.bsp`. Faces now use
  ear clipping, which keeps every corner so no cracks open up.
- A few outlines run out to a point and straight back (`q2rounda`, `rene16`, `rene24`). Those spikes have
  no area, so they're dropped.

Final result over all 363 maps: 3,641,582 triangles, none visibly reversed, 65 hairlines under one unit
tall. Largest light atlas 2048.

Brightness: plain x2 was faithful but murky, because players of that era also raised their gamma. It's
2.6 now, set by eye on `q2duel5`, `ringbase`, `rcdm8` and `qkeep`. `MapMesher.UnlitValue` has to stay in
step with the shaders.

Frame rate: the first launch after a shader change showed 13-15 fps, which fits shader compilation.
Reruns of the same maps showed 99-132 fps. `ringbase`, one of the biggest maps, loads in about 500 ms.

Not checked: the fly controls by hand, since only screenshots were taken. Sky is a flat colour. Water
doesn't move and light styles don't flicker.

## 2026-11-06: milestone 2, movement

Core now has box collision against map brushes, a fixed 60 tick player move, and a small `Simulation`
that takes one command per player per tick. The game has a player, a smoothed camera, a tuning panel
(Tab) that saves to `user://tuning.json`, a fly mode (V) and a stand-in recoil gun on the left mouse.

Written from the plan's rules: air steering only rotates velocity, so strafe-jumping can't gain speed. A
hop's touch of the ground has no friction and charges speed for turning. Corner slide probes along the
surface that was hit, not across the direction of travel, which is why flat walls never trigger it.

Whole-collection tests found things no hand-written test would have:
- 16 mismatches between the tree and brute force, all in `rene34`. It has six zero-thickness sheets 16,000
  units wide. The tree lists them in only a few leaves, where they'd be invisible floors, so brushes with
  no volume are now dropped at load.
- Traces starting inside solid can disagree with brute force, because compilers leave brushes buried
  inside other brushes out of the tree. Players never start there, so that's allowed.
- 108 players ended a tick inside walls, in 527,549 ticks. Springing back from a squeeze could leave the
  body closer than the 1/32 surface gap, and from there a slow creep gave a fraction below -1, which the
  clipper's -1 start value read as "never entered". The start value is now negative infinity, and spring
  back has to leave the gap.
- A few spawn points are buried, or sit in pockets exactly 32 wide. Getting unstuck now tries a squeeze
  first, then any direction up to 64 units.

Final result: 115 tests pass. Tree against brute force is 53,100 traces on 354 maps with 0 mismatches, and
33,588 of those hit something. Random charging round 1,381 spawns for 552,400 ticks ended in solid 0 times.
Every spawn reached the ground. A player tick costs about 6 microseconds.

Found mid-session: `content/q2textures-master` is the high-res pack, with paths matching what maps ask
for. My earlier 239 count missed it because it had landed in a new folder.

Corrected a claim made while planning: crouch-jumping here doesn't lift the legs, as told to Matthew when
asking. It only shortens the body in mid-air.

Not checked: nothing has been played by hand. Input wiring is only proven by a headless smoke test that
drives the simulation directly, so keyboard, mouse, panel sliders and fly mode need a real try. All the
tuning defaults are first guesses. Doors and lifts aren't solid, so some maps can't be left from where
you spawn without V to fly out.
