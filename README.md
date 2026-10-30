# Dying Light-Style Dropkick Mod for The Forest (2018)

[![Latest Release](https://img.shields.io/github/v/release/Aarnavkrdubey/TheForest-DyingLight-Dropkick-Mod?color=success&label=Release)](https://github.com/Aarnavkrdubey/TheForest-DyingLight-Dropkick-Mod/releases/latest)
[![Game: The Forest](https://img.shields.io/badge/Game-The%20Forest%20(2018)-forestgreen.svg)](https://store.steampowered.com/app/242760/The_Forest/)
[![Mod Loader](https://img.shields.io/badge/Loader-BepInEx%205%20(x64)-orange.svg)](https://github.com/BepInEx/BepInEx)
[![Patching](https://img.shields.io/badge/Hook-HarmonyX-red.svg)](https://github.com/BepInEx/HarmonyX)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A complete, standalone C# gameplay modification for the original **The Forest (2018)** (not *Sons of the Forest*) that introduces the iconic **two-legged airborne dropkick from *Dying Light***.

Featuring true procedural skeletal animation on the player character rig, collision-based enemy hit detection, momentum-scaled knockback, ragdoll/knockdown integration, and **realistic gravity physics** (enemy impact recoil, falling onto your back, ground skid, and push-up recovery).

All original **`E` interactions** (`Take`, `Build`, opening doors, climbing ropes, rafts, sleds, and animal pickups) are cleanly remapped to **`V`** with in-game UI prompt icon updates and full reversibility.

---

## Features & Highlights

- **Airborne Dropkick Mechanics**:
  - Jump into the air and press `E` to unleash a two-legged flying dropkick.
  - Sprinting (`Shift`) prior to jumping triggers a **Sprint Momentum Bonus** with increased forward thrust, +35% physical damage, and heavier knockback.
- **Procedural Two-Legged Skeletal Animation**:
  - Operates dynamically in `LateUpdate` across the player skeleton (`char_Hips`, `char_LeftUpLeg`, `char_LeftLeg`, `char_LeftFoot`, `char_RightUpLeg`, `char_RightLeg`, `char_RightFoot`, torso, and arms).
  - Features distinct **Knee-Tuck Chamber (`Startup`)**, **Dual-Boot Snap Thrust (`ActiveKick`)**, and **Gravity Fall & Push-Up Recovery**.
- **Gravity Realism & Ground Recovery (*Dying Light* Style)**:
  - **Enemy Impact Rebound**: When both boots smash into an enemy, Newton's 3rd law causes a physical rebound backward while the enemy gets launched.
  - **Gravity Back-Fall**: After the midair kick, gravity naturally accelerates the horizontal player body down to the ground.
  - **Ground Landing Skid (`GroundImpact`)**: The player crashes onto their back/hip against the terrain with a heavy thud, dirt skid, and ground-level first-person camera perspective.
  - **Hand-Plant & Push-Up Recovery (`GettingUp`)**: The character rolls onto their side, plants both hands against the dirt, and pushes back up to standing height.
- **Collision & Combat Integration**:
  - Multi-probe collision sweep (`Physics.SphereCastAll` + dual-foot `OverlapSphere` + forward capsule) with anti-tunneling protection.
  - Line-of-sight wall occlusion checks prevent hitting through solid walls or camp defenses.
  - Per-dropkick enemy deduplication guarantees targets are hit only once per move.
  - Normal cannibals receive full knockdown (`EnemyHealth.hitFallDown`) and smooth horizontal knockback displacement.
  - Heavy creepy mutants (Armsy, Virginia, Cowman) receive mass-scaled stagger reactions with `0.32x` knockback.
  - Multiplayer client synchronization via Bolt network packets (`PlayerHitEnemy`).
- **Clean Input Remapping & Dynamic UI**:
  - `E` $\rightarrow$ Airborne Dropkick
  - `V` $\rightarrow$ All contextual interactions (`Take` and `Build`)
  - In-game HUD interaction billboard prompts (`ActionIconSystem` / `InputMappingIcons`) dynamically display `V`.
  - Zero conflicts with pause menus, inventory bag, survival book, or multiplayer chat.
  - 100% reversible: original keys are preserved before `PlayerPrefs` is saved, leaving your game settings completely untouched upon removal.

---

## Controls

| Key | Action | Behavior |
| :--- | :--- | :--- |
| **`Space` $\rightarrow$ `E`** | **Airborne Dropkick** | Jump, then press **`E`** while airborne to execute the two-legged dropkick. |
| **`Shift` + `Space` $\rightarrow$ `E`** | **Sprint Dropkick** | Sprint-jump before pressing **`E`** to trigger the **Sprint Momentum Bonus** (increased forward impulse, +35% damage, and maximum enemy knockback). |
| **`V`** | **Interact / Take / Build** | Replaces original `E` for all world interactions: picking up logs, rocks, sticks, animals, opening doors, climbing ropes, sailing rafts, pushing sleds, eating/drinking, and crafting blueprints. |
| **`Shift`** | **Sprint** | Standard sprint. |
| **`Space`** | **Jump** | Standard jump. |

---

## Installation

### Prerequisites
* **The Forest (PC / Steam)** (64-bit original game).
* **BepInEx 5 (x64)** (included in the standalone release package).

> **Compatibility Note**: Works alongside ReShade (`dxgi.dll`). BepInEx uses `winhttp.dll` (UnityDoorstop) which loads independently without any DLL collisions.

### Option A: Quick Install (Pre-Packaged Release)
1. Download **`TheForestDropkickMod_v1.0.0_BepInEx5.zip`** from the [Releases](../../releases) tab.
2. Open your *The Forest* game directory:
   * In Steam: Right-click **The Forest** $\rightarrow$ **Manage** $\rightarrow$ **Browse local files**
   * (Default: `C:\Program Files (x86)\Steam\steamapps\common\The Forest`)
3. Extract the contents of the `.zip` archive directly into your game folder such that `winhttp.dll` and the `BepInEx` directory sit directly next to `TheForest.exe`.
4. Launch the game normally via Steam!

### Option B: Automated PowerShell Installer
If you cloned the repository on your machine:
```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

---

## Configuration

Settings are saved to `BepInEx/config/com.theforest.dyinglightdropkick.cfg` and can be edited with any text editor. Changes **hot-reload automatically** even while the game is running.

```ini
[1. Controls]
# Key used while airborne to execute the dropkick (Default: E)
DropkickKey = E

# Key replacing original E interactions: items, doors, ropes, rafts, blueprints (Default: V)
InteractionKey = V

[2. Mechanics]
# Master toggle for the mod (Default: true)
DropkickEnabled = true

# Sprint momentum bonus multiplier (Default: true)
SprintMomentumBonus = true

# Total duration of the midair kick attack in seconds (Default: 0.54)
DropkickDuration = 0.54

# Cooldown between dropkicks in seconds (Default: 1.2)
Cooldown = 1.2

# Whether the dropkick consumes stamina (Default: true)
StaminaConsumption = true

# Stamina drained per dropkick (Default: 16.0)
StaminaCost = 16.0

[3. Combat & Physics]
# Base physical damage inflicted on impact (Default: 42.0)
DropkickDamage = 42.0

# Forward velocity impulse speed in m/s (Default: 7.2)
DropkickForce = 7.2

# Knockback impulse speed applied to cannibals in m/s (Default: 10.5)
EnemyKnockbackForce = 10.5

# Knockback multiplier for heavy mutants: Armsy, Virginia, Cowman (Default: 0.32)
HeavyMutantKnockbackMultiplier = 0.32

[4. Camera & Feedback]
# Impact camera shake intensity: Off, Low, Medium, High (Default: Low)
CameraShake = Low

[5. Debugging]
# Verbose diagnostic logging in BepInEx/LogOutput.log (Default: false)
DebugLogging = false
```

---

## Uninstallation

To remove the mod:
1. Run the uninstaller script:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
   ```
   *(Add `-RemoveBepInExLoader` to also remove BepInEx entirely)*.
2. **Or manually delete**:
   * `BepInEx/plugins/TheForestDropkickMod/TheForestDropkickMod.dll`
   * `BepInEx/config/com.theforest.dyinglightdropkick.cfg`

Because the mod safely intercepts `InputMapping.SaveAllMaps`, removing the DLL automatically restores all original `E` bindings without altering your `PlayerPrefs` or game files.

---

## Building from Source

### Prerequisites
* Windows 10 / 11
* .NET Framework 4.0 / 4.5+ Developer Tools or Visual Studio / MSBuild
* *The Forest* game files (for assembly references)

### Build Command
Run the included build script:
```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```
The compiled assembly will be produced at `bin/Release/TheForestDropkickMod.dll`.

---

## Project Structure

```text
TheForestDropkickMod/
├── TheForestDropkickMod.csproj      # MSBuild C# project
├── build.ps1                        # Build script
├── install.ps1                      # Game installation script
├── uninstall.ps1                    # Clean uninstallation script
├── LICENSE                          # MIT License
├── README.md                        # Documentation
├── config/
│   └── com.theforest.dyinglightdropkick.cfg  # Default config template
├── Properties/
│   └── AssemblyInfo.cs              # Assembly metadata
├── Release/
│   └── TheForestDropkickMod_v1.0.0_BepInEx5.zip  # Ready-to-install bundle
└── src/
    ├── DropkickModPlugin.cs         # BepInEx plugin entrypoint & lifecycle
    ├── ModConfig.cs                 # Config loader, hot-reloader & conflict validator
    ├── DebugLogger.cs               # Structured logging
    ├── GameReflection.cs            # Pre-cached reflection accessors
    ├── InputRemapManager.cs         # Rewired E->V remapping & UI prompt sync
    ├── DropkickStateMachine.cs      # State machine, physics impulses & gravity recovery
    ├── DropkickAnimator.cs          # Procedural two-legged skeletal animation
    ├── DropkickCameraFeedback.cs    # First-person camera framing, shake & FMOD audio
    ├── DropkickHitDetector.cs       # Continuous dual-foot collision sweeps & wall checks
    ├── EnemyCombatIntegration.cs    # Cannibal knockdown, mutant stagger & network sync
    └── RuntimeSelfTest.cs           # Automated in-engine verification suite
```

---

## License

This project is licensed under the [MIT License](LICENSE).
Feel free to modify, distribute, or incorporate into mod packs with appropriate credit.
