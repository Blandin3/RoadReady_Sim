# RoadReady VR — Dual-Perspective Road-Safety Simulator

**GitHub repository:** <https://github.com/Blandin3/RoadReady_Sim>

**🎬 Demo video:** [Watch on Google Drive](https://drive.google.com/file/d/1O6I6YymtZKBzn1oJbceCeHNuc3ViXTPJ/view?usp=sharing)

**📄 Technical report:** [Read on Google Drive](https://drive.google.com/file/d/1CuddSjThyYtb0ow4eihqtu_YsMYnh_Ae/view?usp=sharing)

**📦 Meta Quest APK download:** [Download page (Google Doc)](https://docs.google.com/document/d/1q_H5WB0voI-eR653iemiXECi_DvbHto_dNNohFID3qA/edit?tab=t.0). Install steps are in [§7](#7-building-and-running-on-meta-quest).

**Designs:** [VR walkthrough visuals](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=9-175&t=kusLrvXvi8BtziO3-1) · [Activity diagram](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=32-7&t=kusLrvXvi8BtziO3-1) · [Interaction flowchart](https://www.figma.com/design/6hhA6hbnkPZrWiG0gf0sWY/RoadReady-VR-%E2%80%93-UI-Walkthrough--Driver---Pedestrian-?node-id=20-1950&t=kusLrvXvi8BtziO3-1) (details in [§13](#13-designs))

## Description

RoadReady is a Unity virtual-reality simulator for practising road-safety decisions in streets modelled on **Kigali (Rwanda)**. Every scenario can be played from **two perspectives**:

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
14. [Testing](#14-testing)
15. [Performance](#15-performance)
16. [End-of-session email report](#16-end-of-session-email-report)
17. [Troubleshooting](#17-troubleshooting)

---

## 1. Features

| Area | What is implemented |
|---|---|
| **Dual perspective** | Each scenario supports Driver and Pedestrian. Progress, levels and history are tracked separately for each perspective. After any attempt, *Switch perspective* reloads the same scenario as the other road user. |
| **Scenarios** | Market zebra crossing, Signalised intersection, Reverse bay parking (driver only, scored out of 100, pass mark 80) and an endless Open road loop (driver only, timed), plus an unscored tutorial for each perspective. Every scenario ends on reaching the goal, a collision or the time limit. |
| **Automatic car** | Starts in neutral with the engine off and the handbrake up, and only moves once the engine is on (ON button or `O`), the handbrake is down (`B`) and D or R is selected (lever or `G`). See [§6](#automatic-gearbox-and-racing-wheel). |
| **Racing wheel** | Logitech G29 (and similar) as a pure input device: steering, accelerator and brake pedals, paddles (indicators) and buttons, through the Input System. The car is an automatic, so the game chooses the gears and ignores the H-shifter. Press `F3` in Play to calibrate and `F2` for a live monitor. |
| **School accounts** | A driving school registers and logs in (Firebase), registers members (codes `R-001`, `R-002`…), picks who trains and sees a dashboard. See [§8](#school-accounts-firebase). |
| **Menus in VR** | World-space UI Toolkit menus. Grab the blue bar above the pause menu with a controller ray to move the menu where you see best; it remembers the spot. |
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
| **Automated tests** | 52 EditMode unit tests (Unity Test Framework) for the safety score, parking pass mark, SSQ / SUS scoring, reaction-time normalisation, time-to-collision and data save / reload. See [§14](#14-testing). |

---

## 2. Requirements

### Software

| Tool | Version | Notes |
|---|---|---|
| **Unity Hub** | latest | <https://unity.com/download> |
| **Unity Editor** | **6000.3.21f1** (Unity 6.3) | Must match `ProjectSettings/ProjectVersion.txt`. |
| Unity module: **Android Build Support** | with **OpenJDK** and **Android SDK & NDK Tools** | Only needed to build for Meta Quest. |
| Unity module: **Windows Build Support (IL2CPP)** | optional | For a PC VR standalone build. An IL2CPP Windows build also needs **Visual Studio 2022 with the "Desktop development with C++" workload** (includes the Windows SDK); otherwise set the Standalone scripting backend to **Mono**. See [§17](#17-troubleshooting). |
| **Git** (+ Git LFS recommended) | any recent | To clone the repository. |
| **Firebase project** | free Spark plan | For school accounts only (Authentication + Firestore). See [§8](#school-accounts-firebase). |
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
   - builds the scenario scenes in `Assets/RoadReady/Generated/Scenes/`: `RR_Tutorial`, `RR_ZebraCrossing`, `RR_SignalizedIntersection`, `RR_Parking`, `RR_OpenRoad`
   - builds the bootstrap scene `RR_Bootstrap` (XR rig, UI, HUD, app controller)
   - writes the **Build Settings** scene list (bootstrap first, then the scenario scenes)
   - sets `RR_Bootstrap` as the **Play Mode start scene** and opens it
4. The Console prints: `[RoadReady] Build complete. Press Play…`

Other menu commands:

| Menu item | Purpose |
|---|---|
| `RoadReady → Rebuild Scenario Scenes Only` | Regenerate the scenario scenes after editing the scene builder. |
| `RoadReady → Use Imported Car For Driver (Bentley)` · `Add Mirrors To Player Car` · `Add PolyDrive Dashboard To Player Car` · `Set Up Vehicle Audio` | Optional car set-up: the imported car model, wing mirrors, the PolyDrive dashboard gauges and the engine sounds. |
| `RoadReady → Accounts → Build Account Scenes` / `Add Account Scenes To Build Settings` | Create the school-account scenes and add them to Build Settings (see [§8](#school-accounts-firebase)). |
| `RoadReady → Accounts → Test Accounts (Play From SchoolSetup)` / `Restore Game Start` | Make Play start from the account screens, or put it back on `RR_Bootstrap`. |
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
| Engine ON / handbrake | `O` / `B` | — |
| Gear lever (cycles D → N → R → P) | `G` | — |
| Move the seat | Numpad `8` `2` `4` `6` | — |
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
| Steer / walk | Grab the steering wheel with the grips, or left thumbstick | Left thumbstick |
| Turn | — | Right thumbstick |
| Gear lever | Grab it with a grip and move it to P / R / N / D | — |
| Move the seat | Right thumbstick | — |
| Indicators | Y (left) / A (right) | — |
| Horn | B | — |
| Cross now | — | A |
| Stop / step back | — | B |
| I see a hazard | X | X |
| Pause | Menu (left controller) | Menu |
| Recenter | Right thumbstick click | Right thumbstick click |
| Look / blind-spot check | Turn your head | Turn your head |

### Automatic gearbox and racing wheel

The car is an **automatic**. Every drive starts in **neutral with the engine off and the handbrake up**; it only moves once all three are dealt with: press the ON button (or `O`), push the handbrake down (or `B`), and select D (or R) with the gear lever (or `G`, or R2 / L2 on the wheel), then use the accelerator and brake.

With a **G29**: the wheel is purely an input device; the game handles the transmission (automatic), so the H-shifter is not used and gears come from the car's gear lever (`G`, or R2 / L2 on the wheel). The G29 finds its own centre with its lock-to-lock spin at power-up; the game then finds the steering axis the first time you turn the wheel left and right. Press **`F3` in Play** to calibrate the wheel, accelerator and brake (`F2` shows every axis and button live). The first drive with an uncalibrated wheel opens the calibration by itself; `F4` swaps the accelerator and clutch if they are the wrong way round. `R2` / `L2` move the lever.

---

## 7. Building and running on Meta Quest

1. **File → Build Profiles** (or *Build Settings*) → select **Android** → **Switch Platform**.
2. Confirm that the scene list has `RR_Bootstrap` first, followed by `RR_Tutorial`, `RR_ZebraCrossing`, `RR_SignalizedIntersection`, `RR_Parking` and `RR_OpenRoad` (Build Everything does this), then the account scenes `SchoolSetup`, `Home`, `MemberRegister`, `StartTraining` and `Dashboard`.
3. **Edit → Project Settings → XR Plug-in Management → Android tab**: tick **OpenXR**, then under **OpenXR → Android** enable the **Meta Quest Support** feature group and the Meta Quest Touch controller profiles. These are already enabled in this repository.
4. **Player settings (Android)** already set in the project:
   - Scripting backend **IL2CPP**, target architecture **ARM64**
   - Minimum / target API level **34**
   - Application identifier `com.DefaultCompany.VRTemplate`. Change it under *Player → Other Settings* (for example `com.alu.roadready`) before distributing.
5. Connect the Quest over USB with Developer Mode on. It should appear under **Run Device**.
6. Click **Build And Run**. The APK is installed and launched on the headset.

A ready-made APK is linked from the [APK download page](https://docs.google.com/document/d/1q_H5WB0voI-eR653iemiXECi_DvbHto_dNNohFID3qA/edit?tab=t.0). To install that APK, or one you built yourself:

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
- `Hazards/*.asset` (e.g. `zebra_car_no_yield`, `signal_red_runner`, `parking_pedestrian_behind`): hazard type, the required response, feedback text for safe / borderline / unsafe outcomes, and the **other-perspective insight** shown on the feedback screen.

Researchers can also override scenario parameters at runtime from the **Admin panel → Scenarios** tab. These overrides are stored on the device in the data folder.

### School accounts (Firebase)

A driving school registers once, then logs in on the headset and stays logged in until **Log out school**. Flow: `SchoolSetup` (register / log in) → `Home` → **Start training** (pick a member, opens the game), **Register new member**, **Dashboard** (asks for the password again, then shows member counts). Screens are world-space canvases with an on-screen keyboard (letters and a `#+=` symbols page), so they work in a headset and with the mouse. The whole experience starts here: Play and the first scene of a build are `SchoolSetup`; **Start training** hands the chosen member to the game, which skips its splash and name page, and finishing a session returns to Home.

One-time Firebase set-up:

1. In the [Firebase console](https://console.firebase.google.com/) create a project and register the app. Put the downloaded `google-services.json` in `Assets/` and `google-services-desktop.json` in `Assets/StreamingAssets/`.
2. **Authentication → Sign-in method**: enable **Email/Password**.
3. **Firestore Database**: create a database, then paste [`firestore.rules`](firestore.rules) into the **Rules** tab and **Publish**. Without this, registering fails with *Missing or insufficient permissions*.
4. In Unity run `RoadReady → Accounts → Build Account Scenes`, then `Add Account Scenes To Build Settings`, and drag `SchoolSetup` to the top of the scene list if the app should start there.

Data lives at `schools/{uid}` and `schools/{uid}/members/{id}`. Only the logged-in school can read or write its own data.

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

---

## 10. Data storage and export

- **Local:** crash-safe JSON for participants and attempts, plus a per-attempt telemetry file (NDJSON, `telemetrySampleRate` Hz). In the Editor use `RoadReady → Open Data Folder`; on Quest pull the files with `adb`.
- **Export:** CSV (attempts, hazards, participants) from the **Admin panel** or `RoadReady → Export CSV (editor data)`. Participants can also get a PDF report by email address (stored locally).
- **Remote (optional):** `remoteBackend` in `RoadReadyConfig` syncs to Firebase Realtime Database or a JSON POST endpoint. Never commit real tokens.
- **Anonymous:** only IDs such as `RR-7KQ2TX` are stored. Withdrawing deletes that participant's data.

---

## 11. How the simulation works

- **Car:** a deterministic kinematic model driven from `Update` (no physics jitter), left-hand drive, an automatic gearbox in `DriverController`.
- **Rules:** `RuleEngine` (driver, pedestrian and shared rules) turns behaviour into Safe / Borderline / Unsafe decisions with a severity.
- **Hazards:** `HazardMonitor` checks head gaze and the response to each scripted hazard.
- **Score:** `ScoreCalculator` combines hazards and rules (parking uses points off 100 instead); `FeedbackGenerator` writes the report.
- **Ending an attempt:** reaching a goal zone, a collision, or the time limit. The open road has no goal and counts as a drive once at least 800 m is covered.

---

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
│   │   ├── Tests/EditMode/        # unit tests (Unity Test Framework, Editor only, never in a build)
│   │   ├── Accounts/              # school login, members, dashboard (Scripts, Editor builder, Scenes)
│   │   └── Generated/             # created by "Build Everything": Config, Scenes, Prefabs, Materials
│   ├── Firebase/, Plugins/        # Firebase Unity SDK (Auth, Firestore) and its native plugins
│   ├── PolyDrive - FREE/          # vehicle models, used for the car dashboard
│   ├── Samples/                   # XR Interaction Toolkit & XR Hands samples
│   ├── XR/, XRI/                  # XR plug-in management, OpenXR and XRI settings
│   └── Scenes/                    # template scenes (not used at runtime)
├── firestore.rules                # Firestore security rules (publish in the Firebase console)
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

## 14. Testing

The scoring, questionnaire and data logic is covered by **52 EditMode unit tests** in `Assets/RoadReady/Tests/EditMode/` (assembly `RoadReady.Tests.EditMode`, Editor only). Each data test uses its own temporary folder, so real study data is never touched.

| Test class | Tests | What it checks |
|---|---|---|
| `ScoreCalculatorTests` | 21 | Composite score weights (and that scaling them changes nothing), minor / major / critical penalties, rule component floored at 0, collision cap (40), time-out cap (50), grade bands (85 / 70 / 50), reaction-time normalisation (0.75 s = 100 %, 3.0 s = 0 %, clamped) |
| `ParkingScoreTests` | 9 | Points off 100, pass mark 80 (exactly 80 passes, 75 does not), late or missing brake for the pedestrian (−20), hitting the pedestrian scores 0, time-out capped and failed |
| `QuestionnaireTests` | 9 | SSQ subscales and total (Kennedy et al., 1993; all "Severe" = 235.62) and SUS (Brooke, 1996; neutral = 50, best = 100, worst = 0) |
| `DataServiceTests` | 8 | Participants and attempts survive a reload, anonymous `RR-XXXXXX` IDs, no duplicate attempts, tutorial and aborted runs excluded from progress, no temp files left behind, withdrawal deletes all data |
| `KinematicsTests` | 5 | Time-to-collision (head-on, near miss, moving apart) and signed head yaw |

**Latest result:** 52 / 52 passed, 0 failed (Unity 6000.3.21f1).

**Run them in the Editor:** **Window → General → Test Runner → EditMode → Run All**.

**Run them from the command line** (close the Editor first):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe" -batchmode -projectPath . `
  -runTests -testPlatform EditMode -assemblyNames RoadReady.Tests.EditMode `
  -testResults TestResults\editmode.xml -logFile TestResults\editmode.log
```

Exit code `0` means every test passed; the results are in the NUnit XML file.

---

## 15. Performance

The target is **90 fps on the headset** (a frame budget of about 11.1 ms).

The CPU load is designed to stay modest. The car is a kinematic model rather than a physics simulation, the rule engine, gaze tracker and hazard checks do small calculations each frame, and traffic and pedestrians follow fixed waypoint paths without pathfinding. The rendering is set up with standalone VR in mind: HDR is off, and the *Performance* URP setting turns off additional lights and keeps the shadow distance short. Over Quest Link on a PC, the drive is expected to hold 90 fps comfortably. On a standalone Quest, the main risk is GPU time: the city scenes contain many imported models, and 4× MSAA, full render scale and a 4096 shadow map all cost time on a mobile chip. If measurements fall short, the first things to try are a lower render scale, foveated rendering, a smaller shadow map, static batching / GPU instancing for repeated props, and capping the number of active traffic agents.

**How to measure** (drive the same zebra-crossing route for about 60 s in each configuration):

| Configuration | Tools | Read off |
|---|---|---|
| **Unity Editor** (no headset) | **Window → Analysis → Profiler** (CPU, GPU, Memory modules) and the Game view **Stats** | Average FPS, CPU and GPU frame time, *Total Used Memory* |
| **Quest via Link** (PC) | A **Development Build** with *Autoconnect Profiler* (Unity Profiler) plus the **Oculus Debug Tool** (`C:\Program Files\Oculus\Support\oculus-diagnostics\OculusDebugTool.exe` → *Performance HUD → Performance Summary*) | App FPS, app GPU time, dropped frames, CPU and memory from the Profiler |
| **Quest standalone** | An Android **Development Build** and the **OVR Metrics Tool** (Meta Horizon Store) with its overlay and CSV recording | FPS, CPU and GPU frame time, memory |

The G29 wheel is a USB device read through Windows, so it works in the Editor and over Link but not on a standalone Quest.

---

## 16. End-of-session email report

When a learner presses **Done** on the debrief screen, the driving school (the school that is logged in) gets an email with the learner's name and total marks. Nothing is sent for a session with no scored attempts (the screen says "No scores to report").

**What the email contains:** school name, learner name, date and time, session length, **total marks**, a table of every scored attempt (scenario and score) and the line "Simulated training result sent by RoadReady." Total marks = *the average of the best score per scenario and role, rounded* ("Overall score: average of best attempt per scenario"). No participant code or other personal data is sent. Every typed value is cleaned and HTML-escaped.

**How it works:** the game announces finished attempts and the end of the session (`SessionHooks`). The code in `Assets/RoadReady/Reports/` listens (the game never references it), builds the report, shows "Sending report..." and then either "Report sent to your driving school" or "No internet. The report will be sent later.". The learner never waits more than 5 seconds. Each session has a unique id, so tapping Done twice or retrying never sends two emails. The email always goes to the school's own login email, never to an address typed in the app.

**Known limits:** a headset taken off in the middle of a session is not reported; a learner who goes back to Home without pressing Done (or whose session is replaced by the next learner) is not reported; sending needs internet.

### Option B: free Google Apps Script relay (default)

Needs only a Gmail account. No Firebase billing, and no email password in the project or the build.

1. Open <https://script.google.com> signed in to the Gmail account the emails should come **from** (a separate address such as `roadready.reports@gmail.com` is best), and click **New project**.
2. Delete the sample code and paste in the whole of [`docs/email-relay/Code.gs`](docs/email-relay/Code.gs).
3. At the top of the script, change the three settings:
   - `SHARED_SECRET`: a long random string (24+ characters). Keep a copy.
   - `ALLOWED_RECIPIENTS`: the login email of every driving school that may receive reports, for example `['school@example.com']`. **A school not in this list gets nothing.**
   - `SENDER_NAME`: the name people see.
4. Click **Deploy > New deployment > Web app**. Set *Execute as*: **Me**, *Who has access*: **Anyone**. Click **Deploy**, accept the Google permission prompts (it needs to send mail as you), and copy the **Web app URL** (it ends in `/exec`).
5. Test the deployment: open that URL in a browser. You should see `{"ok":true,"message":"RoadReady email relay is running."}`.
6. In Unity: **Assets > Create > RoadReady > Report Settings**. Name it exactly `ReportSettings` and move it into `Assets/RoadReady/Reports/Resources/` (create the `Resources` folder if needed). Select it and fill in **Email Relay Url** (the `/exec` URL) and **Email Relay Secret** (the same secret as in step 3). Leave *Delivery* on `AppsScript`.
7. This file is listed in `.gitignore` on purpose. **Never commit it**, because it holds the secret.

If you change `Code.gs` later, use **Deploy > Manage deployments > Edit > New version**; the URL stays the same.

**Security note:** the secret is stored inside the build, so someone determined could extract it from the APK. That is why the script also checks the recipient allowlist, builds the email itself from plain fields (it never sends HTML supplied by the app) and refuses a session id it has already sent. The worst case is a fake report to a school that is already on your list. A normal Gmail account can send about 100 emails a day.

### Option A: Firebase "Trigger Email" extension (needs the Blaze plan)

Not needed if you use Option B. In `ReportSettings` set *Delivery* to `FirestoreMail`; the app then writes the report to the `mail` collection and the extension sends it.

1. Upgrade the Firebase project to the **Blaze** plan and add a **budget alert** (Billing > Budgets).
2. Install the extension **Trigger Email from Firestore** (Extensions): collection `mail`, your SMTP connection URI (for Gmail, an App Password) and a default From address. The SMTP password stays in Firebase, never in the app.
3. Publish [`firestore.rules`](firestore.rules) (it includes a `/mail` rule: a school may only create a document addressed to its own login email, with only the keys `to` and `message`, within size limits, and nobody can read, change or delete it from the app).

### Testing it

1. **Relay on its own:** open the Web app URL in a browser (step 5 above).
2. **Whole flow:** in Unity press Play, log in as a school whose email is in `ALLOWED_RECIPIENTS`, start training for a member, finish one scored scenario, press **Finish session**, answer the questionnaire and press **Done**. You should see "Sending report..." and then "Report sent to your driving school". Check the school's inbox (and the spam folder) for **"RoadReady session report: {Name} - {marks}"**.
3. **No double email:** press Done twice quickly. Only one email should arrive.
4. **No scores:** start a session and press Finish session without finishing any scenario. You should see "No scores to report" and no email.
5. **No internet:** turn the internet off and press Done. You should see "No internet. The report will be sent later." and the Unity Console shows why.
6. If nothing arrives, open the script in Apps Script and check **Executions** for errors.

---

## 17. Troubleshooting

| Problem | Fix |
|---|---|
| Windows build fails with **"Could not set up a toolchain for Architecture x64 … IL2CPP"** | IL2CPP needs a C++ compiler. Either set **Edit → Project Settings → Player → Windows tab → Other Settings → Scripting Backend** to **Mono**, or install Visual Studio 2022 with the **Desktop development with C++** workload (it includes the Windows SDK), then restart Unity. |
| **"Scenario could not start"** message in the app | The scenario scenes are missing from Build Settings. Run **RoadReady → Build Everything**. |
| UI does not react to the controllers / mouse | Keep the `PanelInputConfiguration` that `RoadReadyApp` creates (input redirection = *Never*). Do not add a second uGUI EventSystem. |
| Package errors on first open | Make sure you use Unity **6000.3.21f1**. Close Unity, delete `Library/`, then reopen. |
| Headset not detected in Build And Run | Enable Developer Mode, accept USB debugging in the headset, and check that `adb devices` lists it. Try another USB-C data cable. |
| Black screen with Quest Link | Make sure Link is active before pressing Play and OpenXR is ticked on the **Standalone** tab. In the Meta Quest Link app, set OpenXR runtime to *Meta Quest Link*. |
| Registering a school says **"Missing or insufficient permissions"** | Publish [`firestore.rules`](firestore.rules) in Firebase → Firestore → Rules and enable Email/Password sign-in. |
| Play mode exits immediately with the headset connected | The Meta runtime ended the OpenXR session. Be inside Link before pressing Play, set *Play Mode OpenXR Runtime* to **Oculus**, close SteamVR and other VR apps, and set Meta Quest Link as the active OpenXR runtime. Without a headset, Play works as usual. |
| The car does not move | It starts in neutral with the engine off and the handbrake up: press the ON button (`O`), push the handbrake down (`B`) and select D or R (lever or `G`). |
| Wheel steering or pedals feel wrong | Press `F3` in Play to calibrate, `F4` to swap the accelerator and clutch if they are the wrong way round, `F2` to see the live axes. |
| Account menu text boxes do not type | Click the box, then use the on-screen keyboard. |
| Motion sickness during tests | Turn on the comfort vignette, snap turning or seated mode in **Comfort settings**. Stop the session if the SSQ warning appears. |

