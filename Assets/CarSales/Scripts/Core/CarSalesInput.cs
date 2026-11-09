using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSales
{
    /// <summary>
    /// Resolves input actions safely. If an InputActionReference is missing or broken (for example after the
    /// .inputactions file was replaced and got a new GUID), built-in default bindings are used instead of crashing.
    /// </summary>
    public static class CarSalesInput
    {
        static Dictionary<string, InputAction> fallback;
        static InputActionMap fallbackMap;
        static readonly HashSet<string> warned = new HashSet<string>();
        public const string OverridesKey = "CarSales.BindingOverrides";

        /// <summary>The game's input actions (the linked .inputactions asset), once any linked action has been seen.</summary>
        public static InputActionAsset Asset { get; private set; }

        /// <summary>The actions rebinding works on: the linked asset, or the built-in defaults.</summary>
        public static IInputActionCollection2 Collection { get { if (Asset != null) return Asset; if (fallback == null) BuildFallback(); return fallbackMap; } }

        static void Register(InputActionAsset asset)
        {
            if (asset == null || Asset == asset) return;
            Asset = asset;
            LoadOverrides();
        }

        /// <summary>Applies saved key bindings (Settings → Controls).</summary>
        public static void LoadOverrides()
        {
            string json = PlayerPrefs.GetString(OverridesKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try { Collection.LoadBindingOverridesFromJson(json); } catch (System.Exception e) { Debug.LogWarning("[CarSales] Saved key bindings could not be loaded: " + e.Message); }
        }

        public static void SaveOverrides()
        {
            PlayerPrefs.SetString(OverridesKey, Collection.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public static void ResetOverrides()
        {
            Collection.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(OverridesKey);
        }

        /// <summary>Every action rebinding can show (the asset's first map, or the defaults).</summary>
        public static IEnumerable<InputAction> AllActions()
        {
            if (Asset != null) { foreach (var m in Asset.actionMaps) foreach (var a in m.actions) yield return a; yield break; }
            if (fallback == null) BuildFallback();
            foreach (var a in fallback.Values) yield return a;
        }

        /// <summary>Returns the referenced action, or a built-in default with the same name. Always enabled.</summary>
        public static InputAction Resolve(InputActionReference reference, string actionName)
        {
            InputAction action = null;
            if (reference != null)
            {
                try { action = reference.action; } catch { action = null; }
            }
            if (action != null && action.actionMap != null) Register(action.actionMap.asset);
            // Phase 10: look the name up in the game's own actions before falling back (so rebinding applies everywhere).
            if (action == null && Asset != null) action = Asset.FindAction(actionName);
            if (action == null)
            {
                action = Fallback(actionName);
                if (action != null && warned.Add(actionName))
                    Debug.LogWarning($"[CarSales] Input action '{actionName}' is not linked - using built-in default keys. " +
                                     "Open this scene and run Tools > Car Sales > Re-link Input Actions (or 2. Build Everything).");
            }
            if (action != null && !action.enabled) action.Enable();
            return action;
        }

        static InputAction Fallback(string name)
        {
            if (fallback == null) BuildFallback();
            if (fallback.TryGetValue(name, out var a)) return a;
            Debug.LogError($"[CarSales] Unknown input action '{name}'.");
            return null;
        }

        static void BuildFallback()
        {
            fallback = new Dictionary<string, InputAction>();
            fallbackMap = new InputActionMap("CarSalesDefaults");

            var move = fallbackMap.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            fallback["Move"] = move;

            var look = fallbackMap.AddAction("Look", InputActionType.Value, "<Mouse>/delta", expectedControlLayout: "Vector2");
            look.AddBinding("<Gamepad>/rightStick", processors: "scaleVector2(x=12,y=8)");
            fallback["Look"] = look;

            fallback["Jump"] = Button("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            fallback["Sprint"] = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            fallback["Interact"] = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonWest");
            fallback["ToggleShop"] = Button("ToggleShop", "<Keyboard>/o", "<Gamepad>/select");
            fallback["CloseMenu"] = Button("CloseMenu", "<Keyboard>/escape", "<Gamepad>/buttonEast");
            fallback["Inventory"] = Button("Inventory", "<Keyboard>/tab", "<Gamepad>/dpad/left");
            fallback["Vehicle"] = Button("Vehicle", "<Keyboard>/f", "<Gamepad>/buttonNorth");
            fallback["Map"] = Button("Map", "<Keyboard>/m", "<Gamepad>/dpad/up");
            fallback["Phone"] = Button("Phone", "<Keyboard>/p", "<Gamepad>/dpad/down");
            fallback["CameraView"] = Button("CameraView", "<Keyboard>/c", "<Gamepad>/rightStickPress");
            fallback["GearUp"] = Button("GearUp", "<Keyboard>/leftShift", "<Gamepad>/rightShoulder");
            fallback["GearDown"] = Button("GearDown", "<Keyboard>/leftCtrl", "<Gamepad>/leftShoulder");
            if (Asset == null) LoadOverrides();
        }

        static InputAction Button(string name, string keyboard, string gamepad)
        {
            var a = fallbackMap.AddAction(name, InputActionType.Button, keyboard);
            a.AddBinding(gamepad);
            return a;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (fallback != null) foreach (var a in fallback.Values) a.Dispose();
            fallback = null;
            fallbackMap = null;
            Asset = null;
            warned.Clear();
        }
    }
}
