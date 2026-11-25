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

## 2026-11-25: milestone 4a, sounds

Built in its own worktree beside 4b. Sounds now bake to our own format and play in the world, starting with the
maps' ambient sound spots.

Asked Matthew one round, and he took both recommendations:
- Loops fade as in the original: every looping sound is at full volume within 80 units and silent by about 413
  (the original's "static" fade), whatever the map's attenuation and volume keys say. The keys only shape one-off
  sounds. 3,758 loops start on, and 2,217 of them set no attenuation, so following the keys would have made busy
  maps far louder than their authors ever heard them.
- Sounds are cut off as in the original: a loop is heard only within 400 units, and only when its spot is in the
  map's visibility data from around your eye (the PVS). A one-off is heard only where its spot can be heard (the
  PHS), unless it's heard everywhere. Maps now bake their visibility data for this (`BakeFile.Version` 3), so
  every map bakes again once.

The sound folder holds 2,877 `.wav` files. 2,860 are 8- or 16-bit mono PCM. 3 are 16-bit stereo, 1 is 24-bit
stereo, 1 is 32-bit float and 1 is IMA ADPCM. 11 aren't WAV files at all (ten are config text), and 4 hold no
samples. Rates run from 5,500 to 88,200 a second, and 196 files have a cue point.

How it works. Our own reader (`WavReader`, in Core) reads 8-, 16-, 24- and 32-bit PCM and 32-bit float, mixes
any channels down to mono, and keeps the loop points the original used: the first cue point, and the length in a
"mark" label after it, which can end the sound early. It refuses compressed formats and broken files with the
reason. Each sound bakes to `cache/sounds/<path>.snd` as 16-bit mono at its own rate, keyed on a SHA-256 of the
`.wav` and `SoundFile.Version`, in the same tagged sections as maps and textures. A map's name for a sound becomes
a file as in the original: `.wav` is added if missing, and case and leading slashes are forgiven, as Windows did.

The simulation says what to play through `Simulation.Sounds` (`SoundEvents`, in a file of its own). One-off
`SoundEvent`s wait until the game takes them, and loops are state with an on switch and a position. A one-off
from the same emitter cuts off the one before, for doors' start and stop sounds. `TargetSpeakers.Start` reads a
map's speakers and adds their loops, on if they start on (flag 1). Loops that start off and speakers that play
once wait for 4b, which only has to call `TargetSpeakers.Use` (written and tested, not called yet). The 11
speakers marked not-in-deathmatch (flag 2048) are left out, as in the original.

In the game, `WorldSounds` turns bakes into Godot streams and places a player for each loop you can hear. Which
sounds you hear and how loud is worked out in Core (`Hearing`, `LoopMix`, `SoundFalloff`), so Godot only places
and pans them. Copies of one loop add up but stop at full volume, since the original mixed them into one channel.
The original capped each ear separately, and capping the total is close to that. A loop that starts playing picks
up where its sound has got to by the clock, so copies stay in step. The overall volume is 0.7, the original's
default.

Content findings:
- The 375 maps name 244 different sounds: 4,111 speakers, 11 trains and one worldspawn (`risc1`). 228 bake. The
  other 16 are all missing files, such as `world/pump.wav` (`q2rdm8`), three in `q2frag1`, and Quake 3 style
  names like `sound/world/fire1.wav` in `q2_q3dm1`.
- All 228 are 8- or 16-bit mono PCM, and 34 have a cue point. Their bakes take 41 MB.
- 18 speakers in the `.bsp` files sit inside walls. The original never played them, and neither do we.
- 355 of the 363 maps have visibility data. `risc2` has the most clusters, 4,169. It brings the whole
  collection's bakes to 692 MB, up from 639.
- `misc/mm.wav` is cut short, and plays what's there.

Checked:
- 303 tests pass: 247 from before and 56 new. The build has no warnings.
- Every sound the maps name bakes, reads back from disk unchanged and matches decoding its `.wav` directly, or is
  refused with its reason. Every `.wav` in the sound folder reads (2,861) or is refused with a reason (16).
- Our reader against Python's own `wave` module on every file: 2,857 identical. The 3 that differ run 6 samples
  longer in ours, because their RIFF headers say the file ends 12 bytes before it really does. We read to the
  real end, as the original did. Python can't read float, and refuses everything we refuse.
- Every map's visibility data unpacks, covers all its leaves' clusters, and bakes and reads back byte for byte.
- Headless smoke tests pass on `ringbase`, on `redpen` (68 loops on, 2 heard from the first spawn, and a test
  sound played through the events), and through the map list.
- Matthew listened on `redpen` in a window and said it sounds right. He also went on to `q2dm8` from the list,
  and the log shows no errors.

Not checked: no one-off sound has been heard, because nothing plays one yet. They're tested in Core and run
headless only, and a sound heard everywhere (attenuation -1), which plays unplaced, hasn't run at all. Movement
wasn't compared with the previous commit: the simulation only gained the sounds property, and the bake tests
still match collision to the bit. Closed doors don't cut sounds off yet, which waits for doors in 4c.

Fixed after review from the planning session, in two goes: quitting with sounds playing made Godot warn "6 ObjectDB
instances were leaked at exit" on `redpen`. `--verbose` named them: a WAV stream and its playback for each sound
playing. Godot's 4.7.1 source explains why. A stopped playback stays in the audio server's live list until the
audio thread next mixes, about every 93 ms headless (4,096 samples at a time), and any still in that list at exit
leak. A playback the audio thread has dropped is freed by the main thread a frame later, or with the audio server
at exit. My first fix waited 0.1 s before quitting, which is right on the edge: the planning session saw the
warning in 3 of 4 runs while the machine was busy. Now every way out goes through `Main.QuitGame`, which stops each
player, clears its stream, and waits until two mixes have started since the stop (the time since the last mix
only goes down when a new one starts). That means one whole mix has run, then it waits one more frame. A 3 s timer
is only a backstop, for a machine with no audio running. The window's close button goes through it too, so
Godot's own quit on close is off. Checked: 10 runs in a row on `redpen` and 12 more with four running at once,
no warning in any, each quit waiting 111 to 187 ms. Also clean on `ringbase` and the map-list route, and a bad
map still exits with 1. Not checked: the close button, which needs a window. `--quit-after N` still warns,
since it quits from inside Godot before any of our code runs; only tests quit that way. While checking, the
smoke test's hop came out differently from earlier in the day: `tuning.json` was saved at 16:33 during
Matthew's listen, with `Accelerate` 13 where the default is 10.
