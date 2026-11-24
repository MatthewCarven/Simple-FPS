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
tuning defaults are first guesses. Doors and lifts aren't solid, so you walk straight through doors,
and anywhere only a lift reaches needs V to fly there.

## 2026-11-08: launcher

`start.cmd` is now the way in: double-click it and you land on `ringbase`, which replaced `q2duel5` as the
default map. If the game crashes, the window stays open so the error can be read. Checked with `--smoke-test`
launched the way Explorer does it, and with `--map=` still working. A real mouse double-click hasn't been tried.

## 2026-11-08: server movement settings

Matthew asked about making the tuning panel a server setting, announced publicly with a picture when it
changes. Agreed: the host alone changes it, the change applies at the next round, the game draws the
picture from a jump run with the old and new numbers, and the server list marks changed servers. It's in
PLAN.md and under milestone 7 in TODO.md. Nothing built yet.

## 2026-11-24: first play by hand

Matthew played `ringbase` by hand on 8, 17 and 24 November and says the movement feels good with every
default as it was. So `MoveSettings` is unchanged, and his `tuning.json` still holds the defaults. The
panel was opened and closed on the 17th with nothing moved.

One fix: the mouse is now grabbed when the game starts, where before looking around needed a click first.
The click that grabs it back after Esc no longer fires the test gun; firing waits for that button to come
up. Screenshot and smoke-test runs leave the mouse alone. It builds with no warnings, passes the headless
smoke test, and the 24 November run was on that build. 115 tests pass.

He did try clipping corners. It's far more forgiving than the Quake 2 he remembers, which he likes and
which was the point, so corner slide stays at reach 8 and speed 300. He tried the recoil escape as well,
hopping backwards while firing, and called it excellent. The test gun stays at a kick of 40 every 6 ticks,
capped at 700.

Still unverified: the log only records map loads, so it can't show which other features he tried. Nobody
has confirmed these by hand yet: squeeze, ladders, water, crouch, R respawn, V fly, and a slider change taking effect live. That's a TODO item for
him. Mouse speed is fixed at 0.1 degrees a pixel with no setting, and he didn't raise it.

## 2026-11-24: swimming can't build speed any more

Matthew swam in the lava under `ringbase` and called it pretty accurate. Lava, slime and water all swim the
same way, so that covers swimming. He couldn't find the map's water. The deep tank is about 360 units ahead
and to the right of spawn 7, one level down.

He also found that holding jump and forward at the surface, looking a little up, built speed: 260 for him. A
test pool showed it going further, about 300 looking level and 510 looking 45 degrees up. That's past a run,
which breaks the rule that only blasts, recoil and jump pads take you past one. The cause was bobbing: each dip
back into the liquid arrived moving down, which left the swim push room to add more. He chose to remove it.

Swimming now can't build flat speed past a normal swim (150). Speed brought in from outside, such as a blast,
is kept, and the liquid slows it. The bobbing test covers water, lava and slime at four angles, and it reached
301 before the fix. Climbing out onto a ledge still works. 120 tests pass, the build has no warnings, and the
headless smoke test passes. The change hasn't been played by hand yet.

On `rexro` he squeezed between wooden walls and sent a picture showing the full 4 units of squeeze holding
while he stood in the gap. So squeeze works by hand, at amount 4 and speed 60. Ladders work too: he climbed the one behind spawn 2.

Back on `ringbase` he sent pictures standing, crouched and flying above the map, and reported respawn working
and gravity set to 200 taking effect live. So crouch, V fly, R respawn and the panel's sliders all work by
hand. Everything in milestone 2 has now been tried by hand except the swim cap.

His `tuning.json` was left with gravity at 200 after that test, so a launch from it floats until he presses
"Reset all to defaults" on the panel, which he'll do himself.

## 2026-11-24: milestone 3 planned

Checked what the tuning session left: defaults unchanged, the mouse grab and swim cap look right, and 120
tests pass. Asked Matthew about milestone 3. Agreed: it covers maps and textures only. Sounds wait for guns
(milestone 5) and models for bots (milestone 6), so each is built against something that uses it. Original
textures stay smooth, a switch flips between high-res and the originals, and a map list at start replaces
typing `--map=`. Split into three sessions in TODO.md: 3a the map bake, then 3b textures and 3c the map list.

## 2026-11-24: milestone 3a, map bake

Maps now load from our own baked file. The first time a map is played it's baked to
`cache/maps/<name>.bake`, and after that the bake is loaded instead of the `.bsp`. A bake holds the
meshes, the light atlas, the collision tree and brushes, the entities, and the title and author. It's
keyed on a SHA-256 of the map's files and a bake version (`BakeFile.Version`, now 1), so changing either
bakes it again. The file is a list of tagged sections, and a reader skips any it doesn't know, so bot paths
(milestone 6) or sky surfaces (3b) are a new section plus a version bump. `index.json` beside the bakes
lists each baked map's name, hash, source `.bsp`, title, author, and which map it's a copy of.

Override files turned out to be two kinds. Each is a set of flags, a 64-byte map path, and the entity text
with its length in front (all 25 have flags 5). 13 replace the entities of a map that's there. The other 12
have no `.bsp` of their own and name another map's, so `q2dm1-nade` plays `q2dm1.bsp` with its own items.
Those now load by their own name. Eleven of the 12 are edits of `q2dm1` to `q2dm7`, and the twelfth is
`q2rdm2_jaquio`.

Copies: when maps are baked from exactly the same files, the name that sorts first is kept and the others
point to it. That finds 8: the six renamed `.bsp` pairs (`q2rdm2` = `q2pduel7`, `q2pduel1` = `q2duel1`,
`q2pduel10` = `q2duel8`, `q2e1m1a` = `q2e1m1`, `q2ht1` = `q2ht`, `roguesedge` = `rdm14`), and two override
variants that match another byte for byte (`q2dm1-arenaquad` = `q2dm1`, `q2dm1x` = `q2dm1-roofquad`). Which
name to keep is my guess, and it's one line to change.

Titles and authors come from the worldspawn `message`, because no map has an author key. The title is the
first line and the author is whatever follows the first "by", after turning the game's coloured letters and
decorations back into plain text. 330 of the 375 maps get a title and 174 an author.

Load times on `ringbase`, headless, three runs each. The first load bakes in 115 to 121 ms and takes 270 to
280 ms in all. Later loads read the bake in 22 to 23 ms and take 180 to 190 ms in all. The rest is textures
and Godot's meshes, and 3b's texture bake should cut the textures part. The `ringbase` bake is 5.3 MB, and
the whole collection would be 632 MB against 389 MB of `.bsp` files. The largest is `risc2` at 7.9 MB.
Baking all 375 takes about 3 s.

Checked:
- 162 tests pass: the 120 from before unchanged, and 42 new. The build has no warnings.
- Every map in the collection bakes, reads back, and matches what's built straight from its files to the
  bit: meshes, light atlas, collision and entities. 15,000 traces against both agree exactly.
- Movement is unchanged. The previous commit, built on its own, and this one end the smoke test's 240 ticks
  of hopping at exactly the same position and speed from spawns 0, 3 and 6 on `ringbase`.
- A changed `.bsp` or override bakes again, and so does a damaged or half-written bake. The index rebuilds
  itself from the bakes if it's missing, damaged, or misses one.
- `q2dm1-nade` loads by name, and the Quake 1 map still says it isn't a Quake 2 map.

Not checked: nothing was played by hand or run in a window, so the times above are headless, where Godot's
side of loading costs less. Collision still covers only the world. The doors' and lifts' brushes are in
the bake, ready for milestone 4.
