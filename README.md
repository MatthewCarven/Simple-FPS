# Simple FPS

Working title. An original deathmatch shooter in Godot 4.7 and C#, with classic quick movement
and its own twists: you hop like you're on ice, squeeze round corners instead of snagging on them,
and every shot and blast shoves you about. See [PLAN.md](PLAN.md).

Early days. It doesn't play yet.

## Running it

Needs the .NET SDK and Godot 4.7.1 mono. `start.cmd` builds and launches it, expecting Godot in
`..\MinerCraft\Godot_v4.7.1-stable_mono_win64`.

Tests run without Godot:

```
dotnet test SimpleFps.slnx
```

## Layout

- `SimpleFps.Core`: plain C# with no Godot, for the simulation and the map loading
- `SimpleFps.Core.Tests`: tests for Core
- `SimpleFps.Game`: the Godot project

No maps, textures, sounds or models live in this repo.
