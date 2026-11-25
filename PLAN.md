# Simple FPS: plan

Working title. An original deathmatch shooter in Godot, with classic quick movement
and our own twists. Agreed with Matthew on 2026-11-06.

The idea in one line: you hop around like you're on ice, you squeeze round corners
instead of snagging on them, and every shot and blast shoves you about.

## What we agreed

| Topic | Decision |
|---|---|
| Engine | Godot 4.7.1 .NET, the copy in `MinerCraft/Godot_v4.7.1-stable_mono_win64`. C#. |
| Content | Drop a file in, and it's baked to our own format the first time it's used. The game only ever runs our format. Same route for maps, textures, sounds and models. |
| Opponents | Bots first, online later. Built as server and client from day one so online needs no rewrite. |
| Wall grace | Both kinds, each with its own setting: corner slide and body squeeze. |
| Movement | Bunny-hopping and blast jumping. No strafe-jumping. No crouch-jumping (see open items). |
| Air control | Almost none, like ice. To change direction you touch down. |
| Hop landing | Turning on landing costs speed. Gentle curves stay fast, hard corners slow you down. |
| Pushes | Every explosion shoves you. Gunfire pushes both shooter and target a little, so your own gun steers you mid-hop. |
| Map objects | Agreed 2026-11-25. Teleporters keep your speed, turned to face the exit, where the original stopped you dead. Jumping off a moving lift or train keeps its speed, so lift launches and train surfing are tricks. Teleporters get our own glowing pad, since the original's model isn't ours. Sounds come in with map objects instead of waiting for guns. |
| Sounds | Agreed 2026-11-25 (4a). Looping sounds fade as in the original, full volume within 80 units and silent by about 413, whatever the map's keys say, since that's what the maps' authors heard. One-off sounds follow the map's attenuation and volume. Both are cut off as in the original: loops by the map's visibility data from around your eye and a 400-unit limit, one-offs by what can be heard from their spot. |
| Server movement | Agreed 2026-11-08. Each server owns its movement settings, and only its host (or admins they name) can change them. A change is announced to everyone at once and takes effect at the next round. The announcement carries a picture the game draws itself: a jump run with the old numbers and the new, both arcs drawn, plus the old and new values. Servers with changed movement get a badge and a one-line summary, like "low gravity, fast run", in the server list. |
| Lighting | Cheap. Each map's own baked light, plus small moving lights on energy rounds, muzzle flashes and explosions. |
| Textures | Maps keep their look. High-res swapped in where names match, otherwise the original or a stand-in. A per-map file can override any of it. A switch flips between high-res and the originals (2026-11-24). Original textures stay smooth, as now, not pixelated. Skies are each map's own, and a high-res sky only swaps in if it's the original redrawn (2026-11-24). |
| Later mode | Weapons of Destruction style: huge arsenal, guided weapons, big physical blasts, gadgets and traps. Our own weapon designs. |
| Audience | Just Matthew for now, public one day. Everything in the folder today is build placeholder, replaced before shipping. |

## What's in the content folder (2026-11-06)

- **364 maps: 363 Quake 2 format, 1 Quake 1** (`rene40q1.bsp`). So one loader, not several,
  and the Quake 1 map is skipped.
- 354 have deathmatch spawns. 9 don't (mostly single-player or test maps), and those get skipped
  for deathmatch.
- 351 have baked light. 12 don't, and they need a flat light fallback.
- Maps are small by today's standards: median 625 brushes and 3,094 faces, largest 4,438 and
  18,477. Cheap to run.
- 319 have a title in the file, and 152 name an author, so the bake can build most of the credits
  list for us.
- Some are renamed copies (`q2pduel7` = `q2rdm2`, `q2duel1` = `q2pduel1`). The bake detects these
  by hash.
- 25 maps have an `.override` file holding a replacement entity list. The bake should honour these.
  (Found at 3a: 13 replace a present map's entities. The other 12 name another map's `.bsp` and have
  none of their own, so `q2dm1-nade` plays `q2dm1.bsp` with its own items, under its own name.)
- Player models are MD2. Sounds are WAV: 2,877 files, 2,860 of them 8- or 16-bit mono PCM (found at 4a).
  The maps name 244 of them, and 16 of those are missing.
- Textures are WAL. Of the 4,527 names the maps use, 4,174 are at their exact path and 4,318 turn up
  by bare name. 330 of the 363 maps have every texture at its exact path.
- The high-res pack is `content/q2textures-master/pak99_textures/textures`, laid out with the same
  paths the maps ask for. It covers 1,469 of the 4,527 names, including every texture in `q2duel5`
  and `q2rdm10`. 49 maps are fully covered. It also holds models, icons and 9 skies. Only `night1` is a
  remake of its original; `unit1_` to `unit9_` are one mountain range in two tints (found at 3b).
- `content/qpakman-master` is a GPL pak file tool. We read loose files, so we don't need it, and its
  code mustn't be copied into this project.
- WAL textures hold no colours of their own. `content/palette.act` holds the palette, taken from the
  887 PCX files that share it. Don't use `textures/new/q2.act`: it has ten colours blanked to white.

## How it's built

```
SimpleFps.Core/        Plain C#, no Godot: BSP reader, bake, collision, movement,
                       weapons, the game simulation
SimpleFps.Core.Tests/  Runs without Godot
SimpleFps.Game/        The Godot project: drawing, input, sound, menus
content/               maps, textures, sound, players, env (not in git)
cache/                 Baked files (not in git)
```

Same split as Overburden and Evolution. The Godot project has to sit in its own subfolder,
otherwise Godot tries to import all 3,000+ sounds and every image.

**Simulation.** A fixed 60 ticks a second, working in map units, driven only by per-tick input
commands. A player, a bot and later a network client are all just sources of commands. The local
game talks to its own server in the same process from day one.

**Collision.** Our own box sweep against the map's brushes, not Godot's physics. Quake 2 maps store
the exact solid shapes they were built from, so this is accurate. It also runs the same on server
and client, can be tested without Godot, and wall grace can live inside the sweep itself.

**Movement.** Starting numbers from that era, then tuned by feel: run 300, accelerate 10, friction 6,
gravity 800, jump 270, step 18, body 32 wide and 56 tall. Our changes:
- Air steering only rotates your velocity a little and never adds speed, so strafe-jumping can't exist.
- Holding jump rebounds off a landing. During that brief touch you can turn, and speed lost depends on
  how sharp the turn is.
- Corner slide: if a move is blocked but a small sideways offset (starting at about 8 units) is clear,
  you're eased across it and keep your speed.
- Squeeze: your body narrows a little (starting at 32 down to about 24) when pressed against a wall or
  into a gap, and springs back when there's room.
- Recoil acts as thrust in the air, where there's no friction to eat it, so hopping backwards down a
  hallway while firing is a real escape. This is a wanted trick, and tuning must keep it. Hits push you
  too, so a chaser's shots speed you up while their own recoil slows them. Recoil gets its own top-speed
  setting, so it can be limited without touching blasts.
- All of these are live settings on an in-game tuning panel. Online, the panel belongs to the server's
  host (see Server movement above). Clients predict their own movement, so they have to run the same
  numbers as the server, which means the server sends its settings to everyone anyway.

**Weapons.** Defined in data files, not code, so a huge arsenal means adding data. Each weapon has
damage, fire rate, knockback, recoil, and optional light. Code is only needed for each new behaviour:
instant hit, projectile, guided, homing, placed gadget, turret.

**Lighting.** Baked light goes in as a texture layer. Moving lights are small and cast no shadows.

**Bake.** One file per map, holding meshes, the light atlas, collision, entities and bot paths. Kept in
`cache/`, keyed on file hash and bake version, so changing the format just re-bakes. New maps can be made
with any free Quake 2 map editor and compiler, so we don't need our own editor. Built at 3a: maps bake
to `cache/maps/<name>.bake`, a list of tagged sections, so bot paths are one more section. An `index.json`
beside them lists each map's title, author and which map it's a copy of. The map list at start (3c)
doesn't wait for bakes: it reads every map's title straight from the map in about 0.2 s, and hides
copies (keeping the shortest name) and maps without deathmatch spawns. Built at 3b: textures and skies
bake to `cache/textures` and `cache/skies`, one file per name, decoded and mipmapped, keyed the same way.
Built at 4a: sounds bake to `cache/sounds`, one file per sound as 16-bit mono, keyed the same way, and map
bakes carry the map's visibility data, which decides which sounds you can hear.

## Milestones

Each is about one session, and each ends in something you can run.

0. **Set up.** Repo, projects, Godot in a subfolder, content moved, tests running. Done.
1. **See a map.** Read Quake 2 maps and fly round one with textures and baked light. A test loads all
   363. Done.
2. **Move.** Collision, run, jump, hop, ladders, water, wall grace, tuning panel. Done: Matthew
   played it by hand and kept every default (2026-11-24). Doors and lifts aren't solid until milestone 4.
3. **Bake.** Own level format and cache, stand-in textures, high-res swapping with a switch back to the
   originals, skies, `.override` files, and a map list at start with titles and authors. Sounds and
   models moved to the milestones that first use them (agreed 2026-11-24). Built 2026-11-24 in three
   sessions; Matthew's hand checks are in TODO.md.
4. **Map objects.** Doors, lifts, buttons, trains, spinning things, teleporters, push zones, hurt zones,
   and the triggers, timers and relays that set them off. Sounds are baked here, for doors, lifts,
   teleporters and the maps' ambient sound spots (moved from milestone 5, agreed 2026-11-25). Objects
   run inside the simulation at 60 ticks, so online play later needs no rewrite. Anything that needs
   damage waits for milestone 5: buttons you shoot, walls that break, and crushing. Until then a blocked
   door or lift backs off. Monsters in single-player maps are ignored.
5. **Guns.** A few weapons, damage, knockback, recoil, energy round lights, pickups placed from the map's own item spots.
   Also the map objects that need damage: buttons you shoot, walls that break, crushing, hurt zones.
6. **Bots.** Paths generated at bake time, then a real deathmatch. This is the first full game. MD2 models
   are baked here, for the bots' bodies.
7. **Online.** Split the server off properly, add prediction, play between the laptop and the desktop.
   The server sends its movement settings on joining, and announces changes, which apply at the next round.
8. **Destruction mode.** The big arsenal, guided weapons, gadgets, traps.
9. **Ship clean.** Replace placeholders, see below.

## Biggest risks

- **Bots on arbitrary maps.** There's no path data in these maps, so it has to be generated, and maps
  built around jump pads and lifts make that harder. Milestone 6 is the likeliest to overrun.
- **Map objects.** Quake 2 maps lean on a wide set of entity types. We'd build the common ones and log
  the rest, so some maps will have stuck or missing parts for a while.
- **Texture coverage.** Depends on what finishes copying. Stand-ins cover any gaps.

## Ship clean (before any public release)

- Remove the eight original Quake 2 levels (`q2dm1` to `q2dm8`) and edits of them. They're also kept out
  of the test and tuning set from the start, so the game never gets shaped around them.
- Remove textures from Quake 2's own folders (`e1u1` to `e3u3`). 1,716 of the 4,527 texture names the maps
  use point there, so community maps that use them need reskinning through the per-map file.
- Remove those folders from `q2textures-master` too. 1,661 of its 1,828 textures are high-res remakes of
  them. The pack's licence is permissive, but it says the images may carry licences of their own.
- Remove characters owned by others: Homer, Marvin the Martian, Megaman, Megatron.
- Remove sounds from the original game and other games (for example `sound/daikatana`, and monster folders
  like `sound/brain` and `sound/bosstank`).
- Ship only maps with clear credit and permission, plus a generated credits list.
- Keep the drop-in folder, so players can add maps they already have.

## Open items for Matthew

- Name of the game.
- Crouch-jumping was left out. It's a checkbox on the tuning panel, off by default. In this style of
  physics it shortens your body in mid-air to fit low openings, and doesn't lift your legs.
- Hopping keeps speed but never gains it, so going faster than a run comes from blasts, recoil and jump
  pads. How fast recoil alone can take you gets decided once it's playable.
- How many weapons the normal game has, compared with Destruction mode. Decide at milestone 5.
