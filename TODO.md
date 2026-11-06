# TODO

## Next
- [ ] Matthew: play it and tune it (`start.cmd --map=ringbase`, Tab for the panel). Keyboard and mouse
      haven't been tried by hand yet, and feel can only be judged by playing.
- [ ] Milestone 3: bake (see PLAN.md).

## Milestone 3
- [ ] Swap in high-res textures from `content/q2textures-master/pak99_textures/textures`, keeping the
      `.wal` size for texture coordinates.
- [ ] Honour the `.override` entity files.
- [ ] Skies. They're a flat colour for now. Maps name theirs with the `sky` key, and both `content/env`
      and the pack hold images.

## Later
- [ ] Moving water and lava, flowing surfaces, flickering lights and animated textures.
- [ ] 33 maps are missing at least one texture. Most missing names are `gothic_*`, so check whether
      those maps came from another game's texture set.
- [ ] Lava and slime do nothing yet. Add damage when health exists.
- [ ] Long traces, such as hitscan weapons, should walk the map's tree along the line. The current
      search uses a box around the whole trace, which is fine for movement but slow across a big map.
- [ ] Unzip or read `textures/1492.zip` and `textures/alk09.zip`.
- [ ] Decide what to do with the stray files: `.pk`, `.sfk`, `.old`, `.bad`, `.tmp`.
