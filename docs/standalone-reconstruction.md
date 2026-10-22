# Standalone reconstruction evidence

## Target and analysis boundary

The supplied `GAME86.EXE` is LZEXE-packed. `tools/inspect_game.py` removes that
packing and any inner EXEPACK layer. `tools/recover_gameplay.py` independently
unpacks the original and checks instruction anchors before exporting tables.
It records SHA-256 identities for both the packed executable and load image.
The output is `artifacts/reconstruction/gameplay.json`.

The analyzed packed executable SHA-256 is
`e4ca6f41aa770eb6989a9357ba048cf1f9b657425f117a351285010d5853636c`.

All CS offsets below refer to the unrelocated load image. The initial DS is
`0x2559`; a DS offset maps to image offset `0x25590 + offset`. Recovery began with
byte inspection and the existing 16-bit disassembly. REA 6.5.0 now supplies actual
Ghidra 12.1.4 function dossiers and pseudocode through Ubuntu WSL, using isolated
Node 24.15.0 and Temurin JDK 21.0.12.1+1 under ignored `.tools/rea-linux`. Downloads
were verified against published SHA-256 values. No MCP registration was changed.
The initial Windows-provider failure remains in `artifacts/rea-entity-dispatch.json`.

The [REA Evidence index](rea-evidence-index.json) records full retained Evidence
records with their identifiers, file hashes, target hashes and source provenance.
The full records are under ignored `artifacts/reconstruction/rea-*.json` and include
assembly, body ranges, call relationships, pseudocode and provider limitations.
The reset routine was analyzed directly in the unpacked original. Ghidra does not
automatically discover every indirect target: for selected handlers, analysis-only
copies change the MZ header's initial IP to the recovered entry. The tool
`tools/prepare_rea_entry.py` verifies identical load-image and relocation bytes and
writes a provenance sidecar. These copies must not be executed.

Ghidra maps original CS offset `n` to address `0x10000 + n`; its relocated DS is
`0x3559`. Uninitialized register context in hinted entries produces stack artifacts
and uncertain register/flag data flow in pseudocode. The recovered assembly and
original bytes are authoritative when these artifacts conflict with decompilation.
See [REA's DOS adapter documentation](https://github.com/morluto/rea/blob/main/docs/ghidra-dos.md).

`tools/index_rea_evidence.py` regenerates the index and verifies source, derived
target and full-record hashes. The current index contains 235 records, including
herd rescue, normal play, firing, bullet movement/collision, shared death,
hunters, projectile bursts, axis pursuit and the homing collectible.
GNU objdump 2.44 supplements REA for small routines;
bounded decoding starts at each known entry rather than walking through preceding
embedded tables. Ghidra's unresolved jump tables remain explicitly unresolved in
the retained dossiers; their destinations were recovered from original table words.

## Recovered structures

| Structure | Original location | Observed behavior |
| --- | --- | --- |
| Entity pool | DS:48c1 | 64-byte records; state word negative means inactive |
| Pool reset | CS:4307 | Initializes 100 records, preserving unspecified words |
| Update loop | CS:432b | Processes 70 records in ascending memory order |
| Update dispatch | DS:1fd2 | 46 pointers, indexed by low byte of state |
| Render loop | CS:4374 | Processes 70 records |
| Render dispatch | DS:202e | 19 pointers, indexed by low byte of word at record +6 |
| Child allocator | CS:4f93 | First free among 70; partial initialization, caller sets state |
| Random table | DS:6ac9 | 512 bytes; initialized as two copies of 0..255 |
| Random shuffle | CS:007b-00a9 | 4000 pair swaps with timer samples from CS:e616 |
| Random next | CS:e4de | Reads table[index], advances modulo 512 |
| Wave pointer table | DS:2bda | 100 script pointers, followed by a zero word |
| Script commands | CS:0245 | Eight negative prefix commands |
| Spawn dispatch | DS:2ca4 | 41 pointers, indexed by script spawn kind |
| Spawn stream | CS:3d09 | (kind, count) pairs; count=255 terminates |
| Bullet pool | DS:68c9 | 15 records, each 16 bytes; negative X is inactive |
| Damage dispatch | CS:0c04 | 18 handler pointers, indexed by entity word +3e |
| Chaser damage outcomes | CS:1047 | 12 pointers, selected after the hit counter expires |
| Player trails | DS:6cc9 and 70c9 | Separate 128-record histories, eight bytes per record |

The same pool is reused by other screens. Gameplay update capacity must not be
inferred from reset capacity. Behavior IDs are states and have not all been mapped
to named enemy species.

## Source ports currently present

| Original code | Portable implementation | Coverage |
| --- | --- | --- |
| CS:4307 | `EntityPool.Reset` | Writes the five original reset fields across 100 records |
| CS:43dc | `SpawnDelay` | Full lifecycle routine; dispatch states 0, 4, 7, 10 |
| CS:43c9 | `ExpandingDeath` | Full local routine; state 2; returns release for global counter |
| CS:4ec8 | `TimedDeath` | Full local routine; state 9; returns release for global counter |
| CS:4b49 | `PlayerContact` | Contact bits; preserves different player status conditions |
| CS:4ae9-4b42 | `AimChase` | Shared steering arithmetic used by the ported chase actor |
| CS:4e7f | `MoveAndBounce` | Shared movement; original bottom collision forces Y=187 |
| CS:4f93 | `PrepareChild` | Partial record initialization; caller owns state/free-count changes |
| CS:e4de, 007b-00a9 | `RandomSequence` | Table cycling/shuffle; timer input is caller supplied |
| CS:0229, 0455, 3d09 | `WaveDefinition`, `OriginalWaves` | All 100 wave data streams; contiguous boundaries verified |
| CS:0392-0454 | `WaveParameters.Apply` | Eight prefix commands, signed/scaled values and sound cue 0x16 |
| CS:1c67 | `SpawnContext.Position` | Original rejection sampling, one-sided first-player Y exclusion and final Y clamp |
| CS:1d8d and recovered spawn entries | `SpawnContext.Create` | Common allocation and local factories for kinds 0-15, 17-20, 23-25, 28, 29, 31, 33, 38; full scripted pool falls through to record 70 |
| CS:4df3-4e7e | `HerdMotion.Step` | Herd movement/animation, now called from the complete local rescue handler |
| CS:4ac9-4b48 | `ChaseActor.Step` | Local state-3 update; freeze, contact, loop parity, timer and stepped movement |
| CS:43f7-4464 | `ActorContext.PlayerHit` | Status/phase gates and original sound-register events; audio playback still absent |
| CS:43f1, 3cea | `ActorDispatcher.Step` | Local contact-only state 1 and bouncing projectile state 15; lifecycle free counters |
| CS:4c46-4df2 | `HerdActor.Step` | Rescue, final-herd bonus, separate trail assignment, popup and hot-bullet timer |
| CS:4ed7, 4feb, 504f, 509b | `SpawnerActor` | Moving parent, spawning child and wall-released shot; byte timers, quotas and selected-player aiming |
| CS:0335 | `DecimalScoreboard` | Two eight-digit scores, inclusive counters, repeat bonus and decimal carry behavior |
| CS:0501, 1961-19ce, 4bfb | `TrailHistory` | Position-only recording, byte-offset wrap and state-6 follower animation |
| CS:16c1-1a00 | `PlayerMotion` | Human movement, firing lock, twin-stick aim and two-player controls; droid/demo AI excluded |
| CS:0981-0c03 | `BulletSystem.Fire` | Nine-tick eligibility cadence, shared slots and alternating three-way spread |
| CS:0c28-0da9 | `BulletSystem.Step` | Movement, ricochet lifetime, parity collision, piercing and bomb sweep |
| CS:105f and recovered damage entries | `DamageRules` | All twelve chase-death outcomes; shared kill/scoring, knockback, spawner pickups, splitting, fixed impulse and health damage. Damage entries 8, 9, 13 and 17 remain explicit failures |
| CS:495f, 39e9, 2457, 2407, 23a7 | `HunterActor` | Basic/timed/proximity/producer hunters; herd claiming, conversion, killing and alarm actions; preserves unscaled basic-target offset |
| CS:3b26, 3c09 | `HunterProjectiles` | Four-shot and reverse-pattern bursts; original allocation boundaries and partially initialized records |
| CS:220b, 4336-435b | `AxisChaseActor` | Per-record selected-player axis pursuit, random threshold, four history pairs and wall bounce; no freeze gate |
| CS:203f, 20f7, 2112, 212d | `CenterSpawnerActor` | Center-column parent, expiring shots, bouncing offspring and ten-child death replacement; original partial allocation and counter effects |
| CS:38a2 | `FixedMotion` | Signed 16.16 acceleration/velocity/position, original word comparisons, carry and exact wall rejection |
| CS:35b4 | `FixedShooterActor` | Packed firing clock, early horizontal shots and later directional children; selected-player facing |
| CS:3441, 3490, 3d71, 3dac | `DirectionalActor` | Eight-direction initialization, byte timer and incremental turning; shared by late shots and death outcomes |
| CS:2f87, 3014 | `ContactEmitterActor` | Contact-deactivated emitter with remote shot origins; falling actor with fractional acceleration and return phases |
| CS:1232, 1108, 11e2, 388a | `FixedSplitActor` | Three sizes, fixed-motion update and two/four-child splitting; preserves SI projectile writes and full-pool kind write |
| CS:382d, 37fe, 3860 | `BeamActor` | Horizontal/vertical alignment damage, packed activation clocks and fixed movement; raw distance bits retained for damage routing |
| CS:4779, 46b0, 44ab | `SpriteFrame` beam renders | Hard-coded head, original pixel spans using palette index 1, then actor body |
| CS:27e7, 2865, 2ae1 | `AxisDashActor`, damage handler 11 | Alternating-axis fixed-point movement, signed decay, boundary retargeting and hit pause/health |
| CS:24ef, 256e, 25ae, 25f1, 10b0 | `MutableSpawnerActor` | Shared-ramp initialization, child quota/timer, harmful contact and persistent bouncing actor |
| CS:123c/1241 with SI=256e | `WaveScriptStore` | Confirmed writes into wave-22 script data; decoded on subsequent wave starts and retained across retries |
| CS:1416, 148d, 13a6, 12c6 | `DeathTransforms` | Cardinal hazard, move-before-contact, reflected shot and in-place conversion; original hostile/free counter differences |
| CS:2b2d, 2d6e, 2d5a, 12ed, 2da7 | `RecoilActor` | Acceleration growth, facing/animation, retained fractional fields, damage-created variant and low-word impulse |
| CS:0eb8, 0f10-1021, 37c2 | `PowerupActor` | Collectible messages, ten pickup effects, homing/expiry and harmful contact |
| CS:1562-1a38, 1d40, 3d09 | `GameSimulation` | Connected normal-human frame and spawn scheduler; elapsed-time pacing remains unverified |
| CS:0455-0615, 1ced-1d3f | `StartWave`, `RetryWave`, `WaveDefinition.FromSurvivors` | Selective reset, retained globals/records and first-encounter survivor grouping |
| CS:0625-0815 | `GameCampaign` | Normal clear/retry/lives/continue flow; audio host must signal warp completion; alternating-player snapshots excluded |

Spawn counts represent one fewer than the number created: CS:1d8d checks for a
negative count, then CS:1db1 decrements before creating an entity. Wave 1 contains
one instance of spawn kind 0 and 31 of kind 1. This does not identify the species
of all spawn kinds. There are 3,465 scripted initial instances across the 100
waves, excluding enemies generated during play. Every script's decoded end matches
the next pointer; all terminate with `ff ff`. Modes observed are 0, 1 and 2.

Wave parameters carry over when omitted. Command 7 takes three bytes; command 3
consumes a fourth byte without using it. Recovered parameter words retain their
DS identifiers until their downstream meaning is established. All eight commands
are locally ported; full wave setup, spawn handlers and their consumers are not.

The first two spawn kinds are identified from their handlers: kind 0 creates the
herd member (delayed state 4, then state 5); kind 1 creates a chaser in state 3.
Chasers move only when their retarget timer expires. Signed arithmetic shifts
quantize negative movement differently from positive movement; replacing this with
normalized continuous vectors would change the original behavior. State-3's local
update and death-contact helper have REA dossiers/pseudocode cross-checks. Herd
rescue, firing, the normal frame path and shared death now also have REA dossiers.
State-1/15 and individual pickup effects additionally use manual disassembly.

Rescued animals remain active as state-6 trail followers. The normal rescue is
500 points; the final animal adds 1,000 points. The secondary-player branch can
award 250 points in the original two-player flag combination. Scores use decimal
digits and inclusive counters; an all-digit overflow retries the increment, giving
1 after 99,999,999 + 1. A ten-thousand carry queues the extra-life collectible;
it does not directly increment lives in the score routine.

Player bullets share 15 slots. They fire every nine eligible ticks after both
status words reach zero and spawning finishes. Three-way bursts share those slots,
collision tests alternate by slot/frame parity, and piercing still processes only
the first hit per bullet per tick. Ricochet retains a 60-tick counter that ordinary
shots do not decrement. The adjacent-table scan reduction is checked against the
original projectile-sprite bytes by the recovery tool.

The library contains no DOS code blob or instruction interpreter. Signed word
storage preserves timer underflow, inactive sentinels and high-byte state metadata.
Invalid movement inputs that would loop forever in the original fail explicitly.
`StepLifecycle` handles only lifecycle states. `ActorDispatcher` now covers all 46
dispatch entries and rejects unknown IDs. `GameSimulation` connects the
human-play routines and exposes outcomes. `GameCampaign` handles normal progression,
survivor retries, life consumption and continue/game-over states. The source clear
loop requests 11 display frames with the normal update callback still selected,
followed by 48 and 47 vertical-retrace wipe steps. Interrupt and display callbacks
are now independent, with outcome resolution after drawing.
Original wipe graphics, menu/high-score presentation,
alternating-player snapshots and full runtime timing parity remain unfinished. Warp waits
for audio-host completion before advancing five waves and awarding the 50,000-point
base bonus. The sampled-audio host now supplies active-voice status after the
source minimum two retraces; the earlier Unity stop on this path has been removed.

Wave reset reuses the actor context, entity pool and random table. It resets only
the original written fields, retaining scores, hot bullets, aim, firing cooldown,
random cursor and untouched words in reused records. Retry grouping scans only
the first 70 records using the original signed low-byte state test and excludes
negative spawn-kind records. It combines matching kinds in first-encounter order,
with at most 31 groups. Unspawned script groups are not reintroduced. A tooling
start at a later wave reconstructs carried prefix values without pretending to
replay all prior gameplay.

Static timer evidence now establishes separate clocks. CS:e543 programs PIT
channel 0 in mode 3 with divisor `0x5cec` (23,788), and installs CS:3de4 as IRQ 0.
The ISR increments the input-frame counter at CS:3e08, invokes the selected
gameplay callback through DS:39e4 at CS:3eb7, and chains the previous BIOS timer
using the same divisor accumulator at CS:3ec2. On the standard IBM clock of
1,193,182 Hz, this implies about 50.158988 interrupts per second; that frequency
is inferred from the divisor and the [DOSBox-X IBM PIT clock definition](https://raw.githubusercontent.com/joncampbell123/dosbox-x/master/include/timer.h).
The far display entry at `e7b:013f` instead draws and waits for vertical retrace at
CS:e5f3. IRQ input counters continue during wipe loops even when gameplay uses its
no-op callback. `OriginalTiming` exports the verified divisor and the referenced
IBM clock; `GameClock` delivers IRQ events at their ratio and display events on an
independent, currently provisional 70 Hz clock. Events are processed in timestamp
order with deadlines derived from event counts. A host frame can deliver zero,
one or multiple events without changing that ordering.

`GameCampaign.TickInterrupt` polls input-frame counters in every phase, updates
actors only when their callback is selected, and keeps continue timers on IRQ
ticks. `TickDisplay` counts the 11 clear displays and both wipe loops, advances
popup lifetime through its renderer callback, and resolves outcomes after drawing.
The new-scene draw gate suppresses actor updates until the first display. Pause
stops actor updates while input counters and drawing continue; full original pause
key/palette behavior remains separate. Mobile application suspension freezes both
clocks and discards the first resume delta. Hardware initial phase, exact retrace
rate, instruction-level interrupt interleaving and overload behavior remain
unverified; this is not a claim of cycle-identical replay.

The moving spawner's initial wait counter of 300 precedes a packed-byte spawn cadence.
Spawn kind 33 uses the same actor states with sprite base 290, wait counter 30,
cadence reload 48 and 20 children instead of base 60, wait 300, cadence 96
and three children for kind 3.
After that wait, the high byte decrements as a signed byte and reloads from the
low byte on underflow. Its three-child quota decrements before allocation, so a
full pool still consumes an attempt. Parent animation and contact continue during
freeze, while its movement and spawning stop; child states 12 and 13 have no
freeze gate. Child retargeting and bouncing occupy different ticks. Shots use
strict wall bounds and can commit X before expiring on the Y boundary. The damage
handler retains its score popup and selects a pickup using the original 60/160
freeze-dependent threshold and the separate 140 comparison.

## Validation performed

1. The .NET Standard 2.1 core builds with zero warnings and zero errors.
2. `dotnet run --project standalone/Checks/Llamatron.Checks.csproj` passes the
   selected record/lifecycle/movement/contact/allocation/random/configuration
   boundary vectors and decodes all 100 original waves. Checks also cover first-wave
   spawn records/random consumption, herd frame cadence, chase timer movement,
   simultaneous-contact priority, hit-event ordering and projectile release counters.
   Integration checks execute the first wave's eight obstacles and 32 scripted
   instances in 43 logic ticks, then exercise a real shot through collision,
   damage, a 40-point award and same-frame death expansion. No DOS game files or
   emulator are read or launched by this C# core/check execution.
   Additional vectors cover spawner random consumption, child allocation and
   free/hostile counters, high-byte timer reload, freeze asymmetry, full-pool quota
   consumption, partial-axis shot release and freeze-dependent pickup selection.
   Campaign vectors cover render-before-retry order, survivor grouping, selective
   reset and retained globals, one index advance, both wipe loop lengths, actual
   second-wave spawning, the wave-100 sentinel, continue rollover/input priority,
   input-frame ticks during continue, and deferred warp advancement/bonus.
   Clock vectors compare timestamp ordering under 60 and 90 host frame chunks,
   independent 50-IRQ/70-display counts in one second, render-side popup expiry,
   pause input ticks, and the new-scene draw gate. Smoke capture stops on the first
   display after interrupt 120 (display 168 at the provisional 70 Hz rate).
3. Regenerated `OriginalWaves.cs` matches the checked-in source bytes. Decoded
   script boundaries match every successive original pointer, and all eight
   command codes and 41 spawn kinds are recovered.
4. Full REA Evidence records are retained, with analysis-copy provenance and
   target identity checked in `docs/rea-evidence-index.json` (165 records). Both
   campaign and timer-analysis sessions closed successfully after capture.
5. Python compilation and `git diff --check` pass. No original-instruction
   differential replay, complete playable game, headset run or Android runtime
   validation has been performed for this replacement.
6. Unity 6000.3.18f1 imported the project and compiled the core, runtime and editor
   scripts. The editor generated the preview scene and baked 406 sprite meshes
   from the original 1024x256 atlas. OpenXR loaders are configured for Windows and
   Android. The Android XR package is pinned to 1.1.0, with OpenXR 1.17.1.
7. A Windows x64 development player built successfully and ran the deterministic
   120-tick first-wave smoke path with `XR=False`, `outcome=0`, `fault=none`.
   Its captured 1280x800 frame was inspected: arena, extracted sprite meshes,
   shots and readable score text render. The first capture exposed mirrored HUD
   text; rotating the text and rebuilding corrected it in a second actual player
   capture. These are desktop rendering checks, not stereo/headset validation.
   The campaign build also completed exactly 120 smoke ticks without a fault;
   its captured frame shows the updated wave HUD. The smoke path ignores live
   keyboard/controller input and stops at the tick boundary, avoiding capture
   overshoot when one display frame advances multiple logic ticks.
8. An ARM64 IL2CPP Android XR development APK built with a successful build result.
   `aapt2` inspection confirms minimum SDK 32, target SDK 36, Game Activity,
   `android.software.xr.api.openxr`, Android XR controller support, and the shared
   `libopenxr.google.so` runtime declaration. ZIP inspection confirms ARM64
   `libil2cpp.so`, `libUnityOpenXR.so` and `libopenxr_loader.so`.
   The build summary still counts one logged exception from the official
   `com.unity.pipeline` 0.9.0-exp.1 helper: its `PipelineRuntimeBuildProcessor`
   unconditionally calls `GetSubtarget<StandaloneBuildSubtarget>()` for Android.
   The package cache was not modified. This is not a clean build result or proof
   of installation, immersive startup, input, rendering or audio on a headset.
9. A separate Quest ARM64 development APK also built with a successful result.
   Its manifest declares VR head tracking, `com.oculus.intent.category.VR`, and
   Quest target devices, without the Android XR Google runtime declaration.
   The first Quest build exposed OpenXR validation reading the selected Windows
   settings group. The build helper now selects the requested platform's settings
   group before building. A second Quest build removed that null-reference error;
   the same one Pipeline callback exception remains in its build summary.
10. Switching Android vendors exposed a stale Quest launch category in an
    incremental Android XR build. Android build options now include
    `CleanBuildCache`. Both variants were rebuilt with that option. Inspection of
    their actual packaged manifests confirms Android XR keeps its OpenXR/Google
    declarations without Quest entries, while Quest keeps its VR category and
    supported-device metadata without the Google runtime declaration. The helper
    package's one logged build exception remains separate from this cache fix.
11. The split-clock Windows player built and ran 120 IRQ ticks plus 168 display
    ticks with `outcome=0` and `fault=none`. A separate original-DOS run using a
    fresh complete fixture and the local instrumented DOSBox-X measured 251 input
    IRQ increments and 346 draw starts over 4.985 seconds: approximately 50.35 Hz
    and 69.41 Hz. Its source executable hash matches the reconstruction target;
    byte checks confirmed the PIT installation and IRQ handler were unmodified.
    Instrumentation was sprite/event markers without framebuffer copying, audio
    was disabled, SDL used its dummy driver, and emulation used fixed 12,000 cycles.
    All owned probe processes were closed. This short wall-clock observation
    supports separate roughly 50/70 Hz clocks, not exact cadence or state parity.
    Retained report: `artifacts/reconstruction/reference-clock-20261022T000906/clock-report.json`.
    Reusable diagnostic: `tools/probe_reference_clock.py`. The reusable CLI adds
    paused data-segment snapshots for future replay work; its argument handling
    and syntax were checked after the successful prototype measurement.
    Both Android variants also rebuilt with the split-clock code; packaged
    manifests retain their respective platform declarations. Their successful
    build results still include the previously identified Pipeline helper
    exception, and neither was installed or tested on a headset in this pass.

## Hunter and axis-pursuit increment

1. Core checks cover herd claiming/conversion, the basic hunter's unscaled byte
   target address, four-shot freeze behavior, reverse-pattern allocation at the
   60-record search boundary, axis pursuit and contact-before-movement ordering.
   The real third-wave stream spawns and advances through 256 logic ticks without
   an unsupported handler. These vectors do not establish complete DOS parity.
2. The Windows player rebuilt successfully with zero build errors. Both first-
   and third-wave desktop smoke runs completed 120 IRQ ticks and 168 display
   frames with `outcome=0` and `fault=none`. The third-wave PNG was visually
   inspected. `--wave N` selects a starting wave for developer checks; it does
   not reproduce prior-wave gameplay or captured original random state.
3. States 8, 14 and 21 share the hunter port. Their four herd-contact outcomes
   kill, convert, emit shots or set the alarm announcement. All twelve
   chase-death outcomes are now supported.
   State 23 retains its per-record selected-player targeting and four position-history pairs.
4. Both Android APKs rebuilt with these handlers. Packaged manifests retain
   Google OpenXR declarations only in the Android XR variant and Quest VR/device
   declarations only in the Quest variant. Each build succeeded while logging
   the existing Pipeline helper's Android-subtarget exception. No headset
   installation or runtime validation was performed for this increment.

## Producer and center-spawner increment

1. Spawn kinds 13, 24 and 38 initialize producer, shooting and alarm hunters.
   State 22 runs its signed-byte child timer before the shared hunter freeze
   gate. Children retain popup fields and increment hostiles without decrementing
   the free counter. The real fifth-wave script creates six producers and
   advances through 301 logic ticks with render-command generation supported.
2. Spawn kind 15 uses fixed coordinates without position-sampling random draws.
   State 24 pins X to 160 and spawns state-25 shots; these expire on wall contact.
   Damage outcome 3 removes the parent without the common score/death effects
   and creates up to ten state-26 offspring. Its scan budget is checked while
   skipping occupied records, allowing consecutive allocations beyond record 69.
   Those offspring bounce and use damage handler 7's health/velocity response.
3. The axis-chase two-player target was corrected after checking CS:4336-435b:
   DS:2d03/2d05 hold the selected player's coordinates for each record, rather
   than permanent player-one coordinates. The four-shot helper separately uses
   player one's Y at DS:2cfd. Checks now distinguish these addresses.
4. The Windows player rebuilt with zero build errors after the target correction.
   Its fifth-wave desktop smoke completed 120 IRQ ticks and 168 display frames
   with `outcome=0` and `fault=none`; the saved PNG was visually inspected.
   The parent/offspring paths have targeted core vectors rather than a complete
   original wave-29 replay, which still requires other unported spawn kinds.
5. Android XR and Quest APKs rebuilt successfully with the same source and
   retained their separate packaged vendor declarations. Both builds still log
   the Pipeline package's known Android-subtarget exception. Neither artifact
   has been installed or run on a headset for this increment.

## Fixed-point shooter increment

1. The CS:38a2 movement port retains 16.16 word carry, signed subtraction,
   acceleration delay, and the source's unusual speed-limit comparison. Exact
   X bounds 8/304 and Y bounds 16/183 reject a candidate and zero velocity;
   the saved fractional position remains unchanged on rejection.
2. Spawn kind 25/state 35 includes selected-player facing and a firing byte
   timer that reloads during freeze. Before wave index 50 it emits horizontal
   state-15 shots; from index 50 it creates state-36 directional children.
   Their original quadrant and velocity tables are exported as data.
3. Damage handler 12 decrements the high byte of word 24 and removes the actor
   at zero without common scoring, kill-count or free-counter changes. Death
   outcomes 10/11 initialize directional children and then run the common kill.
   Although their assembly contains loops, CS:3d43 returns BP=-1 and CS:3441
   preserves it; CS:3d4e consequently stops after the first successful child.
4. Core checks cover fractional carry, the low-word clamp quirk, exact wall
   rejection, both projectile branches, heading changes at zero target delta,
   per-record two-player targeting, byte health and one-child death outcomes.
   The actual seventh-wave script advances through 350 logic ticks with render
   commands supported. This is not a complete original-state replay.
5. The Windows build succeeded with zero build errors. Its seventh-wave smoke
   completed 120 IRQ ticks and 168 display frames with `outcome=0` and
   `fault=none`; the captured PNG was visually inspected.
6. Android XR and Quest APKs rebuilt successfully with these paths and retain
   their separate packaged vendor declarations. Each logs the known Pipeline
   Android-subtarget preprocessing exception; neither was tested on a headset.
   All 107 indexed REA records passed provenance/hash verification. The final
   close request reported a launcher-identity cleanup mismatch; a subsequent
   live process check confirmed that its Ghidra process had exited.

## Contact-emitter and falling-actor increment

1. Spawn kinds 28/29 and update states 38/39 are connected. The emitter moves
   and consumes its firing clock/random bytes during freeze, suppressing only
   shot allocation. Its temporary side/top shot origin is restored afterward.
   Contact changes its sprite and decrements hostiles once, without damaging
   the player or releasing the record. The falling actor instead gates on
   freeze, damages on contact, and retains its 16.16 drop/return phases.
2. Core checks cover retained initialization fields, remote shot origins,
   position restoration, freeze behavior, permanent contact deactivation,
   pursuit at equal X, and floor overshoot/visible-position ordering. Actual
   wave-nine and wave-twelve scripts each advance through 350 logic ticks
   with render-command generation supported.
3. Both desktop smoke runs completed 120 IRQ ticks and 168 display frames with
   `outcome=0` and `fault=none`. Their images were inspected. The wave-twelve
   weight exposed mirrored sprite fronts from the initial camera; the extracted
   source lettering is correct. The shared screen-to-world X mapping and
   upright/flat/effect-plane orientation are corrected together.
4. All 115 REA records passed hash/provenance verification; the four new entry
   analyses and their close operations succeeded.
5. The Windows player rebuilt successfully after the shared orientation fix.
   Both desktop smokes passed again; the new wave-twelve capture shows readable
   `16 TONS` lettering matching the extracted source sprite. Sprite fronts,
   flat shots, effects and screen X now agree from the initial camera viewpoint.
6. Both Android variants rebuilt with the final orientation correction, retaining
   separate Google OpenXR and Quest vendor declarations in their packaged
   manifests. Each succeeded while logging the known Pipeline preprocessing
   exception. These APKs have not been installed or tested on a headset.

## Splitting and beam-family increment

1. Spawn kinds 5/6/7 and state 16 use the recovered shared initializer and
   fixed-motion update. Factories consume a random delay byte before setting
   delay to zero. Normal damage splits a large actor into two medium actors
   and a medium actor into four small actors; hot shots suppress splitting.
2. Split initialization writes through SI as well as BX: it clears the source
   projectile's sprite/lifetime while preserving new actors' residual velocity
   and popup words. Initial velocity axes are swapped relative to projectile
   axes. Failed allocation still writes the kind word at record 70, without
   activating that record or changing counters. Checks cover these boundaries.
3. Spawn kinds 8/9 and states 17/18 implement horizontal/vertical beams. Their
   activation clocks keep running during freeze, while fixed movement stops.
   Alignment damage passes the raw absolute coordinate difference into the
   shared damage routine, retaining its bit-based player-routing behavior.
   Damage handler 6 changes integer velocity words without clearing fractions.
4. Render entries 2/3 draw the hard-coded head, beam span in EGA set/reset
   palette index 1, then actor body. The source spans are X=8..311 and
   Y=10..195 respectively. The missing main-bank head at offset 7aa2 is now
   extracted from the binary and included in the 407-sprite atlas. The captured
   sixteen-color palette is bundled for pixel-line rendering; palette cycling
   remains separate work.
5. Core checks pass, including protected 350-tick runs of the real eleventh-
   and sixteenth-wave scripts. Unity compiled cleanly and baked 407 meshes;
   direct asset checks confirm sixteen colors and the beam-head lookup.
   The 133-record REA index passed source/hash/provenance verification.
6. The Windows build succeeded with zero build errors. Wave sixteen completed
   a 120-IRQ/168-display smoke with score 12 and no fault. Wave eleven initially
   captured an inactive phase at tick 220 after a death/retry. A matching-clock
   diagnostic found its first active beam display at tick 358; the tick-360
   capture visibly shows the horizontal span and head/body. That run completed
   503 displays with score 4, four lives and no fault. Both final captures were
   inspected. `--smoke-ticks N` provides configurable developer capture timing.
7. Android XR and Quest APKs rebuilt with the split/beam paths, pixel-line
   renderer and expanded atlas. Their packaged vendor declarations remain
   separate. Both succeeded while logging the existing Pipeline Android-
   subtarget exception; neither was installed or run on a headset for this pass.

## Axis-dash and scripted-allocation increment

1. Scripted actor and obstacle factories now retain CS:3d43's failed-scan
   result: record 70. The caller initializes that record and updates counters
   instead of throwing. The successful allocator's preliminary field resets
   remain distinct from the failure path; scripted callers still clear popups.
   Boundary checks cover residual fields and repeated reuse without scanning
   into record 71.
2. Spawn kind 23 and state 34 alternate horizontal/vertical dashes using signed
   16.16 movement and signed sixteen-bit decay. Boundary rejection retargets
   before committing position or sprite; speed underflow retargets after the
   movement commit. Exact reverse lower bound sixteen remains valid.
3. Damage handler 11 sets the hit-pause word before reducing health. The actor
   animates during this pause and dies only after health becomes negative.
   Per-record selected-player targeting, fractional carry, signed decay and
   the two retarget orderings have targeted vectors. The real fourteenth-wave
   script advances through 350 protected logic ticks with rendering supported.
4. Unity compiled cleanly and the Windows build succeeded with zero build
   errors. Its wave-fourteen smoke completed 120 IRQ ticks and 168 displays
   with `outcome=0` and `fault=none`; the saved capture was visually inspected.
   All 141 indexed REA records passed hash and entry-hint provenance checks.
5. Android XR and Quest APKs rebuilt with these changes. Packaged manifests
   retain separate Google and Quest declarations. Both builds succeeded while
   logging the existing Pipeline Android-subtarget exception; no headset
   installation or runtime test was performed for this increment.

## Mutable spawner and runtime-confirmed wave-data writes

1. `tools/probe_reference_mutation.py` created an isolated original-game fixture,
   entered normal play, and installed one synthetic state-19 actor with one
   child attempt. Actor, dispatcher and shared initializer bytes were verified
   against the original image. After normal IRQ dispatch, DS:2576/2578 changed
   from bytes `01 0f 05 03` to `00 00 00 00`, and a state-16 child existed.
   Only the probe's owned process job was closed afterward. This verifies the
   controlled emission path, rather than claiming a complete natural DOS replay.
   Retained report: `artifacts/reconstruction/reference-mutation-20261022T022537/mutation-report.json`.
2. The confirmed register flow is CS:4363 leaving SI=256e, then CS:123c/1241
   clearing SI+8/a. Those bytes are two wave-22 spawn groups, changing sixteen
   kind-1 actors and four kind-5 actors into two single-herd groups. The standalone
   now retains mutable level data and decodes it on later wave starts. Retries
   retain that data; the Unity host also shares its store across restarts. Exact
   original menu/new-game reset behavior still needs a separate runtime check.
3. Spawn kinds 10/11 and states 19/20 are connected. The spawner snapshots the
   shared ramp acceleration, consumes child quota before allocation, and can
   emit during freeze after its startup delay. Successful children preserve
   residual velocity/popup fields and mutate the script; full-pool attempts do
   neither, while still consuming quota and requesting their sound.
4. Core checks cover the observed four-byte preimage/write, independent stores,
   retry/later-wave decoding, full-pool behavior, retained child fields and shared
   ramp access. Real wave-nineteen and mutated wave-twenty-two scripts each
   advance through 350 protected ticks. Natural core wave-nineteen emission
   reproduces the observed mutation. All 151 REA records passed provenance checks.
5. Unity compiled cleanly and Windows built with zero build errors. The wave-
   nineteen smoke completed 300 IRQ ticks and 419 displays with score 1548,
   four lives and no fault. A fresh direct wave-twenty-two start completed
   120 ticks and 168 displays with no fault. Both captures were inspected;
   mutated wave-twenty-two decoding is covered by the core checks rather than
   claiming that this fresh direct-start capture replayed earlier gameplay.
6. Android XR and Quest rebuilt with mutable wave data and both actor handlers.
   Packaged vendor declarations remain separate. Each build succeeded while
   logging the existing Pipeline Android-subtarget exception; neither was
   installed or tested on a headset for this increment.

## Remaining chase-death outcomes and recoil family

1. Chase-death outcomes 5/6/8 are connected, completing the twelve-entry table.
   The cardinal hazard samples until an odd byte, moves before harmful contact,
   retains retry kind 1 and does not increment hostiles. Reflected shots negate
   in sixteen bits before arithmetic halving, retain sound/popup fields, and
   increment hostiles. Hot shots suppress both allocations. Hunter conversion
   occurs in place with balanced hostile count and bypasses common scoring/kill.
2. Spawn kinds 17-20 and 31 initialize recoil actors (states 31/40). The shared
   sprite selector persists in spawn context. Acceleration growth and facing
   occur during freeze; movement does not. The fixed-animation variant updates
   its animation even after the common contact handler returns.
3. Damage entries 10/14/15 are ported. Entry 10 writes projectile Y and VX into
   LOW velocity words, retaining high words and startup delay. Entry 14 creates
   a recoil actor with cached whole positions zeroed and fractional positions
   preserved. Entry 15 kills when its health word reaches exactly zero, not on
   signed underflow. Their different counter and residual-field effects remain.
4. Targeted checks cover wrapped negation, random rejection, hot suppression,
   retained fields, full-pool failure, acceleration carry, low-word impulse and
   animation after contact. The actual wave-thirty-two script advances through
   350 protected core ticks. Unity compiled cleanly, and all 165 retained REA
   records passed hash/provenance verification with clean provider closes.
5. The Windows build succeeded with zero build errors. Its wave-thirty-two
   smoke completed 120 IRQ ticks and 168 displays with `outcome=0` and
   `fault=none`; the capture was visually inspected. This exercises normal
   recoil spawning/rendering, while the transformation boundaries are covered
   by targeted core checks rather than a complete original gameplay replay.
6. Android XR and Quest rebuilt with all chase-death outcomes and the recoil
   family. Their packaged vendor declarations remain separate. Both builds
   succeeded while logging the known Pipeline Android-subtarget exception;
   neither artifact was installed or tested on a headset for this increment.

## Leader/follower chain increment

1. REA function and code records cover CS:2def, 366f, 36a0, 36f6 and 115d.
   All 175 indexed records passed source/hash/provenance verification. Raw
   instruction decoding also confirms the shared herd initializer at 1dd0
   and chaser initializer at 1edf.
2. Kind 16 creates eight followers followed by a leader, scanning forward
   through occupied records. This scan can exceed the seventy gameplay slots;
   the portable implementation preserves the recovered hundred-record layout
   and fails explicitly if a scan exceeds it. Only the common factory decrements
   the free counter, and only the leader increments the hostile counter.
   Followers preserve residual velocity, popup and other unmodified words.
3. States 29/30 share the sixty-four-entry history at DS:4481 and cursor 0870.
   Constructors fill history without resetting the cursor. Leaders emit chasers
   from the first 61 slots, record their old position/sprite, update facing from
   the signed low velocity word, then enter fixed movement. Freeze suppresses
   movement while emission/history continue. Followers test contact at their old
   position before reading shared history; damage 9 starts their flash counter.
4. Damage 8 applies fixed impulse on the first four hits. The fifth scans for
   a follower to convert through the shared herd initializer, using the current
   DS:0806 sprite and a two-tick delay. Encountering a leader first instead
   removes the struck leader without score, kill or free-counter changes.
   Conversion preserves mode and unmodified entity fields. The common factory
   now retains the shared herd sprite even when creating other entity kinds.
5. Targeted core checks cover constructor residuals, counter semantics,
   allocation boundaries, history/contact ordering, freeze behavior, fifth-hit
   conversion and early-head scan termination. The actual wave-31 script
   advances through 350 protected logic ticks with render commands. These checks
   establish the reconstructed source paths, not complete original runtime parity.
6. Unity compiled without script errors. The Windows build succeeded with zero
   errors; its wave-31 smoke completed 300 IRQ ticks and 419 displays with
   `outcome=0`, `fault=none`, score 40 and five lives. The captured chain was
   visually inspected. Android XR and Quest rebuilt successfully with the known
   Pipeline package exception; packaged vendor declarations remain separate. This
   increment has not been installed or exercised on a headset.

## Orbiting enemy increment

1. REA function/code records cover CS:2974, 26a0, 2a75, 2a21 and the resource
   loader at 60aa. The index verifies 185 records. Exact instruction decoding
   supplements the shared initializer at 29af and position update at 2ab5.
2. The table at DS:4141 is loaded from `S.CMP`, not initialized in the executable.
   Recovery now decodes that original resource, records its compressed/decoded
   SHA-256 identities and exports its first 256 signed bytes as standalone data.
   The entire 768-byte decoded resource matches the original DS capture in
   `reference-mutation-20261022T022537/before-data-segment.bin`; the comparison is
   retained as `orbit-resource-capture-check.json`. The shipped player uses the
   generated C# table and requires no CMP file or DOS execution.
3. Kinds 21/22 skip random position selection. Kind 21 starts at the low byte of
   shared phase DS:081e, resets the shared acceleration to 2.0 and chase limits
   to 1. Kind 22 starts at phase zero without resetting those globals. Both
   increment shared phase by DS:0820, which also retains its original default 64.
4. State 32 freezes its entire update. When active it emits chasers from the
   first 61 slots, increments the shared 32-bit accumulator by 0x40, writes its
   high word to both chase limits, animates and advances circular position.
   Its unconditional post-emission kind write also affects slot 61 after a full
   scan. State 33 animates and advances during freeze, suppressing only its
   count-five aimed shot. Neither state calls player contact. Both retain
   damage-11 health/hit-flash handling.
5. Position uses signed arithmetic half-table values around (160,110), with
   byte-wrapped phase and quarter-turn lookup. Targeted checks cover wrap,
   negative samples, freeze distinctions, shared globals, carry, failed scans,
   projectile direction and damage. Actual waves 24 and 37 each advance through
   350 protected core ticks with render commands. Unity compiled cleanly and
   the Windows build succeeded with zero errors.
6. Windows wave-24 and wave-37 smoke runs each completed 300 IRQ ticks and
   419 displays with `outcome=0`, `fault=none`, score 0 and four lives. Capture
   inspection found an intermittent presentation issue: HUD text can be clipped,
   and one offscreen capture was almost black. A normal desktop wave-37 capture
   showed the complete scene/HUD, but a subsequent normal wave-24 capture also
   clipped text. Thus concurrency and batch mode alone do not explain it.
   Screenshot/render-state timing remains unresolved; these smoke runs prove
   logic progression, not stable visual presentation. Android XR and Quest rebuilt
   with the known Pipeline package exception. Manifest inspection confirms Google
   OpenXR only in Android XR and Oculus category/device declarations only in Quest;
   headset validation remains outstanding.

## Edge shooters and scripted directional actor

1. REA function/code records cover the four edge factories at CS:2bb6, 2c04,
   2c52 and 2c9f, shared update at 2d35, and the scripted directional factory
   at 33e0. All 197 indexed records passed target/hash/provenance verification.
2. Kinds 34-37 retain random position selection, then replace one coordinate
   with the source edge value (Y=190/17 or X=8/311). Their separate VX/VY words
   store the firing direction; fixed-point velocity starts at zero. Acceleration,
   health and firing cadence come from DS:0828-082e. Recovery now exports the
   four original defaults (all zero), while wave command 7 retains its overrides.
   A fifth random draw ORs into X acceleration only when its low word is nonzero;
   otherwise it ORs into Y acceleration. Cached positions are initialized, while
   delay word +30 and other unmodified fields survive.
3. State 42 tests contact first, advances the high-byte firing timer and, on
   underflow, emits sprite-298 shots with the stored VX/VY. Shot origin precedes
   fixed movement. Freeze suppresses firing and movement but not the timer;
   contact returns before all three. Damage 15 retains its exact-zero health
   behavior. The shared projectile helper owns child/free/hostile bookkeeping.
4. Kind 26 shares state-36 directional movement, starting at heading zero with
   cadence 0120, sprite base 260 and sound word 031a. Its other writes match the
   existing directional initializer, which now accepts those factory values.
5. Core checks cover all four edges, random consumption, retained delay,
   acceleration axes and zero-default behavior, freeze, shot ordering/counters,
   contact precedence and the kind-26 parameters. Actual waves 46, 67, 68 and 69
   each advance 350 protected logic ticks with render commands. Unity compiled
   without errors and the Windows build succeeded with zero errors.
6. Windows wave-46 and wave-69 smoke runs each completed 300 IRQ ticks and
   419 displays with `outcome=0` and `fault=none`, scoring 880 and 2900 respectively.
   Captures were inspected; wave 69 again showed the known intermittent HUD
   clipping, so stable presentation remains unverified. Android XR rebuilt with
   the known Pipeline package exception; its manifest retains Google OpenXR and
   excludes Oculus category declarations. The Quest rebuild completed with the
   same known Pipeline package exception, and
   no headset validation was performed for this increment.

## Completed gameplay dispatch implementation

1. Kinds 27/30 share the stationary-emission and moving phases at CS:320a.
   High-byte clocks trigger exactly at zero; movement/firing clocks advance only
   in their respective phases. Freeze stops the update, contact returns before
   clocks, and damage 13 kills only the unfrozen stationary phase. Other hits
   replace VX/VY with signed half-shot impulse and start a moving hit flash.
2. `LineMotion.Advance` directly ports CS:32fe-33df using signed words. BP=0
   advances one raster step, with source axis swapping, diagonal ties and
   `(1,1)` sentinel on coincident/exhausted endpoints. Homing pickups now share
   this implementation. A permanently contact-disabled test fixture could let
   pickups reach the exact target and enter the original impossible-bounce path;
   the 100-wave sweep instead permits normal collection and suppresses damage
   through the existing hit lock. The portable bounce routine still rejects
   inputs that would loop in DOS rather than silently changing the sentinel.
3. Kind 27 creates directional children at inherited coordinates without clearing
   popup data or decrementing the free counter. Kind 30 creates asymmetric
   horizontal projectile pairs with normal projectile counters. Kind 32/state 41
   shares the phase logic, uses frame-clock animation and creates harmful homing
   pickups (index 9, sprite 288, lifetime 100), again retaining popup/free count.
4. Kind 39 adds a random heading and cadence 0104 to the existing directional
   initializer. Kind 40 skips random position and initializes the fixed-center
   dormant state 43, whose source update is a RET. Damage 17 starts detonation on
   the fifty-first hit without common kill/score handling. State 44 spends up to
   150 child quotas every five updates, even during freeze; full pools still
   spend quota without random draws. State 45 advances explosion sprites 327-329
   every four updates. Removal decrements hostile count without freeing the
   source counter. Unspecified child words survive the partial initializer.
5. Renders 9/10 each draw normally and increment
   the full render word. Render 14 emits four circular history positions using
   the recovered DS:0911 sprite offsets, then the body. Commands represent the
   visible drawing operations; the original EGA page storage is not emulated.
   Render 4 remains unsupported: CS:4646 calls the sprite writer directly and
   then enters 4499 after the writer has changed DI. Reusing the original entity
   for a second transformed draw was an unsupported assumption and was removed.
   `tools/probe_reference_render4.py` confirmed this in an owned DOSBox run:
   `reference-render4-20261022T042650/registers.txt` stops at CS:4672 with DI=325f,
   rather than the original entity at 48c1. The parent render word is already 5.
   Reads through 325f return unrelated values such as X=27400 and Y=27440.
   The sprite-entry instrumentation preserves entry registers, and the report
   verifies the original render-4/remaining writer bytes. The disposable probe
   and its owned process job were closed. This confirms the pointer clobber but
   does not yet define a standalone continuation for every input. In contrast,
   render 14 calls 42f0, which explicitly preserves all registers around drawing.
6. Targeted checks cover line boundaries, phase ordering, signed clocks, partial
   allocation, retained fields, all remaining factories, damage and render-state
   mutation. Waves 48/56 have 350 protected ticks; waves 61/65/66 permit normal
   contact with damage locked. A separate sweep ran all 100 scripts for up to
   350 ticks each: all 100 spawn streams completed, two waves cleared early,
   and none warped. This is independent later-wave startup, not a continuous
   campaign replay or original-machine state comparison.
7. All 46 update IDs, 41 spawn kinds, 18 damage IDs, twelve chase-death outcomes
   and 18 of 19 render IDs now have implementations. Unity compiled cleanly after
   the render-4 guard correction, and the corrected Windows build succeeded.
   All 235 REA records, including the final core/render dossiers and line-stepper,
   passed target/hash/provenance verification with clean provider closes. Audio,
   menus/modes, palettes and platform/runtime parity remain separate unfinished work.
8. Windows wave-48/56 and wave-61/66 smoke runs each completed 300 IRQ ticks and
   419 displays with no simulation fault. These preceded the render-4 guard
   correction; the correction was then checked by the 100-wave core sweep and
   a clean Unity compile/Windows rebuild. Android XR and Quest subsequently
   rebuilt with that guard, both logging the known Pipeline package exception.
   Their packaged vendor declarations remain separate. A later palette-symbol
   rename changes no gameplay behavior. No headset validation was performed.

## Sampled audio implementation

1. The supplied `SBLASTER.BIN` is a 1,895-byte flat 16-bit driver. Analysis-only
   wrappers add a 64-byte DOS header while preserving every original driver
   byte; `tools/prepare_rea_sound_driver.py` records source/derived hashes and
   refuses to overwrite the input. These wrappers are never executed. Their
   separate REA records are retained under `artifacts/reconstruction/audio` and
   are not part of the GAME86-specific 235-record index. The separate
   [audio Evidence index](rea-audio-evidence-index.json) verifies 24 records,
   source/wrapper identities and byte-exact provenance.
2. Successful driver initialization enables AH functions 0-6. AH=3 accepts sample
   IDs 1-32, maps CL through a rate table at driver 03d2, and uses CH as priority.
   It queues a voice and can request a stop based on the current priority. AH=6
   returns current sample ID in AH and priority in AL. `SoundDriverState` now
   implements the queue/service, matching-ID stop, stop-all and completion rules.
   Lower priority requests remain pending; equal/higher priority can preempt.
   DMA completion clears sample ID but retains priority. Mismatched stop IDs
   remain pending, including the source's stale-stop edge case.
3. DS:06c7 was previously misnamed as music. GAME86 CS:e960-e9c2 reads its words,
   maps nibbles through DS:2134 and calls VGA BIOS palette functions. Core symbols
   are now `PaletteSequenceOffset`, `PaletteSequenceRequested` and
   `RequestPaletteSequence`; the field remains a request pointer until palette
   progression is implemented. Sampled audio and palette flashes are separate
   source systems. This correction prevents an audio host from misusing the
   palette requests. Portable checks passed after the rename.
4. The driver selects ten-byte descriptors at 0609 using sample ID +1. The 32
   active descriptors supply 32-bit bank offsets, 16-bit lengths and unused zero
   loop words. It sends DSP command 14 and rate command 40; the
   [DOSBox-X DSP source](https://dosbox-x.com/doxygen/html/sblaster_8cpp_source.html#l01716)
   confirms single-cycle 8-bit PCM and the time-constant rate. Mono unsigned
   decoding follows that source. CL maps to constants 1,52,126,152,206; nominal
   rate is 1,000,000/(256-constant). Generated WAVs use an 8,000 Hz container;
   Unity applies the original rate through playback pitch.
5. The launcher reads exactly 156,813 SPL bytes after the 1,895-byte driver in a
   9,920-paragraph allocation. Sample 30's descriptor extends 96 bytes past EOF.
   `tools/probe_reference_audio.py` enables Sound Blaster only in an owned copy,
   verifies resident driver regions and matches the complete loaded SPL to its
   file. Capture `reference-audio-20261022T044534` contains all 96 tail bytes,
   of which 78 are nonzero. The first asset profile preserves those observed
   bytes rather than guessing padding or truncation; other DOS layouts may vary.
   `tools/recover_audio.py` requires the matching captured memory for this tail,
   records hashes and exports all 32 byte-exact WAVs plus descriptor/rate data.
6. `SampledAudio` loads those bundled clips and renders one non-spatial voice.
   The driver service runs before each game IRQ callback, as at GAME86 3e02.
   Audio completion is polled separately from retained priority. Warp now polls
   active sample ID, with the source minimum two fresh retraces. Application
   suspension pauses the voice; the game's existing pause continues driver IRQ
   service. Full original pause/menu sound behavior remains part of that work.
7. Unity's audio bake uses PCM, preserves rate, disables mono normalization and
   compares every imported float sample to the recovered unsigned PCM amplitude.
   All 32 imports passed. Core checks cover queue latency, priority, deferred
   requests, invalid/zero IDs, stale stops, constructor sound wiring and warp
   completion. The existing 100-wave bounded sweep still passes.
8. The Windows audio build succeeded with zero errors. Its wave-1 smoke completed
   300 IRQ ticks/419 displays without a simulation fault, recording 23 voice
   starts, 21 stops and Unity mixer peak 0.707107. This proves engine signal
   production, not speaker/headset audibility or original-machine timing parity.
   Android XR rebuilt with the known Pipeline package exception; Quest is pending.

## Remaining obligations

1. Extend the working REA DOS analysis across the remaining indirect handlers and
   retain their function evidence. Continue verifying and extending the mapping.
2. Complete alternating-player campaign snapshots, original wipe/menu/high-score
   presentation, droid/demo AI and original pause input. Exercise warp playback
   end to end against the original, beyond the core status/retrace checks.
   Verify the connected normal
   frame implementation against original runtime traces, including rare effects.
3. Verify all implemented gameplay dispatch entries against original execution,
   including rare and non-scripted states, retained fields, rendering mutations
   and allocation failure paths. All 46 update IDs, 41 spawn kinds, 18 damage IDs,
   12 chase-death outcomes and 18 render IDs now have implementations; code
   coverage and bounded checks do not establish full runtime parity. Render ID 4
   remains unsupported pending an original-runtime trace of the post-blitter DI.
4. Refine retrace rate, initial hardware phase and IRQ/render interleaving; compare
   deterministic state traces against the original using the same inputs and
   captured random table. The recovered PIT rate and independent clocks are now
   implemented; the live timing observation does not establish state parity.
5. Complete palettes, animations and transformed pixel effects; verify sampled sound in
   the standalone asset pipeline. The 407 recovered sprites are packed and baked;
   supported actor/player/bullet drawing now emits standalone render commands.
   No original game file may
   be needed by the shipped player. Decide how to package derived asset data.
6. Finish and validate the initialized Unity project's Windows and Android OpenXR,
   stereo arena rendering, controller input and audio. Match original pixel presentation and preserve the
   existing mod while the replacement is incomplete.
   Resolve intermittent clipped HUD/black offscreen captures in the desktop
   presentation path; do not treat successful capture-file creation as visual validation.
7. Produce standalone Windows and ARM64 Android builds. Verify gameplay without
   original files or DOSBox, then validate PC VR and physical standalone OpenXR
   controller/rendering behavior. A portable-library build proves none of these.
