# Star Trek Fan Game

A Star Trek–themed top-down space shooter built with **C# / WPF** on **.NET 10**.
Pilot your starship, sweep the field with photon torpedoes, and clear waves of
incoming **Borg** vessels — cubes, spheres, and pyramids — across an endless
run of increasingly crowded levels.

> Non-commercial fan project. Star Trek and all related marks are trademarks of
> their respective owners; this game is an unofficial, educational tribute and
> is not affiliated with or endorsed by CBS / Paramount.

## Gameplay

- **Fly** your ship anywhere on the field and **aim freely through 360°**.
- **Destroy Borg ships** for points. Larger vessels take more hits and flash red
  as they take damage before exploding.
- **Survive.** Colliding with an enemy costs a shield; lose all your shields and
  it's game over. You get a brief window of invulnerability after each hit.
- **Clear the field** to "warp" to the next level — a new starfield background
  and more enemies each time.

### Controls

| Action | Input |
| --- | --- |
| Move ship | `W` `A` `S` `D` |
| Aim | Mouse |
| Fire | Left click or `Spacebar` |

The toolbar along the bottom adds **Play / Step / Stop**, **Spawn / Clear**, a
manual **Fire!** button, and lets you switch between two fire modes:

- **Rifle** — fires one torpedo per click/press. Never overheats.
- **Machine Gun** — auto-fires while held, but builds heat. Overheat and you're
  locked out until it cools (watch the heat gauge, top-right).

## Features

- ~60 FPS retained-mode rendering on a `DispatcherTimer` — visuals are created
  once and repositioned each frame to keep the game loop smooth.
- Elastic shape-to-shape collisions, particle explosions, and torpedo trails.
- Borg sprite variants chosen by enemy size, with hit points that scale to match.
- Looping Star Trek: The Next Generation theme plus pooled torpedo / explosion
  sound effects for overlap-free audio.
- Shield pips, score, level, and fire-mode HUD overlay.

## Requirements

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`net10.0-windows`)
- Visual Studio 2022 (optional) — the solution opens directly

## Build & Run

```bash
# from the repository root
dotnet run --project StarTrekFanGame
```

Or build the whole solution:

```bash
dotnet build StarTrekFanGame.sln
```

In Visual Studio, open `StarTrekFanGame.sln`, set **StarTrekFanGame** as the
startup project, and press **F5**.

## Project layout

```
StarTrekFanGame.sln
StarTrekFanGame/
├─ App.xaml / App.xaml.cs        # WPF application entry point
├─ MainWindow.xaml(.cs)          # game loop, rendering, input, HUD
├─ AudioManager.cs               # theme music + pooled sound effects
├─ Model/                        # game entities
│  ├─ GameModel.cs               # aggregate game state
│  ├─ GameShape.cs               # abstract bouncing entity
│  ├─ Circle / RectShape / TriangleShape
│  ├─ Bullet.cs                  # photon torpedo
│  ├─ Gun.cs                     # aim, fire rate, machine-gun heat
│  └─ ExplosionParticle.cs
└─ Assets/                       # sprites, backgrounds, audio
```
