# TODO

## Next
- [x] Matthew: play it and tune it (double-click `start.cmd`, Tab for the panel). Done 2026-11-24: it
      feels good with every default unchanged.
- [ ] Matthew: check that bobbing at a liquid's surface no longer builds speed (capped 2026-11-24). It's
      the last movement change not yet tried by hand.
- [ ] Milestone 3: bake (see PLAN.md).

## Milestone 3
Agreed 2026-11-24: maps and textures only, run as three sessions. 3a first, then 3b and 3c side by side
(Matthew, 2026-11-24), each in its own git worktree under `.claude/worktrees`. A worktree has no `content/`
of its own, so the game and `start.cmd` look for it and for Godot further up (06e8153).

3a, map bake:
- [x] Bake each map to our own format in `cache/`, keyed on file hash and bake version: meshes, light
      atlas, collision, entities, title and author. The game loads only the baked file. Done 2026-11-24.
- [x] Honour the `.override` entity files. Done, including the 12 that play another map's `.bsp` under
      their own name.
- [x] Spot renamed copies by hash (`q2pduel7` = `q2rdm2`), so the map list can hide them. Done: 8 found.

3b, textures:
- [ ] Bake textures to our own format.
- [ ] Swap in high-res textures from `content/q2textures-master/pak99_textures/textures`, keeping the
      `.wal` size for texture coordinates.
- [ ] A switch between high-res and the original textures: a setting, plus a key while testing. Original
      textures stay smooth, as now.
- [ ] Skies. They're a flat colour for now. Maps name theirs with the `sky` key, and both `content/env`
      and the pack hold images.

3c, map list:
- [x] A map list at start, with titles and authors from the bake and renamed copies hidden.
      `start.cmd --map=name` still skips it. Done 2026-11-24: one list sorted by title, a search box,
      Enter or double-click to play, and an Esc menu in a map (Resume, Map list, Quit). Maps without
      deathmatch spawns are hidden too.
- [x] The bake's index only lists maps baked so far. Done: Matthew chose a quick read over baking
      everything. The list reads each map's title straight from the map, about 0.2 s for all 375.
- [x] Matthew: of a set of copies, which name is kept. Done: the shortest, then the one that sorts
      first (Matthew, 2026-11-24), so `q2rdm2` stays and `q2pduel7` is hidden. The index uses the
      same rule.
- [ ] Matthew: try the map list by hand. Search, arrows, Enter, double-click, and Esc then "Map list"
      have only been run headless or by script, and the list's look only seen in one picture.

## Milestone 5
- [ ] Bake sounds (moved from milestone 3).

## Milestone 6
- [ ] Bake MD2 models (moved from milestone 3).

## Milestone 7
- [ ] The server owns the movement settings and sends them to each player on joining. Only the host,
      or admins they name, can change them.
- [ ] Announce a change to everyone at once, and apply it at the next round.
- [ ] Draw the announcement's picture by running a jump with the old and new settings and drawing both
      arcs, with the old and new values under it.
- [ ] Mark servers with changed movement in the server list: a badge and a one-line summary.

## Later
- [ ] An options menu with invert Y (Matthew plays inverted) and mouse speed. These belong to each
      player, so they stay out of `MoveSettings`, which the server will own. Mouse speed is fixed at
      0.1 degrees a pixel in `LocalPlayer.cs` until then.
- [ ] Moving water and lava, flowing surfaces, flickering lights and animated textures.
- [ ] 33 maps are missing at least one texture. Most missing names are `gothic_*`, so check whether
      those maps came from another game's texture set.
- [ ] Lava and slime do nothing yet. Add damage when health exists.
- [ ] Long traces, such as hitscan weapons, should walk the map's tree along the line. The current
      search uses a box around the whole trace, which is fine for movement but slow across a big map.
- [ ] Unzip or read `textures/1492.zip` and `textures/alk09.zip`.
- [ ] Decide what to do with the stray files: `.pk`, `.sfk`, `.old`, `.bad`, `.tmp`.
- [ ] Map bakes aren't compressed: 632 MB if every map is baked, against 389 MB of `.bsp` files. Compress
      them if disk space starts to matter, at some cost in load time (a bake loads in about 22 ms now).
