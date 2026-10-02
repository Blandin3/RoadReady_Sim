# RoadReady VR — Dual-Perspective Road-Safety Simulator

**GitHub repository:** <https://github.com/Blandin3/RoadReady_Sim>

**Designs:** [VR walkthrough visuals](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=9-175&t=kusLrvXvi8BtziO3-1) · [Activity diagram](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=32-7&t=kusLrvXvi8BtziO3-1) · [Interaction flowchart](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=20-1950&t=kusLrvXvi8BtziO3-1) (details in [§13](#13-designs))

## Description

RoadReady is a Unity virtual-reality simulator for practising road-safety decisions in streets modelled on **Kimironko, Kigali (Rwanda)**. Every scenario can be played from **two perspectives**:

- 🚗 **Driver**: drive a car through the scenario and obey speed limits, signals, zebra crossings and give-way rules.
- 🚶 **Pedestrian**: walk and cross the same street, judge gaps in traffic and look both ways.

After each attempt the learner sees a safety score, an analysis of their hazard awareness and a **"Seen from the other side"** insight. One button then reloads the *same* scenario as the *other* road user. RoadReady is also a research instrument. It runs a single-group pre-test / training / post-test study, includes a briefing-only comparison group, and records anonymised data that can be exported to CSV.

---

## Table of contents

- [Description](#description)
1. [Features](#1-features)
2. [Requirements](#2-requirements)
3. [Setting up the development environment](#3-setting-up-the-development-environment)
4. [Getting the project](#4-getting-the-project)
5. [First-time project setup (Build Everything)](#5-first-time-project-setup-build-everything)
6. [Running in the Unity Editor](#6-running-in-the-unity-editor)
7. [Building and running on Meta Quest](#7-building-and-running-on-meta-quest)
8. [Configuration](#8-configuration)
9. [Running a study session](#9-running-a-study-session)
10. [Data storage and export](#10-data-storage-and-export)
11. [How the simulation works](#11-how-the-simulation-works)
12. [Project structure](#12-project-structure)
13. [Designs](#13-designs)
14. [Troubleshooting](#14-troubleshooting)

---

## 1. Features

| Area | What is implemented |
|---|---|
| **Dual perspective** | Each scenario supports Driver and Pedestrian. Progress, levels and history are tracked separately for each perspective. After any attempt, *Switch perspective* reloads the same scenario as the other road user. |
| **Scenarios** | Market zebra crossing (unsignalised), Signalised intersection, Moto-taxi junction (unsignalised T-junction), plus an unscored tutorial for each perspective. |
| **Rule-based evaluation** | A deterministic rule engine labels every decision **Safe / Borderline / Unsafe** with a severity (minor, major, critical). |
| **Hazard perception** | Scripted hazards are measured with head-gaze tracking and response checks. Each one is classified with Endsley's three situation-awareness levels (perception, comprehension, projection). |
| **Safety score** | A 0–100 composite score: hazard detection 45 %, reaction time 25 %, rule compliance 30 %. A collision caps the score at 40. |
| **Difficulty levels** | Up to 3 levels per scenario. The next level unlocks at a score of 80 or more. |
| **Study design** | Baseline (pre-test), Training and Post-test phases. Baseline and post-test always use Level 1. A *Briefing-only* comparison group gets a 6-page verbal safety briefing instead of VR training. |
| **Ethics** | Informed-consent screen. Declining or withdrawing deletes all of that participant's data. Only anonymous IDs (e.g. `RR-7KQ2TX`) are stored. |
| **Questionnaires** | Simulator Sickness Questionnaire (SSQ) after every session. System Usability Scale (SUS) after the post-test session. |
| **Comfort** | Comfort vignette, snap or smooth turning, seated mode, assisted crossing, text size, volume and voice instructions. |
| **Data** | Crash-safe local JSON storage, per-attempt telemetry (NDJSON), CSV export and optional sync to Firebase or a REST endpoint. |
| **Admin panel** | Opened with a PIN. Lets the researcher tune scenario parameters, export CSV or delete all data. |

---

## 2. Requirements

### Software

| Tool | Version | Notes |
|---|---|---|
| **Unity Hub** | latest | <https://unity.com/download> |
| **Unity Editor** | **6000.3.21f1** (Unity 6.3) | Must match `ProjectSettings/ProjectVersion.txt`. |
| Unity module: **Android Build Support** | with **OpenJDK** and **Android SDK & NDK Tools** | Only needed to build for Meta Quest. |
| Unity module: **Windows Build Support (IL2CPP)** | optional | For a PC VR standalone build. |
| **Git** (+ Git LFS recommended) | any recent | To clone the repository. |
| **Meta Quest Link** app | latest | Optional. Lets you play in the Editor on a Quest headset over USB or Air Link. |
| **Android Platform Tools (`adb`)** | ships with Unity's Android SDK | Used to install builds and pull exported data from the headset. |
| Code editor | VS Code (C# Dev Kit + Unity extension) or Visual Studio 2022 | |

### Hardware

- A Windows 10/11 PC that can run Unity 6, with a dedicated GPU recommended for PC VR / Quest Link.
- **Meta Quest 2, 3, 3S or Pro** (the OpenXR Meta Quest feature and Touch / Touch Plus / Touch Pro controller profiles are enabled).
- USB-C data cable for Quest Link and `adb`.

> No headset? You can still develop and test in the Editor with **keyboard and mouse** (see [§6](#6-running-in-the-unity-editor)).

### Main Unity packages (already in `Packages/manifest.json`)

| Package | Version |
|---|---|
| XR Interaction Toolkit | 3.5.1 |
| OpenXR Plugin | 1.17.1 |
| Unity OpenXR: Meta | 2.5.1 |
| XR Plug-in Management | 4.6.0 |
| XR Hands | 1.8.1 |
| XR Core Utilities | 2.6.0 |
| AR Foundation | 6.5.0 |
| XR Composition Layers | 2.5.0 |
| Android XR (OpenXR) | 1.3.1 |
| Input System | 1.20.0 |
| Universal Render Pipeline | 17.3.0 |

Unity downloads all of these automatically the first time the project is opened.

---

## 3. Setting up the development environment

1. **Install Unity Hub** and sign in with a Unity ID (a free Personal licence is fine).
2. In Unity Hub go to **Installs → Install Editor → Archive**. Install **6000.3.21f1** (or open the project first; Hub will offer to install the right version).
3. While installing, tick these modules:
   - ✅ **Android Build Support**
     - ✅ OpenJDK
     - ✅ Android SDK & NDK Tools
   - ✅ Windows Build Support (IL2CPP), optional
   - ✅ Visual Studio / VS Code integration, optional
4. **Install a code editor.** In Unity, open **Edit → Preferences → External Tools** and set *External Script Editor* to VS Code or Visual Studio.
5. **(Quest users) Prepare the headset:**
   1. Install the **Meta Horizon** app on your phone and pair the headset.
   2. Enable **Developer Mode** (Meta Horizon app → *Devices* → *Headset settings* → *Developer mode*). This requires a free Meta developer account / organisation.
   3. Install **Meta Quest Link** on the PC to test in the Editor.
   4. Connect the headset over USB and accept the *Allow USB debugging* prompt inside the headset.

---

## 4. Getting the project

```bash
git clone https://github.com/Blandin3/RoadReady_Sim.git
cd RoadReady_Sim
```

Then, in **Unity Hub → Projects → Add → Add project from disk**, select the `RoadReady_Sim` folder and open it with **6000.3.21f1**.

The first import takes several minutes because Unity rebuilds the `Library/` folder and resolves packages. `Library/`, `Temp/`, `Logs/`, `UserSettings/` and `Build/` are git-ignored on purpose. Never commit them.

> If Unity asks to enable the **new Input System** backend and restart, click **Yes**.

---

## 5. First-time project setup (Build Everything)

RoadReady generates its scenes, prefabs, materials, configuration assets and UI panels from code, so you get a consistent project with one click.

1. Wait for the import and script compilation to finish (no spinner bottom-right, no red errors in the Console).
2. In the top menu choose **`RoadReady → Build Everything`**.
3. This command:
   - creates materials, prefabs and the world-space UI panel settings
   - creates the configuration assets in `Assets/RoadReady/Generated/Config/`: `RoadReadyConfig`, `ScoringConfig`, the scenario definitions and hazard definitions
   - builds the scenario scenes in `Assets/RoadReady/Generated/Scenes/`: `RR_Tutorial`, `RR_ZebraCrossing`, `RR_SignalizedIntersection`, `RR_MotoTaxiJunction`
   - builds the bootstrap scene `RR_Bootstrap` (XR rig, UI, HUD, app controller)
   - writes the **Build Settings** scene list (bootstrap first, then the scenario scenes)
   - sets `RR_Bootstrap` as the **Play Mode start scene** and opens it
4. The Console prints: `[RoadReady] Build complete. Press Play…`

Other menu commands:

| Menu item | Purpose |
|---|---|
| `RoadReady → Rebuild Scenario Scenes Only` | Regenerate the four scenario scenes after editing the scene builder. |
| `RoadReady → Open Data Folder` | Open the local study-data folder used in the Editor. |
| `RoadReady → Export CSV (editor data)` | Export all Editor-recorded data to CSV. |
| `RoadReady → Play From Current Scene (disable bootstrap redirect)` | Press Play in whatever scene is open. Scenario scenes have **no camera** by design, so only use this when debugging. |

> ⚠️ Re-running **Build Everything** regenerates the generated assets. Copy hand-tuned values from `RoadReadyConfig` / `ScoringConfig` somewhere safe first, or change them in the generator code.

---

## 6. Running in the Unity Editor

Press **Play**. The game always starts from `RR_Bootstrap`, even if another scene is open. There are three ways to test.

### A. Keyboard and mouse (no headset)

| Action | Driver | Pedestrian |
|---|---|---|
| Accelerate / walk forward | `W` / `↑` | `W` |
| Brake / walk back | `S` / `↓` | `S` |
| Steer / strafe | `A` `D` / `←` `→` | `A` `D` |
| Look around | hold **right mouse button** + move mouse | same |
| Left / right indicator | `Q` / `E` | — |
| Horn | `H` | — |
| Cross now (assisted crossing) | — | `Space` |
| Stop / step back | — | `C` |
| "I see a hazard" | `F` | `F` |
| Pause menu | `Esc` | `Esc` |
| Recenter view | `R` | `R` |

Click the UI panels with the mouse. A gamepad also works for throttle, brake and steering.

### B. XR Interaction Simulator

The project includes the XRI *Starter Assets* and XR simulation settings. Turn on the XR Interaction Simulator through **Edit → Project Settings → XR Plug-in Management → (Standalone tab)** or the XRI simulator prefab to fake headset and controller input in the Game view.

### C. Meta Quest via Link (recommended for real testing)

1. Start **Meta Quest Link** on the PC, connect the headset (cable or Air Link) and enter Link mode.
2. Check that **Edit → Project Settings → XR Plug-in Management → Standalone (PC) tab → OpenXR** is ticked.
3. Under **OpenXR**, make sure the *Oculus Touch Controller Profile* / *Meta Quest Touch Plus* profiles are listed.
4. Press **Play** in Unity. The scene appears in the headset.

**Quest controller mapping**

| Action | Driver | Pedestrian |
|---|---|---|
| Accelerate | Right trigger | — |
| Brake | Left trigger | — |
| Steer / walk | Left thumbstick | Left thumbstick |
| Turn | — | Right thumbstick |
| Indicators | Left / right grip | — |
| Horn | B | — |
| Cross now | — | A |
| Stop / step back | — | B |
| I see a hazard | X | X |
| Pause | Menu (left controller) | Menu |
| Recenter | Right thumbstick click | Right thumbstick click |
| Look / blind-spot check | Turn your head | Turn your head |

---

## 7. Building and running on Meta Quest

1. **File → Build Profiles** (or *Build Settings*) → select **Android** → **Switch Platform**.
2. Confirm that the scene list has `RR_Bootstrap` first, followed by `RR_Tutorial`, `RR_ZebraCrossing`, `RR_SignalizedIntersection` and `RR_MotoTaxiJunction` (Build Everything does this).
3. **Edit → Project Settings → XR Plug-in Management → Android tab**: tick **OpenXR**, then under **OpenXR → Android** enable the **Meta Quest Support** feature group and the Meta Quest Touch controller profiles. These are already enabled in this repository.
4. **Player settings (Android)** already set in the project:
   - Scripting backend **IL2CPP**, target architecture **ARM64**
   - Minimum / target API level **34**
   - Application identifier `com.DefaultCompany.VRTemplate`. Change it under *Player → Other Settings* (for example `com.alu.roadready`) before distributing.
5. Connect the Quest over USB with Developer Mode on. It should appear under **Run Device**.
6. Click **Build And Run**. The APK is installed and launched on the headset.

To install an APK you already built:

```bash
adb devices                      # the headset must be listed as "device"
adb install -r Builds/RoadReady.apk
```

On the headset, the app appears under **Library → Unknown Sources**.

---

## 8. Configuration

All study and tuning settings live in ScriptableObjects in `Assets/RoadReady/Generated/Config/`.

### `RoadReadyConfig`

| Field | Default | Meaning |
|---|---|---|
| `studyName` | `RoadReady  Pilot` | Shown on the researcher setup screen. |
| `askSsqAfterEverySession` | `true` | Show the SSQ at the end of every session. |
| `ssqWarningTotalScore` | `40` | SSQ score at which the researcher is warned to stop. |
| `tutorialSkipDelaySeconds` | `3` | How long before the tutorial can be skipped. |
| `remoteBackend` | `None` | `None`, `FirebaseRealtimeDatabase` or `JsonPost`. |
| `remoteBaseUrl` | empty | e.g. `https://your-project-default-rtdb.firebaseio.com` |
| `remoteAuthToken` | empty | Database secret / bearer token. **Do not commit real secrets.** |
| `telemetrySampleRate` | `10` Hz | Trajectory logging rate for each attempt. |
| `voiceLines` | — | Optional audio clips for each instruction key (see `InstructionLibrary.cs`). Text is always shown, even without audio. |
| `driverTutorial`, `pedestrianTutorial`, `scenarios` | generated | The scenarios offered in the menu. |

### `ScoringConfig`

This asset holds every threshold of the rule engine, so the scoring model can be audited and reported:

- **Weights**: hazard detection 0.45, reaction time 0.25, rule compliance 0.30
- **Reaction time**: 0.75 s scores 100 %, 3.0 s scores 0 %
- **Penalties**: minor −10, major −25, critical −60. A collision caps the score at 40.
- **Grades**: ≥ 85 *Road Ready*, ≥ 70 *Almost there*, ≥ 50 *Needs practice*, below 50 *Unsafe — try again*
- **Driver rules**: speed tolerance +3 km/h for 1.5 s, give-way gap 3 s, following headway 1 s, blind-spot head yaw 55°
- **Pedestrian rules**: walking speed 1.3 m/s, safe-gap margin 1.5 s, look-both-ways yaw 45° within 5 s, roadway dwell grace 4 s
- **Time-to-collision**: below 1 s is a near miss (Unsafe), below 2 s is a close call (Borderline)

### Scenario and hazard definitions

- `Scenario_*.asset`: name, location, scene, supported perspectives, a briefing **for each perspective**, difficulty levels (traffic density, moto-taxi share, NPC speed, driver yield compliance, signal compliance, pedestrian density, time limit), the unlock score (default 80) and the hazard list.
- `Hazards/*.asset` (e.g. `zebra_car_no_yield`, `signal_red_runner`, `junction_hidden_moto`): hazard type, the required response, feedback text for safe / borderline / unsafe outcomes, and the **other-perspective insight** shown on the feedback screen.

Researchers can also override scenario parameters at runtime from the **Admin panel → Scenarios** tab. These overrides are stored on the device in the data folder.

---

## 9. Running a study session

This is the flow implemented in `RoadReadyApp.cs`:

```
Participant Setup (researcher) → Consent → Instructions → Tutorial (first time only)
   → Main Menu [Driver ⇄ Pedestrian] → Briefing → Scenario → Feedback
        ↳ Try again · Switch perspective · Next level · Menu
   → Finish session → SSQ (+ SUS in the post-test session) → Debrief
```

1. **Participant Setup (researcher):**
   - **New participant:** choose a *Perspective group* (Driver or Pedestrian), *Condition* (RoadReady training or Briefing only), *Age band* and *Driving experience*. The system enforces two rules: 16–17-year-olds cannot join the Driver group, and they need guardian consent.
   - **Returning participant:** pick their ID from the list. Toggle *This is the post-test session* for the follow-up visit.
2. **Consent:** all three checkboxes must be ticked. *I do not agree* deletes the record.
3. **Instructions:** shows the controls for both perspectives and plays the voice welcome.
4. **Tutorial:** runs automatically the first time for the participant's group. It is not scored.
5. **Main Menu:** toggle Driver / Pedestrian. Each scenario card shows its phase badge (**BASELINE**, **TRAINING** or **POST-TEST**), level and score history.
   - The first attempt at a scenario in each perspective is the **baseline** (Level 1).
   - In a post-test session every attempt is **post-test** (Level 1).
   - *Briefing-only* participants can only do baseline and post-test attempts, and get the **Safety briefing** button instead.
6. **Feedback:** score, grade, metrics, hazard and decision breakdown, tips, and the "Seen from the other side" insight.
7. **Finish session:** the participant answers the questionnaires and sees the debrief, then the app returns to setup for the next participant.

**Withdraw from study** is available on the Main Menu and the Pause menu. It permanently deletes that participant's data.

```

## 12. Project structure

```
RoadReady_Sim/
├── Assets/
│   ├── RoadReady/
│   │   ├── Scripts/
│   │   │   ├── Editor/            # RoadReadySetup (menu), ScenarioSceneBuilder, GreyboxKit
│   │   │   └── Runtime/
│   │   │       ├── Core/          # RoadReadyApp (session flow / state machine), enums, events
│   │   │       ├── Config/        # RoadReadyConfig, ScoringConfig, ScenarioDefinition, HazardDefinition, ComfortSettings
│   │   │       ├── Scenarios/     # ScenarioManager, ScenarioContext, TutorialDirector
│   │   │       ├── Player/        # DriverController, PedestrianController, PlayerRig, GazeTracker, input, vignette
│   │   │       ├── Rules/         # RuleEngine, DriverRules, PedestrianRules, SharedRules
│   │   │       ├── Hazards/       # HazardMonitor, HazardTrigger
│   │   │       ├── Scoring/       # ScoreCalculator, FeedbackGenerator
│   │   │       ├── Traffic/       # vehicle & pedestrian AI, signals, waypoints, spawners, zones
│   │   │       ├── Data/          # DataService, TelemetryWriter, CsvExporter, Questionnaires, RemoteSync
│   │   │       └── UI/            # RoadReadyUI, HudController, screens, world-space panels, fader, voice
│   │   ├── UI/
│   │   │   ├── UXML/              # one layout per screen (MainMenu, Briefing, HUD, Feedback, Pause, ...)
│   │   │   └── USS/RoadReady.uss  # design tokens and styles
│   │   └── Generated/             # created by "Build Everything": Config, Scenes, Prefabs, Materials
│   ├── Samples/                   # XR Interaction Toolkit & XR Hands samples
│   ├── XR/, XRI/                  # XR plug-in management, OpenXR and XRI settings
│   └── Scenes/                    # template scenes (not used at runtime)
├── Packages/manifest.json
└── ProjectSettings/
```

The UI is built with **UI Toolkit** on world-space panels (1 px = 1 mm, viewed at 1.2–1.8 m, minimum body text 28 px). Colours follow the Rwandan flag palette: blue `#00A1DE`, yellow `#FAD201`, green `#20603D`.

---

## 13. Designs

All designs are in Figma. Main file: **RoadReady VR – UI Walkthrough (Driver & Pedestrian)**.

| Design | What it shows | Link |
|---|---|---|
| **VR walkthrough visuals** | How the VR experience looks, screen by screen, for both the driver and the pedestrian perspective: landing menu, scenario selection, briefing, in-game HUD and feedback | [Open in Figma](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=9-175&t=kusLrvXvi8BtziO3-1) |
| **Activity diagram** | The system's activities from setup and consent, through tutorial, scenario attempts, evaluation and feedback, to questionnaires and debrief | [Open in Figma](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=32-7&t=kusLrvXvi8BtziO3-1) |
| **Interaction flowchart** | How the user moves between screens and actions (menus, perspective switching, retry / next level, pause, withdraw) | [Open in Figma](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=20-1950&t=kusLrvXvi8BtziO3-1) |

Supporting design files:

- **UI design file:** [RoadReady VR — Menus & In-Game UI](https://www.figma.com/design/XcT1w44eW1Eqfh07pfalSx)
  - *00 · Cover, Tokens & Logic*: design tokens, perspective colour coding (Driver = blue, Pedestrian = yellow) and a "logic at a glance" board
  - *01 · Landing Menu Flow*: setup, consent, instructions, Main Menu (Driver and Pedestrian), briefings, comfort, safety briefing, card states, modal
  - *02 · In-Game*: driver and pedestrian first-person views with HUD, tutorial states and pause menus
- **Logic diagrams (FigJam):** [Session Flow + Attempt Evaluation Logic](https://www.figma.com/board/NAIvm0EvorRQu18GyZQxuj)

---

## 14. Troubleshooting

| Problem | Fix |
|---|---|
| **"Scenario could not start"** message in the app | The scenario scenes are missing from Build Settings. Run **RoadReady → Build Everything**. |
| UI does not react to the controllers / mouse | Keep the `PanelInputConfiguration` that `RoadReadyApp` creates (input redirection = *Never*). Do not add a second uGUI EventSystem. |
| Package errors on first open | Make sure you use Unity **6000.3.21f1**. Close Unity, delete `Library/`, then reopen. |
| Headset not detected in Build And Run | Enable Developer Mode, accept USB debugging in the headset, and check that `adb devices` lists it. Try another USB-C data cable. |
| Black screen with Quest Link | Make sure Link is active before pressing Play and OpenXR is ticked on the **Standalone** tab. In the Meta Quest Link app, set OpenXR runtime to *Meta Quest Link*. |
| Motion sickness during tests | Turn on the comfort vignette, snap turning or seated mode in **Comfort settings**. Stop the session if the SSQ warning appears. |

