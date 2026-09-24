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

            // Heavier things on the floor: they trail the cursor and barely leave the hand when thrown.
            // The chest is past the full-slowdown mass and well past what Max Pull Force moves briskly.
            GrabProp(parent, PrimitiveType.Cube, "Crate", new Vector3(-2.2f, 0.255f, -10.5f), Vector3.one * 0.5f, 8f, standard);
            GrabProp(parent, PrimitiveType.Cube, "Heavy Chest", new Vector3(2.4f, 0.255f, -10.5f), new Vector3(0.8f, 0.5f, 0.5f), 25f, standard);
            GrabProp(parent, PrimitiveType.Cube, "Plank", new Vector3(1.2f, 0.03f, -12.2f), new Vector3(0.1f, 0.05f, 1.2f), 2f, standard);
            Stool(parent, new Vector3(-1.2f, 0.005f, -12.2f), standard);

            // Something to throw at
            for (int i = 0; i < 3; i++)
                GrabProp(parent, PrimitiveType.Cube, $"Target Box {i + 1}", new Vector3(0f, 0.155f + i * 0.305f, -15f), Vector3.one * 0.3f, 1f, standard);
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

            foreach (var controller in Object.FindObjectsByType<PlayerController>())
            {
                var root = controller.gameObject;
                AddIfMissing<PlayerStance>(root);
                AddIfMissing<PlayerInteractMode>(root);

                var interactor = root.GetComponent<PlayerInteractor>();
                if (interactor != null)
                    SetReferenceIfEmpty(interactor, "grabSettings", grabSettings);

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
