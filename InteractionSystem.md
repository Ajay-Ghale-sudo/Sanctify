# Sanctify Interaction System

Status as of 2026-09-25. Where this file and the code disagree, the code wins.

- **Part 1, Now.** How the system works today, and the rules for working in this codebase.
- **Part 2, Doors.** How doors, bolts, blocking and breaking work.
- **Part 3, Steps.** What to verify, then a backlog of things to build only when needed.

The Amnesia research write-up and the monster/door write-up this replaces are no longer needed to work here. Everything still relevant from them is below.

---

# Part 1: Now

A state machine on the player (`PlayerInteractor`) is fed by props (`Interactable` subclasses). A focus ray finds a prop. On Interact, the prop writes an `InteractionContext` and asks for a state. That state owns input and physics until it ends itself. Four states exist: **Default**, **Pickup**, **Grab** (with throw) and **Drag** (push and pull).

## What's built

All paths are under `Assets/Scripts/`.

| Piece | Where | Status |
| --- | --- | --- |
| Frame order | `Characters/Player/PlayerController.cs` | Built |
| State machine, focus ray, view/aim poses | `Characters/Player/PlayerInteractor.cs` | Built |
| State contract | `Interaction/InteractionState.cs`, `HeldState.cs`, `InteractionContext.cs` | Built |
| Prop base | `Interaction/Interactable.cs` | Built |
| Focus and dispatch | `Interaction/States/DefaultState.cs` | Built |
| Pickup | `Interaction/PickupItem.cs`, `States/PickupState.cs` | Built; inventory stubbed (`Take()` accepts everything) |
| Grab and throw | `Interaction/PhysicsProp.cs`, `GrabData.cs`, `PlayerGrabSettings.cs`, `States/GrabState.cs` | Built, accepted 2026-09-23 |
| Drag (push and pull) | `States/DragState.cs`, `PhysicsProp.Handling` | Built, third version accepted |
| Cursor interact mode | `Characters/Player/PlayerInteractMode.cs` | Built |
| Crouch and tiptoe | `Characters/Player/PlayerStance.cs`, `CharacterMotor.TrySetHeight` | Built, interact mode only |
| Player/prop collision safety | `Interaction/PlayerCollisionFilter.cs`, `CharacterMotor.SetIgnored` | Built |
| Held-object shadow | `Interaction/HeldObjectShadow.cs` | Built |
| Cursor and focus text | `Debug/DebugCrosshair.cs` | OnGUI placeholder |
| Test rig and arena | `Editor/PlayerRigBuilder.cs` | Built |
| Doors (handle, latch, bolt, blocked state, break) | `Interaction/Door.cs`, `States/DragState.cs` (jointed bodies) | Built 2026-09-25, not yet playtested (Part 2) |
| HUD, inventory, sounds, switches | | Not started (Part 3 backlog) |

The 2026-09-25 changes (per-kind reach, no drag reaching phase, grab draw-in, critical damping, braking, pivot hold, leash) were last recorded as not yet compiled or playtested. Step 1 covers that.

## Working in this codebase

- **Engine.** Unity 6000.7.0b1, URP, new Input System. Use Unity 6 names: `linearVelocity`, `linearDamping`, `angularDamping`. Use `FindObjectsByType<T>()` without a sort mode.
- **Compiling.** The user compiles in the open editor. Never compile from the command line (no batchmode, Roslyn or `dotnet`). After changing scripts, ask the user to recompile, then search the project's `Logs/Editor.log` for `error CS`.
- **Assemblies.** One runtime assembly (`Assets/Scripts/Sanctify.asmdef`), one editor assembly (`Assets/Scripts/Editor/Sanctify.Editor.asmdef`). Namespaces follow folders: `Sanctify.Characters`, `Sanctify.Characters.Player`, `Sanctify.Cameras` (not `Camera`), `Sanctify.Interaction`, `Sanctify.Debugging`, `Sanctify.Editor`.
- **Style.** Classes are `sealed`. Serialized fields are camelCase with a `[Tooltip]`; private fields are `_camelCase`. Components use `[DisallowMultipleComponent]` and `[RequireComponent]`. XML summaries say why, not what. Player subsystems have no Update of their own: they expose `Tick`, `FixedTick`, `LateTick`, and `PlayerController` calls them.
- **Tuning** lives in ScriptableObjects, because play-mode edits persist there: player-wide in `Assets/Settings/Player/`, per-prop presets in `Assets/Settings/Interaction/`. The rig builder's `GetOrCreateAsset` never overwrites existing tuning.
- **Input** comes from `Assets/Input/Sanctify.inputactions` (map `Player`), read only through `PlayerInputReader`. Editing the JSON directly is fine if new actions and bindings get fresh GUIDs.
- **Feel.** King's Field IV: slow, heavy, deliberate.
- **Scene.** `Assets/Scenes/SampleScene.unity`. The builder doesn't update objects it already made: after changing test props, delete `Motor Test Arena` and recreate it from the menu.
- **One step at a time.** Build the step, add test props to `PlayerRigBuilder.CreateTestArena` (and to `UpgradePlayerRigs`/`CreatePlayerRig` if the rig gains a component or asset), ask for a recompile, check `Editor.log`, then stop for feedback.

## Frame order and routing

```
Update       interactMode.Tick → look (routed) → actions (routed) → interactor.Tick → combat, magic
FixedUpdate  stance.FixedTick → movement (routed) + motor → interactor.FixedTick (state forces, then collision filter)
LateUpdate   cameraRig.Tick → interactor.LateTick → interactMode.LateTick (drawn cursor) → heldShadow.LateTick
```

- **Routing.** Look, move and actions (`Interact`, `Attack`, `Magic`, press and release) go to the current state first via `RouteLook`, `RouteMove`, `RouteAction`. Return `true` to let the normal action run too, `false` to swallow it. A swallowed Magic release cancels the charge.
- **Look is a rate** (-1..1 from stick or arrow keys, ramped by `PlayerLook`), not a mouse delta.
- **Control locks.** When `PlayerControlLock` blocks `Actions`, interact mode exits and `PlayerInteractor.Cancel()` returns to Default: held objects drop, unfinished pickups go back.

## State contract

- **IDs.** `InteractionStateId { Default, Pickup, Grab, Drag }`. Register new states in `PlayerInteractor.Awake`.
- **Entering.** A prop calls `Begin(interactor, id, body, hit)` → `BeginInteraction`, which asks `state.CanEnter(context)` first. A refusal leaves the current state running.
- **`Enter()`/`Exit()` take no arguments.** `ChangeState` sets `next.Previous` and makes `next` current before `Enter`, so a state can bail out during `Enter`. Leave with `ReturnToPrevious()`; `ChangeState(null)` means Default.
- **`HeldState`** loads `Prop`, `Body`, `HitPoint` from the context, marks the prop in use, and ends when the prop is destroyed (`PropDestroyed`). Subclasses call `base.Enter()` first and `base.Exit()` last. `IsHoldable` refuses kinematic bodies and whatever the player stands on.
- **Every state restores in `Exit` what it changed in `Enter`,** and ends itself (released, too far, out of sight, prop destroyed, or thrown).
- **Hooks.** `Tick` (after routing), `FixedTick` (after movement; apply forces here), `LateTick` (after the camera; anything that follows the rendered view). No `OnScroll`: interact mode calls `GrabState.AdjustDepth`.
- **Services on `PlayerInteractor`.** `FocusRay`/`AimDirection` (rendered cursor ray, with bob and peek: aim and throw with these). `ViewPose`/`GetFixedStepAimRay()` (same at the last physics step, no bob: physics goals use these). `HoldPose`, `Motor`, `Movement`, `Look`, `GrabSettings`, `CollisionFilter`, `ReachTo(point)` (flat distance from the capsule's side), `HasLineOfSight`, `CastSolid`, `TryGetViewportPoint`.
- **`PD3`** has `Output(error, dt)` and `Output(error, errorRate)` (rate supplied, e.g. a measured velocity).

## Focus and props

- **Focus.** `DefaultState` calls `CastFocusRay`: nearest hit from the rendered camera through `CursorViewport` (screen centre, or the cursor in interact mode), skipping the player and any trigger that isn't a prop's.
- **`Interactable.CanFocus`** checks disabled, in use, `maxFocusDistance` (2 m from the eye), then the prop's `IsUsableBy(interactor, hit)`.
- **`PickupItem`** is usable anywhere the ray reaches, in both modes.
- **`PhysicsProp`** is usable only in interact mode, and only within reach: `grabReach` (0.7 m) to lift, `dragReach` (0.5 m) and facing within 45° to drag. `handling` picks the state: `ByMass` (default; drags above `maxLiftMass`, 20 kg), `Lift` or `Drag`. It uses the body the ray hit, so jointed props are held by the part grabbed.
- **The prop receives the interactor** (there's no global player) and notifies it from `OnDestroy`.

## Pickup

- The item eases to `HoldPose` in `LateTick`; `Take()` runs at `takeAt` of `duration`. Movement and look are swallowed.
- A taken item is hidden, then destroyed in `Exit`, so the animation finishes. A refused or interrupted pickup restores the exact pose, colliders, kinematic flag, interpolation and collision mode.
- `PickupItem` uses its own Rigidbody, never the hit body.

## Grab and throw

The grabbed point, stored in body space, is pulled toward the fixed-step cursor ray at `_depth`. **Don't bring back Amnesia's grab gains** (position P 400, spin gain 100): at Unity's 0.02 s step they overshoot every step and the object buzzes.

- **Pull.** A spring toward the goal, damped against the player's velocity (not the goal's), as a wanted speed (stiffness ÷ damping × gap) and a damper to it. Stiffness 225, damping 30: critically damped, about 0.13 s of trail. Wanted speed is capped at √(2 × a × gap), with a = 60% of what `maxPullForce` (80 N) can brake, so heavy things arrive without overshooting. Keep damping × 0.02 under about 0.6.
- **Effective mass.** `EffectiveMassAt` maps a wanted point acceleration to the force at that point (an off-centre point acts lighter than the body). Sizing by the whole mass made off-centre points buzz.
- **Pivot hold.** Gravity stays on. Outside the pull cap, the point also gets effective mass × −(gravity + centripetal ω × (ω × r) + the swing torque's effect), so the object hangs and swings about the cursor without dragging it. `SwingDampingTorque` brakes angular momentum about the point (`swingDamping` 6, capped at half the spin per step).
- **Held angle (optional).** `GrabData.holdOrientation` or `usePoseOffset` adds a spin loop (catch-up capped at 0.9 ÷ `fixedDeltaTime`) and carries weight at the centre. `GrabState.Rotate` exists but has no binding.
- **Depth.** Starts at the hit depth − `grabPull` (0.08 m), so nothing jumps. Farther than `holdDistance` (0.9 m) is drawn in at 1.5 m/s. Bumpers stop the draw-in and move it within `GrabData.minDepth`/`maxDepth`.
- **While held.** Mass × `massMultiplier`, interpolation on, ignored by player collision, player slowed from 2 kg carried down to 0.5× speed at 20 kg. The goal is kept at least capsule radius + `keepOutMargin` from the player.
- **Ends.** Interact again (toggle). Point farther than max(start distance, `maxDepth`, depth) × 1.1 + 0.2. Out of sight longer than `lineOfSightGrace` (0.5 s). Letting go caps speed at 2 m/s and spin at 4 rad/s.
- **Throw** (Attack). Velocity along eye → grabbed point, lofted 8°, at `throwStrength` (8) × `GrabData.throwMultiplier` ÷ real mass, capped at 14 m/s. Multiplier 0 = can't throw (`SO_Grab_Dense` uses 2.5).
- **Tuning.** `PlayerGrabSettings` (`SO_PlayerGrab`) and `GrabData` (`SO_Grab_Default`, `SO_Grab_Dense`).

## Drag (push and pull)

Walking pushes and pulls; every force goes in at the grabbed point, against normal friction.

- **Starts** within `dragReach` (0.5 m) and facing within 45° (0.75 × `dragLetGoAngle`), straight away.
- **Effort** ramps from −1 (pull) to +1 (push) with the walk input: leaning in 0.4 s, easing off 0.1 s. Force = effort × `dragStrength` (650 N) along the flat feet → point line, fading as the point nears the pace (0.9 m/s at 20 kg, 0.3 m/s at 100 kg).
- **Holding the line.** The point's sideways slip is damped at 8/s per kg.
- **Lift.** A spring toward 5 cm above the point's rest height, capped at min(250 N, 60% of weight) × how low the grab is (all at the base, none at the top). It fades if the point drops 10 cm, so a crate pulled by its top can tip over.
- **Player follows.** Forward speed is capped to the point's speed along the line (next step); strafe is zero. Pulling from within 0.1 m of the capsule, the player leads instead. The body stays solid to the player.
- **Turning** away from the point slows to 0.25× at 60°; past 60° it lets go.
- **Ends.** Interact again, distance from the feet > start × 1.2 + 0.3, out of sight > 0.5 s, turned past 60°, or prop destroyed. Attack and Magic are swallowed.

## Cursor interact mode

Sits above the states, so the cursor works while holding.

- **Toggle** R3 / Tab; keeps the current view. **Exit** stands the player up and calls `Cancel()`.
- **Cursor** on the right stick / IJKL over the 4:3 view; `CursorViewport` follows it. **Edge turn** starts at `edgeBandStart` (0.38) while pushing outward, scales from `edgeHoldStart` (0.2) to `cursorLimit` (0.47), stops when the stick lets off. Pitch comes only from the edge turn here.
- **Holding.** The drawn cursor (`DisplayCursor`) sits on the held point; `Cursor` becomes a hidden pull target, leashed to within `maxLead` (0.15 view units) of the drawn cursor, and pulled back to it while the view turns. Bumpers change depth, limited to `maxDepthLead` (0.25 m) past `GrabState.PointDepth`, and don't strafe then.
- **Triggers** set stance: RT tiptoe (2.05 m), LT crouch (1.2 m). Growing is capsule-cast against the ceiling and retried each step.

## Player collision

The player is a kinematic Rigidbody moved by `CharacterMotor`'s own capsule casts. It never pushes props and props never push it; anything overlapping it gets the player pushed out.

- `CharacterMotor.SetIgnored(collider, bool)` removes colliders from the motor's queries (`Physics.IgnoreCollision` doesn't affect queries).
- `PlayerCollisionFilter` applies both ignores to a grabbed body, and keeps them after release until `Physics.ComputePenetration` shows it's clear.
- Dragged bodies are not filtered: they stay solid.

## Controls

| Control | Normal mode | Interact mode |
| --- | --- | --- |
| Left stick Y / W, S | Walk | Walk; while dragging, push or pull |
| Left stick X / ←, → | Turn | Turn; while dragging, slower turning away, let go past 60° |
| Bumpers / A, D | Strafe | Strafe; while holding, depth; while dragging, nothing |
| Triggers / ↑, ↓ | Pitch | RT tiptoe, LT crouch |
| Right stick / IJKL | Peek | Cursor; edge turns the view |
| R3 / Tab | Enter interact mode | Exit interact mode |
| Circle / E | Interact at centre (pickups, door handles) | Interact at cursor: grab or drag within reach (door handles too; bolts are grabbed and slid), again to let go |
| Square / left mouse | Attack | Throw if holding |
| Triangle / F | Magic (swallowed while holding) | Same |
| Cross / Left Shift | Run | Run |

## Test tools

**Sanctify → Player** menu: **Create Player Rig In Scene**, **Create Motor Test Arena** (pickup table, stance tests, grab table and floor, throw target, drag crates at (−4.5, −12), stone block at (4.5, −12.5), corridor crate at (0, 7), a door at (15, −4) with a crate beside it, a bolted door at (15, −9), three door-breaking iron props between them, a sealed hut at (15, −14.5)), and **Upgrade Player Rigs In Scene** (extend it whenever the rig gains a component or asset). `PlayerControlLock.Debug Block` tests interruptions; `PlayerInteractor` draws the focus ray on Interact.

## Known issues

- **`SO_PlayerMovement.asset` stores old field names** (`groundAcceleration`, `groundFriction`, `stopSpeed`), so `acceleration` (6) and `deceleration` (8) run on code defaults. Rewrite once the user picks values.
- **`HoldPose` ignores the cursor**: pickups always fly to the lower right.
- **Grab.** Grabbed from below, objects flip to hang under the point. Long hanging objects can swing through the player (collision is off while held). The pivot hold's centripetal term is uncapped; watch for pops after a fast spin.
- **Drag.** Tip/slide thresholds assume default friction (0.6). The let-go angle is measured flat from the player's middle, so a point low and close beside the player can be well off to the side while still on screen.
- **UI** must place itself with `FixedAspectRenderer.ViewScreenRect` or `ViewportToGuiPoint` (the camera renders to a RenderTexture), not raw screen size.

---

# Part 2: Doors

Built 2026-09-25, not yet playtested. A door is a hinged Rigidbody with a `Door` component (`Interaction/Door.cs`) that starts the existing **Drag** state from its handle. Walking pushes and pulls at the handle, and the hinge turns that into a swing.

- **Why Drag, not a new DoorState.** Drag already applies every force at the grabbed point, keeps the body solid to the player, caps the player's speed to the handle, backs the player off when pulling close, and lets go when the player turns away (so a door swung wide releases itself).
- **Handle only, both modes.** `Door.IsUsableBy` accepts only hits on its `handle` collider, within `DragState.CanTakeHold`, with or without the cursor. One collider through the leaf serves both sides.
- **Latch.** A door within `latchAngle` (10°) of closed that nobody holds narrows its hinge limits to ±`latchPlay` (2°), so it clicks shut and stays shut when hit. Using the handle unlatches it.
- **Bolt lock.** The bolt is a `PhysicsProp` on a `ConfigurableJoint` to the door, free along the joint's x within its linear limit. It's grabbed and slid in interact mode like any loose object (`SO_Grab_Fixture`, which can't be thrown). It's home past the middle of its travel toward +x, and the joint's connected anchor marks the middle (the builder turns off `autoConfigureConnectedAnchor` so the bolt can start at an end). A latched door with its bolt home is locked. Trying the handle yanks it at `rattleForce` (150 N), toward the player then away, every `yankTime` (0.1 s) for `rattleTime` (0.6 s), so it rattles within the latch play and never opens. Sliding the bolt free unlocks it; sliding it home on a shut door locks it again. It sits on one face, so only that side can reach it.
- **Bolt friction.** The joint's X Drive damper (1000) holds the bolt where it's left, so a swinging door can't fling it home. It drops to 0 while the bolt is held. The bolt's collider is a trigger, so it can sit in the post without shoving the door.
- **Blocking.** Props and doors are ordinary dynamic bodies, so a crate in the way stops the door. Drag pushes jointed bodies with `jointedDragStrength` (200 N), not `dragStrength` (650 N), so a crate over about 30 kg holds a door and lighter things get shoved.
- **State, for monsters.** `Door.State` is `Broken`, else `Locked`, else `Blocked`, else `Closed` (latched), else `Open`. `Blocked` means a `PhysicsProp` of at least `blockingMass` (20 kg) touches the leaf. It's tracked by collision enter and exit, so it survives both bodies sleeping. `IsBlocked`, `IsLocked` and `IsBroken` are public too. A broken door reads `Broken` only until it swaps itself out; after that the Door is destroyed and a stored reference is null.
- **Break.** `Door.Break()` (also the component's context menu) destroys the HingeJoint. So does a `PhysicsProp` with `breaksDoors` hitting the door at `breakSpeed` (2.5 m/s) or more, which in practice means thrown. Once the joint is really gone, the door tips the slab over, adds a plain `PhysicsProp` (Drag by mass, no focus text) and destroys itself. The slab is then an ordinary loose object: grabbed or dragged anywhere, in interact mode only. A drag on the door in progress ends as the Door goes. There's no damage, pieces or debris yet.
- **Friction** is the HingeJoint's own spring, with spring 0 and damper 3.
- **Jointed bodies in Drag** skip the lift and the sideways damping. The joint already holds the point's path, and both would only fight it.
- **Hinge angle is measured from the pose at load,** so author doors closed.

---

# Part 3: Steps

## Step 1: Verify what's there (no new code)

- Recompile and check `Editor.log`.
- Delete `Assets/Test.cs` (an empty MonoBehaviour) and its `.meta`.
- If the 2026-09-25 changes haven't been played yet, rebuild the arena and check:
  - [ ] Loose objects highlight only within about 0.7 m of your body, heavy ones within about 0.5 m and only when facing them. Pickups still highlight from 2 m.
  - [ ] An object grabbed from the floor or the far side of the table comes in to about 0.9 m. Nothing jumps. Bumpers push it back out.
  - [ ] Moving the cursor and stopping: ball, brick and plank stop on the cursor, swing about the grabbed point, and settle within about a second.
  - [ ] The 8 kg crate lags more than the brick but doesn't sail past. Nothing jitters while standing still.
  - [ ] Heavy objects drag straight away on Circle. Walking pushes and pulls; the player never gets ahead or left behind.
  - [ ] By the top edge the crate tips toward you; by a face it slides; by the base its near edge lifts.
  - [ ] Turning away slows, and past about 60° you let go. The corridor crate jams without anyone clipping.

## Step 2: Doors (built, to playtest)

- **Arena** (`CreateDoorTests`, east of the stairs): a free door at (15, −4) with a 40 kg crate beside it, and a door at (15, −9) bolted from its south side. Each is two posts, a lintel and a 0.9 × 2 × 0.06 m, 25 kg leaf hinged on the west post, opening both ways (−100° to 100°). Between the doors lie two 4 kg iron balls and a 5 kg iron weight (`SO_Grab_Dense`, `breaksDoors`). South of them, a 4 × 4 m hut at (15, −14.5) with no way in: its door is bolted on the inside, and its back window is 0.9 × 1.3 m (crouch-sized) with its sill at 1.1 m.
- **Done when:**
  - [ ] Doors are focused only at the handle, from the centre crosshair and from the cursor.
  - [ ] Walking forward pushes a door open from either side; walking back pulls it, with the player backing off instead of clipping.
  - [ ] Turning away from a door swung wide lets go of it, and a released door slows to a stop.
  - [ ] A door let go near closed clicks shut and stays shut when a thrown brick hits it.
  - [ ] From the north, the bolted door yanks back and forth and won't open. From the south, in interact mode, grabbing the bolt and sliding it west lets the door open. Sliding it back east with the door shut locks it again.
  - [ ] Swinging a door open and shut doesn't move its bolt.
  - [ ] The crate dragged against a door keeps it from swinging that way. The door's context menu → **Log State** prints `Blocked` (and `Locked`, `Closed`, `Open` in the other cases).
  - [ ] The door's context menu → **Break** drops it off its hinges. In interact mode the fallen slab drags from any point, not just the handle.
  - [ ] An iron ball or the iron weight thrown at either door, locked or not, knocks it off its hinges. Set down against a door, they don't.
  - [ ] The hut door rattles from outside. The window shows the bolt inside but can't be reached, and nothing gets the player up to it.
  - [ ] A door swung into the player stops, and nothing glitches the camera.

If doors feel wrong through Drag (too slow near the handle's arc, or the turn release is annoying), the fallback is starting **Grab** from `Door.HandleInteract` instead, with one exception in `GrabState.Enter` so jointed bodies aren't added to the collision filter. Try Drag first.

## Backlog: build only when needed

| Item | Build it when | Lazy version |
| --- | --- | --- |
| Door damage and debris | Something deals damage (no damage system exists yet) | A `Damageable` with health and toughness (damage 0 if strength < toughness − 1, half if equal) that calls `Door.Break()` at 0 health; it replaces `PhysicsProp.breaksDoors`; later, a broken prefab whose pieces get the door's velocity plus an outward `VelocityChange` |
| Keys | A level needs one | A `locked` flag on `Door` ANDed into `IsLocked`, cleared by a key `PickupItem`'s On Pickup |
| Monster door handling | Monsters exist | Stuck counter (real speed < 30% of wanted) + overlap a `Door` ahead → bash until broken, with a timeout; see the monsters write-up |
| Switches and levers | A puzzle needs one | A `SwitchProp : Interactable` that invokes a `UnityEvent<bool>` |
| Drawers | A level has one | `Door` on a `ConfigurableJoint` body; `Door` treats a missing HingeJoint as broken, so latch and state need a no-hinge path |
| HUD, per-prop crosshair, pickup outline | Before the first playable level | One UI element for dot and cursor on the `FixedAspectRenderer` canvas; replace `DebugCrosshair` |
| Inventory | Items need to be used | `ItemDefinition` ScriptableObject + `Inventory.TryAdd`; `PickupItem.Take()` calls it (refusal already restores the item) |
| Sounds | The Steam Audio setup exists | Ask the user first |
| Rotating held objects | Asked for | Bind `GrabState.Rotate` (e.g. right stick while Triangle is held) |
