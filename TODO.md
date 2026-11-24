# TODO

## Next
- [x] Matthew: play it and tune it (double-click `start.cmd`, Tab for the panel). Done 2026-11-24: it
      feels good with every default unchanged.
- [ ] Matthew: try the parts nobody has confirmed by hand yet: squeezing through gaps, ladders (try
      `start.cmd --map=rexro --spawn=2` and turn round), crouch, R respawn, V fly, and moving a slider on the panel to see the change straight away.
- [ ] Milestone 3: bake (see PLAN.md).

## Milestone 3
- [ ] Swap in high-res textures from `content/q2textures-master/pak99_textures/textures`, keeping the
      `.wal` size for texture coordinates.
- [ ] Honour the `.override` entity files.
- [ ] Skies. They're a flat colour for now. Maps name theirs with the `sky` key, and both `content/env`
      and the pack hold images.

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
