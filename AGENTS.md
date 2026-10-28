# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, the monsters and the
controls; this file covers how the code is put together and the rules that keep it working.

## What this is

A dungeon shoot-out in the style of Midway's 1981 arcade game Wizard of Wor, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).
Everything in it is original: the graphics are drawn in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAWizardOfWor.slnx                  # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAWizardOfWor.slnx                   # every test runs headless: no window opens
dotnet run --project src/AVAWizardOfWor           # play it; --no-audio for silence
WOR_SHOTS=$PWD/Images dotnet test tests/AVAWizardOfWor.Tests --filter ScreenshotTests   # refresh the README's pictures
WOR_PROBE=1 dotnet test tests/AVAWizardOfWor.Tests --filter PilotProbe   # how the computer warrior does ("lazy" for one that stands and fires)
python3 tools/make_sounds.py                      # rebuild src/AVAWizardOfWor/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAWizardOfWor/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, pause, game over, initials, high scores) and the
    fixed-step loop.
  - `GameEngine.Play.cs` plays a dungeon: the warrior and his one shot, the monsters, the side doors,
    the wisp and the wizard.
  - `Maze.cs` builds each dungeon (eleven cells by six, mirrored, no dead ends) from its number, including the
    courtyard and hollow layouts.
  - `Entities.cs` has `Controls`, directions, the screen and cell measures (`Field`), the warrior and the enemies.
  - `WarriorPilot.cs` is the computer player, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAWizardOfWor/Views/`: `GameCanvas` (with `GameCanvas.Draw.cs`) draws the 1024x768 frame and runs the 60 Hz
  timer. Ctrl+S saves a screenshot to the Pictures folder.
- `src/AVAWizardOfWor/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAWizardOfWor/Sounds/`: the generated WAV files, copied next to the executable.
- `tests/AVAWizardOfWor.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `GameplayTests.cs` holds most tests;
  `PilotProbe.cs` and `ScreenshotTests.cs` are switched on by the variables above.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the game in fixed 1/60 s steps (`Field.Step`) from an accumulator,
  independent of the frame rate, so tests can play whole dungeons deterministically from a seed. Draw random
  numbers from the engine's seeded `_rng` (a `Random(seed)`), never from `Random.Shared`. Each maze is built from
  its own fixed seed in `Maze.Build`, so a dungeon number is always the same maze.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the same `Dy` sign the engine
  expects (negative is up the screen). Two directions at once means "turn at the next corner". Test new controls
  through `InputHandler`, not by building `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAWizardOfWor`); tests load
  the table from a fresh folder under the system temp folder and run silent (`Silent = true`).

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the mazes or monsters change, run the pilot probe to see how the computer warrior copes.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, monsters, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
