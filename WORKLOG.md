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

## 2026-11-24: 3a checked

Checked 3a from the planning session: 162 tests pass, the build has no warnings, and a headless smoke test
on `q2duel5` (not baked before) baked it and passed. The smoke test ends at exactly the same position and
speed on the commit before 3a, run in a throwaway worktree, so the bake didn't change movement on that map
either. It does differ from the 6 November run, because the swim cap changed it in between.

3b and 3c will run one after the other, not side by side as planned, since they'd have to share this
folder. 3c goes first: it's smaller, and a map list makes every later test easier to launch.

## 2026-11-24: milestone 3c, map list

Built in its own worktree, side by side with 3b. The game now opens on a map list unless `--map=` is
given. Asked Matthew first, in two rounds, and he took every recommendation:
- Titles: a quick read rather than baking all 375. The list reads each map's header and entity text,
  where the title and author are, and hashes a map's files only when another map's are the same size,
  to find copies (16 maps). It takes about 0.2 s for all 375 once the files are in the disk cache (150
  to 230 ms over several runs), and about 0.5 s the first time after a build. Nothing is written.
- Copies: the shortest name is kept, then the one that sorts first. That keeps `q2rdm2` over
  `q2pduel7` and `q2dm1x` over `q2dm1-roofquad`. The other six are as before. The bake's index uses the
  same rule now (`MapCopies`).
- The 9 maps without deathmatch spawns are hidden: `q2_ctfb1` (a CTF map), `q2dm1-1`, `quad`,
  `quadshot`, `recon`, `remains`, `risc1`, `risc2` and `robbie1`. `--map=` still loads them.
- The original levels and their edits are listed like any other map. Keeping them out of tuning is
  about what movement is tuned on, not what can be played.
- Look: one list sorted by title, with the author and file name beside it, and a search box that keeps
  the keyboard. Typing filters by title, author or file name, and every word has to match. Up, Down,
  Page Up and Page Down move, Enter or a double-click plays, Esc clears the search. A map with no title
  shows its file name. The sort skips punctuation in front of a title, so `'The Edge' - Modified` files
  under T. That was my call after the first picture put three such titles at the top.
- Esc in a map opens a menu: Resume, Map list, Quit. It replaces Esc just freeing the mouse. Esc again
  resumes, and Esc with the tuning panel open closes the panel. The world keeps running behind the
  menu, as it does behind the tuning panel. Only your input stops.

358 maps are listed: 375, less 8 copies and 9 without spawns. The list is read afresh each time it
opens, so a map dropped in while playing shows up on the way back, and coming back from a map
highlights it. A map that won't load, picked from the list or named by a bad `--map=`, brings the list
up with the error under it.

`--smoke-test` and `--shot` still skip the list and play `ringbase` unless `--map` names another map.
Two flags were added for checking without hands. `--list` opens the list anyway: the smoke test then
picks the map from the list, goes back through the Esc menu, and picks it again before hopping, and
`--shot` takes a picture of the list. `--menu` opens the Esc menu in a `--shot` picture. The smoke test
now hops in a simulation of its own, built from the loaded map the same way as the player's, so frames
that pass while the list is in use can't move its start.

Checked:
- 182 tests pass: the 162 from before, one of them changed for the new copy rule, and 20 new. The build
  has no warnings. A whole-collection test checks the quick read agrees with the bake on every map:
  name, `.bsp`, title, author and copy.
- The headless smoke test on `ringbase` from spawns 0, 3 and 6 ends at exactly the same position and
  speed as on the previous commit, built in a throwaway worktree. Through the list it ends at the same
  spot both times, before and after going back through the Esc menu.
- Headless: a plain launch opens the list with 358 maps. A bad `--map=` opens the list with the error,
  and the smoke test still fails with exit 1 on it. `q2dm1-nade` plays through the list, and a hidden
  copy (`q2pduel7`) can't be found in it.
- Two pictures in a window, which Matthew OK'd: the list, and the Esc menu over `ringbase`. Both looked
  right. The punctuation sort came after the pictures and is checked by a test only.

Not checked: nothing was played by hand. Typing in the search box, the arrow keys, Enter, double-click
and the menu's buttons have only been run by script or headless, and grabbing the mouse after Resume
hasn't been tried. The read times were measured with the maps already in the disk cache, so the first
start after a reboot will be slower.

## 2026-11-24: milestone 3b, textures and skies

Built in its own worktree beside 3c. Textures and skies now load from our own baked files, the high-res
pack swaps in where it fits, T flips between high-res and the originals, and skies are drawn.

Asked Matthew in two rounds, and he took every recommendation:
- Skies: each map's own sky box from `content/env`, drawn at infinity behind its sky surfaces, which still
  hide what's behind them, as in the original. A map naming no sky gets Quake 2's default, `unit1_`. The
  46 maps that set a slow spin (`skyrotate` about `skyaxis`) keep it.
- The key is T. The choice is saved in `user://options.json`, a new per-player options file that invert Y
  and mouse speed can join later. High-res is the default.
- 16 high-res pictures don't have their `.wal`'s shape (384 x 128 for a 256 x 256 wall, for instance).
  They're left out, so those names keep the original.
- Second round, after the first pictures: the pack's skies aren't remakes. Its `unit1_` to `unit9_` are
  one mountain range in two tints, where Quake 2's are red skies with towers and statues, and `unit1_` is
  the default, so about 190 maps would have changed look. Matthew chose to use a pack sky only when it
  looks like the original. Each side shrunk to 16 x 16 differs by 1 in 255 for `night1` and by 91 to 101
  for the unit skies, so the limit is 32. Today only `night1` swaps in. (I told him the pack had 10
  skies. It has 9.)

How it works. Each texture name bakes to its own file, `cache/textures/original/<name>.tex` or
`cache/textures/highres/<name>.tex`, already decoded and mipmapped, so maps that share textures share
bakes. Skies bake to `cache/skies/<set>/<name>.sky` as six cube map faces. Like maps, each file holds a
SHA-256 of its source files and `TextureFile.Version`, and bakes again if either changes; the sources are
read on every load, but only to hash them. A high-res bake keeps the `.wal`'s size, which is what the
map's texture coordinates are measured in. A rejected pack sky bakes as an empty file, so the comparison
isn't made again. The bake file uses the same tagged sections as map bakes, now shared in `BakeSections`.
The PNG, Targa and PCX readers are our own, in Core, so they're tested without Godot. Sky faces are now
meshed, so `BakeFile.Version` is 2 and every map bakes again (the whole collection grows from 632 to
639 MB).

The texture index used to list all 31,082 `.wal` files on every map load, about 100 ms. It now checks
the exact path first and only lists the folder when a bare name is needed (33 maps).

Sky orientation. Quake 2's six pictures aren't named from one viewpoint: looking east you see `rt`,
looking north `bk`. The table was checked on all of `content/env` before writing any code: with it,
neighbouring sides join with no more change than between neighbouring pixels (median 3.0 against 3.2),
and with two sides swapped the change is five times bigger. In a window, the view north on `rota4c`
matches `unit1_bk.tga` the right way round.

Load times on `ringbase`, headless, the base commit and this branch alternating over 6 rounds:
- Base: 206 to 229 ms in all. (Earlier the same evening it measured 183 to 190 ms; the machine's speed
  moves.)
- Originals: 121 to 134 ms. High-res: 158 to 183 ms, with 75 of its 86 textures from the pack.
- In a fast round, textures and meshes took 82 ms with originals: 26 ms reading bakes and hashing their
  sources, 6 ms handing pictures to Godot, and the rest building meshes, which 3b didn't touch. High-res:
  127 ms, of which 49 and 18.
- The first load bakes. `ringbase`'s high-res set took about 0.5 s, and the first flip to the originals
  85 ms. Its bakes come to 31 MB for both sets.

Content findings:
- The maps' drawn faces use 4,188 texture names. 4,011 have a readable `.wal`, 1,331 come from the pack,
  and 177 are the checker stand-in, 2 of those because their `.wal` header is bad (`textures/blue1.wal`,
  `textures/e2u3/green0_1.wal`), as before.
- Baking every texture would take 239 MB for the originals and 903 MB for high-res.
- 375 maps name 46 skies. 7 have no pictures and show the flat colour: `bonechill512_` (`q2_q3dm1` and
  its two betas), `pbsky3` (`q2dm1_mcp`), `q3dm7`, `nuke2` (`railpit`) and `unit1` (`return_dm1`).
- Damaged sky files: the five `env/london/*.tga` are cut short and `env/mountainlakes*.tga` lost bytes,
  probably to a text-mode copy (the 2002 copies in `env/death_jump` are intact). Their `.pcx` copies are
  used instead.

Checked:
- 247 tests pass after merging 3c: its 182 and 65 new. The build has no warnings.
- Every texture the maps use bakes, reads back from disk unchanged, and the originals match the old
  loader's pixels exactly. Every high-res bake keeps its `.wal`'s size and shape.
- Every sky in `env` and the pack (251 names, 246 boxes) bakes and reads back. 3 don't join at their
  edges, all because of their pictures: `teehee_` is a joke collage ("I'm too lazy to make a bottom"),
  `hell1`'s bottom doesn't match its walls, and `city_`'s ground is busy. The test allows 5%; a layout
  mistake would break nearly all of them.
- Our readers against Pillow, on every image the game might read: all 1,882 PNGs identical; 1,459 Targas
  identical and 10 refused by both; 1,373 PCXs identical, 66 refused by us because they have no palette
  (Pillow guesses greyscale; their `.tga` copies are used), and 1 refused by Pillow only. Two fixes came
  out of it: Targa alpha only when the header says there is some, and a packet running past the end of
  the picture counts as damage.
- Movement is unchanged: the base commit and this branch end the smoke test's 240 ticks of hopping on
  `ringbase` (spawn 0) at exactly the same speed and position.
- The smoke test now flips the textures both ways, headless, and both smoke tests pass after the merge,
  including 3c's route through the map list and back.
- Pictures in a window, after telling Matthew: high-res walls on `q2duel5`, its sky in both sets, and the
  skies on `rota4c` and `railasnail`. All looked right: upright, unmirrored, joined, in full colour.

Not checked: nothing was played by hand. The T key has only been run through the smoke test's flips,
never pressed, and the spin hasn't been watched. High-res was judged from a few pictures. `--pitch=` was
added for those pictures, and `--textures=original|highres` sets one run's textures without saving.
The whole-collection texture test needed the .NET heap capped (`DOTNET_GCHeapHardLimit`) once, while the
laptop was down to 0.4 GB of free memory with another project's game running; with 1.3 GB free the
whole suite passed without it.

## 2026-11-25: milestone 4 planned

Counted the map objects in all 363 maps (table in TODO.md). Lifts, doors and teleporters lead, each in
well over a third of them. Asked Matthew, and he took every recommendation: teleporters keep your speed,
turned to face the exit; jumping off a moving lift or train keeps its speed; teleporters get our own
glowing pad, since the original's model isn't in the content and isn't ours to ship; and sounds move from
milestone 5 into milestone 4. Split into 4a sounds and 4b triggers and teleporters, side by side in
worktrees, then 4c moving things, which needs both. Things that need damage wait for milestone 5.

## 2026-11-25: milestone 4b, triggers, teleporters and push zones

Built in its own worktree beside 4a. Maps now have live objects, spawned from the bake's entities and run
inside the simulation at 60 ticks (`SimpleFps.Core/Objects`), so online play later needs no rewrite.
Written from how the entities are documented to behave, not from the original game's code.

What's in:
- Names and targets: firing sets off everything its `target` names, after its `delay`, takes out what its
  `killtarget` names, and shows its `message` to the player who set it off. A chain 64 deep is a loop in the
  map and is cut off. Entities flagged as not in deathmatch are left out (677 across the collection).
- `trigger_multiple` (with `wait`, 0.2 s if unset), `trigger_once`, `trigger_relay`, `trigger_counter`
  ("2 more to go..."), `trigger_always` and `func_timer` (with `random`, `pausetime` and switching on and off).
  The documented flags work: starts off until used, players can't set it off, and a trigger with an angle
  only fires for someone facing that way. Random spreads come from a fixed seed, so a server and its clients
  will agree.
- Teleporters (`misc_teleporter`, and the one `trigger_teleport`) go to the first entity their target names,
  of any class, as the original did, which is how 30 teleporters aimed at `info_notnull`, `path_corner` and
  the like still work. As Matthew chose: your flat speed comes out pointed the way the exit faces, you face it
  too with the view levelled, and up and down speed is kept. Most exits leave your feet exactly on the floor,
  so arrival lifts you by the smallest step that's clear (1/32 of a unit for 70 of them, up to 24 for a few).
- Push zones set your velocity on every tick you're in them, at the map's direction (an angle of -1 is up,
  -2 down) and its speed times ten, as the original read it. The four with no speed, which the original took
  as 10,000, are held to 4,000, the fastest any map asks for by number. My call, not Matthew's. The push-once
  flag works.
- Hurt zones are found and kept, with their damage and on/off flags, ready for milestone 5. They don't hurt.
- Touching uses each brush model's box, with both boxes grown by a unit, as the original did. The old pad's
  touch box is small, so you have to step near a pad's middle.
- Classes not built yet are listed once per map in the log, with counts. Items lead, then `target_speaker`
  (4a) and `func_plat` (4c).
- The game draws our own pads, as Matthew chose: a glowing disc with a small light that casts no shadow at
  each way in, and a plain dim disc at each way out. For the light to show at all, the world shaders now take
  small run-time lights on top of the baked light, which milestone 5's moving lights need anyway. Map messages
  show in the middle of the screen for three seconds, which Matthew chose too. Each teleport and push is logged
  with speeds.

Checked:
- 296 tests pass: the 247 from before and 49 new, over small worlds, plus a whole-collection check. The build
  has no warnings.
- The collection check spawns all 375 maps without errors, touches every trigger and runs ten seconds of
  timers. Every teleporter with a target finds its exit, and a body fits somewhere touching each one (one on
  `rez1` only crouched, in a 52-unit alcove). Five teleporters have no target and go nowhere (`q2lt1`, and
  four on `ra2map20`, whose arena mod placed players itself). Two exits on single-player `q2e3m1` stay blocked
  after lifting and are left to movement's unsticking.
- Movement is unchanged away from objects: the smoke test on `ringbase` ends at exactly the same position and
  speed from spawns 0 and 3 with the map's teleporter live in its simulation, and a test runs 600 ticks of
  hopping and firing beside live objects that never touch the player, matching a plain simulation exactly.
- The world under the new shaders matches the old unshaded look to the pixel in windowed screenshots of
  `q2duel5` and `rexro`, and settled fps is the same (144 and 145, old and new shader runs alternated).
- Matthew played `q2pduel1` and said both work well. The log shows 8 teleports, each coming out at the speed
  it went in, facing its exit; a sideways push at 400; and the jump pad's 900 up, then 300 onto the upper level.
  He found you can get back down past the jump pad if you time a fall near its edge. Simulating 64,000 drops,
  some steered, into that shaft found no way down through a push zone itself: the zones fill only part of the
  shaft, and the open part beside them leads down. The original used the same rule, so it's the map as built.

Not checked: the pad's light strength and the exit disc's dimness weren't commented on. Telefrags, damage in
hurt zones, and the teleporter's and push zone's sounds wait for later (TODO.md). Doors, lifts and anything
else that moves or blocks is 4c's. `q2dm1_mcp` aims two push zones at targets, a feature of a later game; here
they push along their angle as the original did.

## 2026-11-25: 4b, push zones reworked by hand

After the first hand test Matthew asked for jump pads that don't catch you unless you're on the pad, and we
worked it out over five rounds on `q2pduel1` and `q2ztn3dm1`, each one simulated before he played it. Where
it ended (`PushZone.Acts` and `KeepingSpeed`):
- A push zone takes hold of you only when you're on its own floor, or moving up through it, and keeps hold
  while you stay in it. Its floor is the highest ground inside its outline, found as the map loads, from 32
  above its top (some pads are buried in the floor slab you stand on) to 256 below its bottom (some float).
  So you can fall through a pad, land on its floor and be thrown back up, and a ledge at a pad's edge doesn't
  throw or push you just because your head is in the zone. On `q2pduel1` you can now walk off the upper
  ledge down the jump pad's shaft, which the original's zones guard twice.
- Every zone judges you as you were when the tick's touching began, so zones touched together don't depend
  on which is checked first. The first walk-in build got this wrong: from `q2pduel1`'s corridor you touch the
  column, the strip beside it and the carry zone at once, and the carry zone let go as you started to fall.
- A jump pad keeps your speed while you hold any movement key (Matthew): it sets your climb, and across you
  go at your own speed or the pad's sideways speed, whichever is faster, heading the way the two add up. Let
  go and it throws you as the map says. On `q2ztn3dm1` that keeps a 300 run through a pad that used to cut
  it to its own 88.
- "Push zones as in the original: catch you anywhere" on the tuning panel (`MoveSettings.OriginalPushZones`,
  off) brings back the original's zones, which act on anyone touching them and set your speed outright.
- The push log line says whether you were standing or crouched, and on the ground or in the air.

Checked:
- 319 tests pass, and the build has no warnings. A test on `q2pduel1` itself walks and crouches off the ledge,
  launches from the pad, walks in from the corridor, and checks the original rule turns you back at the edge.
- Across the collection, every spot you can stand on inside a jump pad still throws you (2,593 of 2,617),
  except 24 that are ledges at the edge of a pad's shaft (`q2pduel1`, and `raingod`'s four pits), which is the
  point. All 179 standing spots in sideways and downward zones still push you. 116 of the 132 jump pads have
  their floor found; the rest count anyone with their feet in or just under them.
- The smoke test on `ringbase` still ends at exactly the same position and speed from spawns 0 and 3.
- By hand: Matthew crouched off `q2pduel1`'s ledge down the shaft (the log shows the pad throwing him from its
  floor, crouched), walked into the pad from the corridor and said it works now, and ran onto `q2ztn3dm1`'s
  pads holding forward and said his speed carries through (the log shows 300 across, 715 up).

Not checked: walking off `q2pduel1`'s ledge standing up, by hand. Sideways wind and carry zones on other maps
no longer push you while you fall through them, which no one has tried; `q2e3m5`'s chains of pushes are the
likeliest to notice. Matthew also looked at `q2dm2` (an original level, kept out of tuning) from the main
folder, which runs `master` without 4b: its lifts are `func_plat` and `func_train`, which 4c builds.
