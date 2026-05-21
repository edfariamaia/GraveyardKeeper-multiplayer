using System;
using Pathfinding;
using Steamworks;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Manages the visual representation of the remote player in the local world.
    //
    // Strategy: clone the local player's GameObject, then strip every component that
    // would cause the clone to behave like a real game actor (AI pathfinding, physics,
    // game logic, etc.). What remains is a pure visual puppet that we move by directly
    // setting transform.position whenever a Position packet arrives.
    //
    // The clone is kept alive across scene loads with DontDestroyOnLoad, and is
    // destroyed when the player returns to the main menu.
    public class RemotePlayerAvatar : MonoBehaviour
    {
        // Creates the singleton MonoBehaviour container. Called from PatchStartGame.
        public static void Create()
        {
            GameObject go = new GameObject("RemotePlayerAvatar");
            Instance = go.AddComponent<RemotePlayerAvatar>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // Instantiates and strips the avatar from the local player's GameObject.
        // Safe to call multiple times — skips if already initialised or if the game
        // hasn't finished loading yet (game_started == false).
        public void InitAvatar()
        {
            if (_initialized) return;
            if (!MainGame.game_started) return;

            // Guard: player object must exist before we can clone it
            if (MainGame.me?.player?.gameObject == null) return;

            _avatarObject = UnityEngine.Object.Instantiate<GameObject>(MainGame.me.player.gameObject);
            _avatarObject.name = "RemotePlayer";

            // --- Remove components that must not run on the remote avatar ---
            // SimpleSmoothModifierXY must be removed BEFORE Seeker due to a dependency
            foreach (Type t in new Type[]
            {
                typeof(SimpleSmoothModifierXY),   // A* smooth movement modifier
                typeof(Seeker),                    // A* pathfinding agent
                typeof(PlayerComponent),           // Local player input & logic
                typeof(ChunkedGameObject),         // Chunk-loading trigger
                typeof(WorldGameObject),           // World object registration
                typeof(RoundAndSortComponent),     // Sprite sorting helper
                typeof(CustomNetworkAnimatorSync), // Network animator (not ours)
                typeof(BaseCharacterIdle),         // Idle animation controller
                typeof(CurvedAttack),              // Melee attack logic
                typeof(SmartAnimationController),  // Animation state machine
                typeof(AnimationEvent),            // Animation event emitter
                typeof(ObjectDynamicShadow),       // Shadow blob
                typeof(ObjectDynamicShadowChild)   // Shadow blob child
            })
            {
                foreach (Component c in _avatarObject.GetComponentsInChildren(t))
                    UnityEngine.Object.Destroy(c);
            }

            // Remove physics colliders by type name string to avoid requiring the
            // Physics2D module assembly reference
            foreach (Component c in _avatarObject.GetComponentsInChildren<Component>())
            {
                string name = c.GetType().Name;
                if (name == "CircleCollider2D" || name == "BoxCollider2D" || name == "Rigidbody2D")
                    UnityEngine.Object.Destroy(c);
            }

            UnityEngine.Object.DontDestroyOnLoad(_avatarObject);
            _avatarTransform = _avatarObject.transform;
            _initialized = true;

            string playerName = SteamFriends.GetFriendPersonaName(NetworkManager.RemoteID);
            Plugin.Log.LogInfo("Avatar created: " + playerName);
        }

        // Moves the avatar to the position received in a Position packet.
        // If the avatar hasn't been initialised yet, tries to initialise it first.
        public void UpdatePosition(Vector3 pos)
        {
            if (!_initialized)
            {
                InitAvatar();
                return;
            }

            if (_avatarTransform != null)
                _avatarTransform.position = pos;
        }

        // Destroys the avatar GameObject and resets the initialisation flag.
        // Called when the local player returns to the main menu.
        public void DestroyAvatar()
        {
            if (_avatarObject != null)
                UnityEngine.Object.Destroy(_avatarObject);

            _initialized = false;
            Plugin.Log.LogInfo("Avatar destroyed.");
        }

        public static RemotePlayerAvatar Instance;

        // The cloned GameObject used as the visual puppet
        private GameObject _avatarObject;

        // Cached transform reference for fast position updates every frame
        private Transform _avatarTransform;

        // True once the avatar has been successfully cloned and stripped
        private bool _initialized = false;
    }
}
