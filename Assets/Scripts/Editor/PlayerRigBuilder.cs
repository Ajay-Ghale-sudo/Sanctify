using Sanctify.Cameras;
using Sanctify.Characters;
using Sanctify.Characters.Player;
using Sanctify.Debugging;
using Sanctify.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sanctify.Editor
{
    /// <summary>
    /// One-click setup so the player rig is built the same way every time:
    ///
    ///   Player           (CapsuleCollider, Rigidbody, CharacterMotor, PlayerController + subsystems)
    ///   └─ PlayerCamera  (Camera, PlayerCameraRig, FixedAspectRenderer, HeadBobModifier,
    ///                     PeekModifier, LandingDipModifier, AudioListener, DebugCrosshair)
    ///
    /// The camera has no pivot chain: PlayerCameraRig composes its world pose every frame.
    /// Settings assets are created under Assets/Settings/Player if they don't exist yet.
    /// </summary>
    public static class PlayerRigBuilder
    {
        const string SettingsFolder = "Assets/Settings/Player";
        const string InteractionSettingsFolder = "Assets/Settings/Interaction";
        const string InteractionArtFolder = "Assets/Art/Interaction";
        // URP's decal graph with angle fade turned on, so the shadow stays off walls.
        const string HeldShadowShaderPath = InteractionArtFolder + "/SG_HeldShadowDecal.shadergraph";
        const float CapsuleHeight = 1.8f;
        const float CapsuleRadius = 0.35f;
        const float EyeHeight = 1.65f;

        [MenuItem("Sanctify/Player/Create Player Rig In Scene")]
        public static void CreatePlayerRig()
        {
            var movementSettings = GetOrCreateAsset<PlayerMovementSettings>("SO_PlayerMovement");
            var lookSettings = GetOrCreateAsset<PlayerLookSettings>("SO_PlayerLook");
            var headBobSettings = GetOrCreateAsset<HeadBobSettings>("SO_HeadBob");
            var grabSettings = GetOrCreateAsset<PlayerGrabSettings>("SO_PlayerGrab");
            var actions = FindInputActions();

            // ---- Root ----
            var root = new GameObject("Player");
            Undo.RegisterCreatedObjectUndo(root, "Create Player Rig");
            root.tag = "Player";
            root.transform.position = SceneSpawnPoint();

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.height = CapsuleHeight;
            capsule.radius = CapsuleRadius;
            capsule.center = new Vector3(0f, CapsuleHeight * 0.5f, 0f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            root.AddComponent<CharacterMotor>();

            // ---- Player subsystems (before the camera so GetComponentInParent finds them) ----
            var inputReader = root.AddComponent<PlayerInputReader>();
            SetReference(inputReader, "actions", actions);

            root.AddComponent<PlayerControlLock>();

            var movement = root.AddComponent<PlayerMovement>();
            SetReference(movement, "settings", movementSettings);

            var look = root.AddComponent<PlayerLook>();
            SetReference(look, "settings", lookSettings);

            root.AddComponent<PlayerCombat>();
            root.AddComponent<PlayerMagic>();

            // ---- Camera ----
            var cameraGo = new GameObject("PlayerCamera");
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;

            var rig = cameraGo.AddComponent<PlayerCameraRig>();
            SetFloat(rig, "eyeHeight", EyeHeight);
            cameraGo.AddComponent<FixedAspectRenderer>();
            var headBob = cameraGo.AddComponent<HeadBobModifier>();
            SetReference(headBob, "settings", headBobSettings);
            cameraGo.AddComponent<PeekModifier>();
            cameraGo.AddComponent<LandingDipModifier>();
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<DebugCrosshair>();

            var interactor = root.AddComponent<PlayerInteractor>();
            SetReference(interactor, "rayOrigin", cameraGo.transform);
            SetReference(interactor, "grabSettings", grabSettings);
            root.AddComponent<PlayerStance>();
            root.AddComponent<PlayerInteractMode>();
            var heldShadow = root.AddComponent<HeldObjectShadow>();
            SetReference(heldShadow, "material", GetOrCreateHeldShadowMaterial());

            var controller = root.AddComponent<PlayerController>();
            SetReference(controller, "cameraRig", rig);

            // ---- Housekeeping ----
            WarnAboutOtherMainCameras(camera);
            WarnAboutOtherListeners(cameraGo);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log($"Player rig created. Settings assets live in {SettingsFolder}.", root);
            if (actions == null)
                Debug.LogWarning("Sanctify.inputactions was not found. Assign an InputActionAsset on PlayerInputReader.", root);
        }

        [MenuItem("Sanctify/Player/Create Motor Test Arena")]
        public static void CreateTestArena()
        {
            var arena = new GameObject("Motor Test Arena");
            Undo.RegisterCreatedObjectUndo(arena, "Create Motor Test Arena");
            Transform parent = arena.transform;

            // Floor
            Block(parent, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f));

            // Stairs: 0.2 m risers, well under the 0.3 m step height
            for (int i = 0; i < 8; i++)
                Block(parent, $"Stair_{i}", new Vector3(6f, 0.1f + i * 0.2f, -6f + i * 0.6f), new Vector3(3f, 0.2f, 0.6f));
            // Landing at the top, with a ledge to walk off
            Block(parent, "StairLanding", new Vector3(6f, 0.8f, -0.2f), new Vector3(3f, 1.6f, 3f));

            // Too-tall step: 0.45 m, should block
            Block(parent, "TallStep", new Vector3(10f, 0.225f, -6f), new Vector3(2f, 0.45f, 2f));

            // Walkable ramp (30°) and steep ramp (60°)
            Ramp(parent, "Ramp30", new Vector3(-6f, 0f, -6f), 30f, 6f);
            Ramp(parent, "Ramp60", new Vector3(-12f, 0f, -6f), 60f, 4f);

            // Narrowing corridor: two walls converging to a 0.5 m gap, then a dead-end corner
            Wall(parent, "FunnelLeft", new Vector3(-2.5f, 1f, 8f), new Vector3(0.3f, 2f, 8f), 12f);
            Wall(parent, "FunnelRight", new Vector3(2.5f, 1f, 8f), new Vector3(0.3f, 2f, 8f), -12f);
            Block(parent, "FunnelEnd", new Vector3(0f, 1f, 12.5f), new Vector3(6f, 2f, 0.3f));

            // Acute corner (two walls meeting at ~60°)
            Wall(parent, "CornerA", new Vector3(12f, 1f, 8f), new Vector3(0.3f, 2f, 6f), 0f);
            Wall(parent, "CornerB", new Vector3(13.5f, 1f, 8f), new Vector3(0.3f, 2f, 6f), 60f);

            // Low ceiling (1.5 m clearance) to make sure step-up respects headroom
            Block(parent, "LowCeiling", new Vector3(-6f, 1.75f, 6f), new Vector3(4f, 0.5f, 4f));
            Block(parent, "StepUnderCeiling", new Vector3(-6f, 0.1f, 6f), new Vector3(1f, 0.2f, 1f));

            CreateInteractionTests(parent);
            CreateGrabTests(parent);
            CreateDragTests(parent);
            CreateDoorTests(parent);
            UpgradePlayerRigs();

            Selection.activeGameObject = arena;
            EditorSceneManager.MarkSceneDirty(arena.scene);
        }

        /// <summary>
        /// Pickup items south of the arena centre, between the ramps and the stairs. Covers items
        /// with and without a Rigidbody, stacks, custom focus text, a disabled item, one on the
        /// floor, and a slow one for testing interruptions.
        /// </summary>
        static void CreateInteractionTests(Transform arena)
        {
            var group = new GameObject("InteractionTests");
            group.transform.SetParent(arena, false);
            Transform parent = group.transform;

            // Table top at 0.75 m
            Block(parent, "PickupTable", new Vector3(0f, 0.375f, -7f), new Vector3(2.4f, 0.75f, 0.8f));
            Pickup(parent, PrimitiveType.Cube, "Rusty Key", new Vector3(-0.9f, 0.77f, -7f), new Vector3(0.2f, 0.04f, 0.08f));
            Pickup(parent, PrimitiveType.Sphere, "Healing Herb", new Vector3(-0.4f, 0.83f, -7f), Vector3.one * 0.15f, physics: true);
            Pickup(parent, PrimitiveType.Cube, "Gold Coins", new Vector3(0.1f, 0.785f, -7f), new Vector3(0.12f, 0.06f, 0.12f), physics: true, amount: 25);
            Pickup(parent, PrimitiveType.Cylinder, "Lantern", new Vector3(0.5f, 0.87f, -7f), new Vector3(0.15f, 0.12f, 0.15f), focusText: "Take the lantern");
            Pickup(parent, PrimitiveType.Capsule, "Sealed Idol", new Vector3(0.95f, 0.85f, -7f), new Vector3(0.12f, 0.1f, 0.12f), disabled: true);

            // On the floor: needs looking down, and falls back into place if a pickup is interrupted
            Pickup(parent, PrimitiveType.Cube, "Heavy Tome", new Vector3(1.8f, 0.045f, -5.5f), new Vector3(0.25f, 0.08f, 0.32f), physics: true);

            // Slow pickup on a pedestal, long enough to interrupt before the grab frame
            Block(parent, "RelicPedestal", new Vector3(-1.8f, 0.5f, -5.5f), new Vector3(0.5f, 1f, 0.5f));
            Pickup(parent, PrimitiveType.Cube, "Slow Relic", new Vector3(-1.8f, 1.08f, -5.5f), Vector3.one * 0.15f, physics: true, duration: 3f);

            // Stance tests: a shelf above standing eye height (tiptoe), and an item under the
            // arena's 1.5 m ceiling, which only a crouch fits beneath.
            Block(parent, "HighShelf", new Vector3(3.5f, 2.3f, -7f), new Vector3(1f, 0.1f, 0.5f));
            Pickup(parent, PrimitiveType.Cylinder, "Dusty Bottle", new Vector3(3.5f, 2.47f, -7f), new Vector3(0.1f, 0.12f, 0.1f), physics: true);
            Pickup(parent, PrimitiveType.Sphere, "Lost Ring", new Vector3(-6f, 0.25f, 6f), Vector3.one * 0.1f, physics: true);
        }

        /// <summary>
        /// Grabbable props south of the pickup table: light things on a table (one dense but
        /// made to throw far), heavier props on the floor that trail and barely throw, a stool
        /// made of several colliders, a long plank that swings from whichever end you grab, and
        /// a stack of boxes to throw things at.
        /// </summary>
        static void CreateGrabTests(Transform arena)
        {
            var standard = GetOrCreateAsset<GrabData>("SO_Grab_Default", InteractionSettingsFolder);
            var dense = GetOrCreateAsset<GrabData>("SO_Grab_Dense", InteractionSettingsFolder, data => data.throwMultiplier = 2.5f);

            var group = new GameObject("GrabTests");
            group.transform.SetParent(arena, false);
            Transform parent = group.transform;

            // Light things on a table, top at 0.75 m. The brick is heavy for its size but uses a
            // dense-object preset, so it still throws far.
            Block(parent, "GrabTable", new Vector3(0f, 0.375f, -10.5f), new Vector3(2f, 0.75f, 0.7f));
            GrabProp(parent, PrimitiveType.Cube, "Small Box", new Vector3(-0.6f, 0.88f, -10.5f), Vector3.one * 0.25f, 1f, standard);
            GrabProp(parent, PrimitiveType.Sphere, "Ball", new Vector3(0f, 0.865f, -10.5f), Vector3.one * 0.22f, 0.5f, standard);
            GrabProp(parent, PrimitiveType.Cube, "Brick", new Vector3(0.6f, 0.785f, -10.5f), new Vector3(0.2f, 0.06f, 0.1f), 1.5f, dense);

            // Heavier things on the floor: the crate trails the cursor and barely leaves the hand
            // when thrown. The chest is over the 20 kg lift limit, so it's dragged instead.
            GrabProp(parent, PrimitiveType.Cube, "Crate", new Vector3(-2.2f, 0.255f, -10.5f), Vector3.one * 0.5f, 8f, standard);
            GrabProp(parent, PrimitiveType.Cube, "Heavy Chest", new Vector3(2.4f, 0.255f, -10.5f), new Vector3(0.8f, 0.5f, 0.5f), 25f, standard);
            GrabProp(parent, PrimitiveType.Cube, "Plank", new Vector3(1.2f, 0.03f, -12.2f), new Vector3(0.1f, 0.05f, 1.2f), 2f, standard);
            Stool(parent, new Vector3(-1.2f, 0.005f, -12.2f), standard);

            // Something to throw at
            for (int i = 0; i < 3; i++)
                GrabProp(parent, PrimitiveType.Cube, $"Target Box {i + 1}", new Vector3(0f, 0.155f + i * 0.305f, -15f), Vector3.one * 0.3f, 1f, standard);
        }

        /// <summary>
        /// Props too heavy to lift, which are dragged: a crate on open floor, a block heavy enough
        /// to crawl, and a crate inside the narrowing corridor that jams against its walls.
        /// </summary>
        static void CreateDragTests(Transform arena)
        {
            var standard = GetOrCreateAsset<GrabData>("SO_Grab_Default", InteractionSettingsFolder);

            var group = new GameObject("DragTests");
            group.transform.SetParent(arena, false);
            Transform parent = group.transform;

            GrabProp(parent, PrimitiveType.Cube, "Heavy Crate", new Vector3(-4.5f, 0.405f, -12f), Vector3.one * 0.8f, 40f, standard).name = "Drag_HeavyCrate";
            GrabProp(parent, PrimitiveType.Cube, "Stone Block", new Vector3(4.5f, 0.305f, -12.5f), new Vector3(0.9f, 0.6f, 0.9f), 100f, standard).name = "Drag_StoneBlock";
            GrabProp(parent, PrimitiveType.Cube, "Corridor Crate", new Vector3(0f, 0.405f, 7f), Vector3.one * 0.8f, 40f, standard).name = "Drag_CorridorCrate";
        }

        /// <summary>
        /// Two doorways east of the stairs, facing north and south. The first door is free, with a
        /// 40 kg crate beside it to wedge against it. The second is bolted shut from the south.
        /// Between them lie iron props that break either door when thrown at it. South of those,
        /// a hut with no way in.
        /// </summary>
        static void CreateDoorTests(Transform arena)
        {
            var standard = GetOrCreateAsset<GrabData>("SO_Grab_Default", InteractionSettingsFolder);
            var dense = GetOrCreateAsset<GrabData>("SO_Grab_Dense", InteractionSettingsFolder, data => data.throwMultiplier = 2.5f);
            var fixture = GetOrCreateAsset<GrabData>("SO_Grab_Fixture", InteractionSettingsFolder, data => data.throwMultiplier = 0f);

            var group = new GameObject("DoorTests");
            group.transform.SetParent(arena, false);
            Transform parent = group.transform;

            Doorway(parent, "Door", new Vector3(15f, 0f, -4f));
            GrabProp(parent, PrimitiveType.Cube, "Heavy Crate", new Vector3(16.6f, 0.405f, -5.2f), Vector3.one * 0.8f, 40f, standard).name = "Door_BlockingCrate";

            Rigidbody bolted = Doorway(parent, "Bolted Door", new Vector3(15f, 0f, -9f));
            Bolt(parent, bolted, fixture);

            // South of the free door and on the bolted door's locked side, clear of both swings.
            // Dense, so they throw at 4 to 5 m/s, over the doors' 2.5 m/s Break Speed.
            var breakers = new[]
            {
                GrabProp(parent, PrimitiveType.Sphere, "Iron Ball", new Vector3(13.4f, 0.105f, -5.6f), Vector3.one * 0.2f, 4f, dense),
                GrabProp(parent, PrimitiveType.Sphere, "Iron Ball", new Vector3(13.4f, 0.105f, -6.2f), Vector3.one * 0.2f, 4f, dense),
                GrabProp(parent, PrimitiveType.Cube, "Iron Weight", new Vector3(13.4f, 0.105f, -6.8f), Vector3.one * 0.2f, 5f, dense),
            };
            foreach (var breaker in breakers)
                SetBool(breaker.GetComponent<PhysicsProp>(), "breaksDoors", true);

            Hut(parent, new Vector3(15f, 0f, -14.5f), fixture);
        }

        /// <summary>
        /// A 4 × 4 m hut with no way in: its door is bolted on the inside, and the only other
        /// opening is a window in the back wall. That's big enough to crouch through (1.3 m; a
        /// crouch is 1.2, standing 1.8) but its sill is 1.1 m up, far over the 0.3 m step.
        /// </summary>
        static void Hut(Transform parent, Vector3 centre, GrabData boltData)
        {
            const float half = 2f;
            const float wall = 0.2f;
            const float height = 2.4f; // level with the top of the doorway
            const float windowWidth = 0.9f;
            const float sill = 1.1f;   // the window runs from here up to the roof
            float west = centre.x - half;
            float east = centre.x + half;
            float front = centre.z + half - wall * 0.5f; // north: the door
            float back = centre.z - half + wall * 0.5f;  // south: the window
            float doorway = DoorWidth * 0.5f + DoorGap + PostWidth;

            void Span(string name, float x0, float x1, float y0, float y1, float z)
                => Block(parent, name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z), new Vector3(x1 - x0, y1 - y0, wall));

            Block(parent, "Hut Wall W", new Vector3(west + wall * 0.5f, height * 0.5f, centre.z), new Vector3(wall, height, half * 2f));
            Block(parent, "Hut Wall E", new Vector3(east - wall * 0.5f, height * 0.5f, centre.z), new Vector3(wall, height, half * 2f));
            Span("Hut Wall N", west, centre.x - doorway, 0f, height, front);
            Span("Hut Wall N", centre.x + doorway, east, 0f, height, front);
            Span("Hut Wall S", west, centre.x - windowWidth * 0.5f, 0f, height, back);
            Span("Hut Wall S", centre.x + windowWidth * 0.5f, east, 0f, height, back);
            Span("Hut Window Sill", centre.x - windowWidth * 0.5f, centre.x + windowWidth * 0.5f, 0f, sill, back);
            Block(parent, "Hut Roof", new Vector3(centre.x, height + 0.1f, centre.z), new Vector3(half * 2f, 0.2f, half * 2f));

            // The bolt goes on the leaf's south face: inside.
            Bolt(parent, Doorway(parent, "Hut Door", new Vector3(centre.x, 0f, front)), boltData);
        }

        const float DoorWidth = 0.9f;
        const float DoorHeight = 2f;
        const float DoorThickness = 0.06f;
        const float DoorGap = 0.05f;
        const float PostWidth = 0.3f;

        /// <summary>
        /// Two posts, a lintel, and a 25 kg door leaf hinged on the west post, opening both ways.
        /// The leaf's origin is on the hinge line, unscaled, with +x across the door.
        /// </summary>
        static Rigidbody Doorway(Transform parent, string name, Vector3 position)
        {
            float halfOpening = DoorWidth * 0.5f + DoorGap;
            Block(parent, $"{name} Post W", position + new Vector3(-halfOpening - PostWidth * 0.5f, 1.2f, 0f), new Vector3(PostWidth, 2.4f, 0.2f));
            Block(parent, $"{name} Post E", position + new Vector3(halfOpening + PostWidth * 0.5f, 1.2f, 0f), new Vector3(PostWidth, 2.4f, 0.2f));
            Block(parent, $"{name} Lintel", position + new Vector3(0f, 2.25f, 0f), new Vector3((halfOpening + PostWidth) * 2f, 0.3f, 0.2f));

            var leaf = new GameObject(name);
            leaf.transform.SetParent(parent, false);
            leaf.transform.localPosition = position + new Vector3(-DoorWidth * 0.5f, 0.02f, 0f);
            Block(leaf.transform, "Slab", new Vector3(DoorWidth * 0.5f, DoorHeight * 0.5f, 0f), new Vector3(DoorWidth, DoorHeight, DoorThickness));
            // Pokes through both faces, so it works from either side.
            var handle = Block(leaf.transform, "Handle", new Vector3(DoorWidth - 0.12f, 1f, 0f), new Vector3(0.12f, 0.05f, 0.22f));

            var body = leaf.AddComponent<Rigidbody>();
            body.mass = 25f;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var hinge = leaf.AddComponent<HingeJoint>();
            hinge.anchor = new Vector3(0f, DoorHeight * 0.5f, 0f);
            hinge.axis = Vector3.up;
            hinge.useLimits = true;
            hinge.limits = new JointLimits { min = -100f, max = 100f };
            hinge.useSpring = true; // spring 0, damper only: hinge friction
            hinge.spring = new JointSpring { damper = 3f };

            var door = leaf.AddComponent<Door>();
            SetReference(door, "handle", handle.GetComponent<Collider>());
            SetString(door, "focusText", name);
            return body;
        }

        /// <summary>
        /// A 1 kg bolt on the door's south face above the handle, slid home into the east post. It
        /// slides 10 cm along the door; its joint's connected anchor marks the middle of that.
        /// </summary>
        static void Bolt(Transform parent, Rigidbody door, GrabData data)
        {
            const float travel = 0.1f;
            // ponytail: a trigger, so the bolt can sit in the post without shoving the door; a solid keeper if bolts need to stop doors by themselves
            var home = new Vector3(DoorWidth + 0.02f, 1.35f, -(DoorThickness * 0.5f + 0.015f));
            var go = GrabProp(parent, PrimitiveType.Cube, "Bolt", parent.InverseTransformPoint(door.transform.TransformPoint(home)), new Vector3(0.12f, 0.03f, 0.03f), 1f, data);
            go.GetComponent<Collider>().isTrigger = true;

            var joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = door;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.axis = Vector3.right;
            joint.connectedAnchor = home - new Vector3(travel * 0.5f, 0f, 0f);
            joint.xMotion = ConfigurableJointMotion.Limited;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.linearLimit = new SoftJointLimit { limit = travel * 0.5f };
            joint.xDrive = new JointDrive { positionDamper = 1000f, maximumForce = float.MaxValue }; // friction when let go

            SetReference(door.GetComponent<Door>(), "bolt", joint);
        }

        /// <summary>One body, five colliders: every collider must stop touching the player while held.</summary>
        static void Stool(Transform parent, Vector3 position, GrabData data)
        {
            var root = new GameObject("Grab_Stool");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;

            Block(root.transform, "Seat", new Vector3(0f, 0.475f, 0f), new Vector3(0.4f, 0.05f, 0.4f));
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                    Block(root.transform, "Leg", new Vector3(x * 0.16f, 0.225f, z * 0.16f), new Vector3(0.05f, 0.45f, 0.05f));
            }

            AddGrabComponents(root, "Stool", 3f, data);
        }

        /// <summary>
        /// Brings player rigs built by an older version of this builder up to date: adds components
        /// that didn't exist then and assigns settings assets they're missing. Safe to run repeatedly.
        /// </summary>
        [MenuItem("Sanctify/Player/Upgrade Player Rigs In Scene")]
        public static void UpgradePlayerRigs()
        {
            var grabSettings = GetOrCreateAsset<PlayerGrabSettings>("SO_PlayerGrab");
            var heldShadowMaterial = GetOrCreateHeldShadowMaterial();

            foreach (var controller in Object.FindObjectsByType<PlayerController>())
            {
                var root = controller.gameObject;
                AddIfMissing<PlayerStance>(root);
                AddIfMissing<PlayerInteractMode>(root);
                AddIfMissing<HeldObjectShadow>(root);

                var interactor = root.GetComponent<PlayerInteractor>();
                if (interactor != null)
                    SetReferenceIfEmpty(interactor, "grabSettings", grabSettings);
                if (heldShadowMaterial != null)
                    SetReferenceIfEmpty(root.GetComponent<HeldObjectShadow>(), "material", heldShadowMaterial);

                EditorSceneManager.MarkSceneDirty(root.scene);
            }
        }

        static void AddIfMissing<T>(GameObject go) where T : Component
        {
            if (go.GetComponent<T>() != null)
                return;
            Undo.AddComponent<T>(go);
            Debug.Log($"Added {typeof(T).Name} to '{go.name}'.", go);
        }

        static void SetReferenceIfEmpty(Component component, string fieldName, Object value)
        {
            var so = new SerializedObject(component);
            var property = so.FindProperty(fieldName);
            if (property == null || property.objectReferenceValue != null)
                return;
            property.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            Debug.Log($"Assigned {value.name} to {component.GetType().Name} on '{component.name}'.", component);
        }

        // ------------------------------------------------------------------

        static GameObject GrabProp(Transform parent, PrimitiveType shape, string displayName, Vector3 position, Vector3 size, float mass, GrabData data)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = $"Grab_{displayName.Replace(" ", "")}";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            AddGrabComponents(go, displayName, mass, data);
            return go;
        }

        static void AddGrabComponents(GameObject go, string displayName, float mass, GrabData data)
        {
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Anything can be thrown, and small fast things tunnel through walls on Discrete.
            // Speculative covers static and dynamic contacts and is the cheapest continuous mode.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var prop = go.AddComponent<PhysicsProp>();
            SetReference(prop, "grab", data);
            SetString(prop, "focusText", displayName);
        }

        static GameObject Pickup(Transform parent, PrimitiveType shape, string displayName, Vector3 position, Vector3 size,
            bool physics = false, int amount = 1, float duration = 0.6f, string focusText = "", bool disabled = false)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = $"Pickup_{displayName.Replace(" ", "")}";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            if (physics)
                go.AddComponent<Rigidbody>();

            var item = go.AddComponent<PickupItem>();
            SetString(item, "displayName", displayName);
            SetInt(item, "amount", amount);
            SetFloat(item, "duration", duration);
            SetString(item, "focusText", focusText);
            SetBool(item, "interactionDisabled", disabled);
            return go;
        }

        static GameObject Block(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            return go;
        }

        static GameObject Wall(Transform parent, string name, Vector3 position, Vector3 size, float yawDegrees)
        {
            var go = Block(parent, name, position, size);
            go.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            return go;
        }

        static GameObject Ramp(Transform parent, string name, Vector3 footPosition, float angleDegrees, float length)
        {
            var go = Block(parent, name, Vector3.zero, new Vector3(3f, 0.2f, length));
            float rad = angleDegrees * Mathf.Deg2Rad;
            // Rotate about the near edge so the foot of the ramp sits on the floor.
            go.transform.localRotation = Quaternion.Euler(-angleDegrees, 0f, 0f);
            go.transform.localPosition = footPosition + new Vector3(0f, Mathf.Sin(rad) * length * 0.5f, Mathf.Cos(rad) * length * 0.5f);
            return go;
        }

        static Vector3 SceneSpawnPoint()
        {
            var view = SceneView.lastActiveSceneView;
            return view != null ? view.pivot : Vector3.zero;
        }

        /// <param name="configureNew">Applied only when the asset is created, so existing tuning is never overwritten.</param>
        static T GetOrCreateAsset<T>(string fileName, string folder = SettingsFolder, System.Action<T> configureNew = null) where T : ScriptableObject
        {
            EnsureFolder(folder);
            string path = $"{folder}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            configureNew?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// Decal material for <see cref="HeldObjectShadow"/>, with a generated soft blob for its
        /// texture. Made once; later edits to the material or texture are kept.
        /// </summary>
        static Material GetOrCreateHeldShadowMaterial()
        {
            string path = $"{InteractionArtFolder}/M_HeldShadow.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(HeldShadowShaderPath);
            if (shader == null)
            {
                Debug.LogError($"No decal shader at {HeldShadowShaderPath}, so held objects will cast no shadow.");
                return null;
            }

            EnsureFolder(InteractionArtFolder);
            var material = new Material(shader) { enableInstancing = true };
            material.SetTexture("Base_Map", GetOrCreateBlobTexture($"{InteractionArtFolder}/T_HeldShadow.png"));
            material.SetFloat("Normal_Blend", 0f); // darken only; leave the surface's normals alone
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>Black, with a soft round falloff in alpha that reaches zero before the edges.</summary>
        static Texture2D GetOrCreateBlobTexture(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - radius) / radius;
                    float dy = (y + 0.5f - radius) / radius;
                    float falloff = Mathf.Clamp01(1f - (dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(falloff * falloff * 255f));
                }
            }
            texture.SetPixels32(pixels);
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // smooth gradient, no banding
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static InputActionAsset FindInputActions()
        {
            foreach (string guid in AssetDatabase.FindAssets("Sanctify t:InputActionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Sanctify.inputactions"))
                    return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            }
            return null;
        }

        static void SetReference(Component component, string fieldName, Object value) => SetProperty(component, fieldName, p => p.objectReferenceValue = value);
        static void SetFloat(Component component, string fieldName, float value) => SetProperty(component, fieldName, p => p.floatValue = value);
        static void SetInt(Component component, string fieldName, int value) => SetProperty(component, fieldName, p => p.intValue = value);
        static void SetBool(Component component, string fieldName, bool value) => SetProperty(component, fieldName, p => p.boolValue = value);
        static void SetString(Component component, string fieldName, string value) => SetProperty(component, fieldName, p => p.stringValue = value);

        static void SetProperty(Component component, string fieldName, System.Action<SerializedProperty> assign)
        {
            var so = new SerializedObject(component);
            var property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"{component.GetType().Name} has no serialized field '{fieldName}'.", component);
                return;
            }
            assign(property);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WarnAboutOtherMainCameras(Camera ours)
        {
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam != ours && cam.CompareTag("MainCamera"))
                    Debug.LogWarning($"'{cam.name}' is also tagged MainCamera. Disable or delete it so the player camera is used.", cam);
            }
        }

        static void WarnAboutOtherListeners(GameObject ours)
        {
            foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (listener.gameObject != ours)
                    Debug.LogWarning($"'{listener.name}' also has an AudioListener. Unity only wants one.", listener);
            }
        }
    }
}
