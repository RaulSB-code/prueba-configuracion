using System;
using System.Collections;
using System.Reflection;
using Modding;

namespace KO.HollowKnight8.StagFix
{
    public sealed class Local8StagFix : Mod
    {
        private object _uiList;
        private Type _gameObjectType;
        private Type _playMakerType;
        private Type _inputType;
        private Type _keyCodeType;
        private Type _inputManagerType;
        private PropertyInfo _devicesProperty;
        private MethodInfo _findGameObject;
        private MethodInfo _getComponentsByType;
        private MethodInfo _getKeyDown;
        private bool _menuLogged;

        public Local8StagFix() : base("Local8 Stag Menu Fix") { }

        public override string GetVersion()
        {
            return "0.3.25-alpha40-stagfix";
        }

        public override void Initialize()
        {
            ModHooks.HeroUpdateHook += Tick;
            Log("alpha40 Stag menu fix loaded");
        }

        private void Tick()
        {
            try
            {
                object ui = FindUiList();
                if (ui == null)
                {
                    _menuLogged = false;
                    return;
                }

                if (!_menuLogged)
                {
                    Log("STAG MENU open");
                    _menuLogged = true;
                }

                if (KeyPressed("UpArrow") || KeyPressed("W"))
                {
                    Send(ui, "UP", "keyboard");
                    return;
                }

                if (KeyPressed("DownArrow") || KeyPressed("S"))
                {
                    Send(ui, "DOWN", "keyboard");
                    return;
                }

                foreach (object device in GetDevices())
                {
                    if (ControlPressed(device, "DPadUp") || ControlPressed(device, "LeftStickUp"))
                    {
                        Send(ui, "UP", DeviceName(device));
                        return;
                    }

                    if (ControlPressed(device, "DPadDown") || ControlPressed(device, "LeftStickDown"))
                    {
                        Send(ui, "DOWN", DeviceName(device));
                        return;
                    }

                    if (ControlPressed(device, "Action1"))
                    {
                        Send(ui, "SELECTION MADE", DeviceName(device));
                        return;
                    }

                    if (ControlPressed(device, "Action2"))
                    {
                        Send(ui, "SELECTION MADE CANCEL", DeviceName(device));
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _uiList = null;
                LogError("STAG MENU tick failed: " + ex);
            }
        }

        private object FindUiList()
        {
            if (_uiList != null)
            {
                try
                {
                    object go = GetProperty(_uiList, "gameObject");
                    if (go != null)
                    {
                        object active = GetProperty(go, "activeInHierarchy");
                        if (active is bool && (bool)active) return _uiList;
                    }
                }
                catch { }
                _uiList = null;
            }

            ResolveUnityAndPlayMaker();
            if (_findGameObject == null || _playMakerType == null) return null;

            object gameObject = _findGameObject.Invoke(null, new object[] { "Stag Map/UI List Stag" });
            if (gameObject == null)
                gameObject = _findGameObject.Invoke(null, new object[] { "UI List Stag" });
            if (gameObject == null) return null;

            if (_getComponentsByType == null)
            {
                _getComponentsByType = _gameObjectType.GetMethod(
                    "GetComponents",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(Type) },
                    null);
            }

            if (_getComponentsByType == null) return null;

            Array components = _getComponentsByType.Invoke(gameObject, new object[] { _playMakerType }) as Array;
            if (components == null) return null;

            foreach (object component in components)
            {
                string fsmName = Convert.ToString(GetProperty(component, "FsmName"));
                if (string.Equals(fsmName, "ui_list", StringComparison.OrdinalIgnoreCase))
                {
                    _uiList = component;
                    Log("STAG MENU ui_list found");
                    return _uiList;
                }
            }

            return null;
        }

        private void ResolveUnityAndPlayMaker()
        {
            if (_gameObjectType != null && _playMakerType != null) return;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_gameObjectType == null)
                    _gameObjectType = assembly.GetType("UnityEngine.GameObject", false);
                if (_playMakerType == null)
                    _playMakerType = assembly.GetType("HutongGames.PlayMaker.PlayMakerFSM", false);
                if (_inputType == null)
                    _inputType = assembly.GetType("UnityEngine.Input", false);
                if (_keyCodeType == null)
                    _keyCodeType = assembly.GetType("UnityEngine.KeyCode", false);
                if (_inputManagerType == null)
                    _inputManagerType = assembly.GetType("InControl.InputManager", false);
            }

            if (_gameObjectType != null && _findGameObject == null)
            {
                _findGameObject = _gameObjectType.GetMethod(
                    "Find",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(string) },
                    null);
            }

            if (_inputType != null && _keyCodeType != null && _getKeyDown == null)
            {
                _getKeyDown = _inputType.GetMethod(
                    "GetKeyDown",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { _keyCodeType },
                    null);
            }

            if (_inputManagerType != null && _devicesProperty == null)
                _devicesProperty = _inputManagerType.GetProperty("Devices", BindingFlags.Public | BindingFlags.Static);
        }

        private bool KeyPressed(string key)
        {
            try
            {
                ResolveUnityAndPlayMaker();
                if (_getKeyDown == null || _keyCodeType == null) return false;
                object code = Enum.Parse(_keyCodeType, key, true);
                object value = _getKeyDown.Invoke(null, new object[] { code });
                return value is bool && (bool)value;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable GetDevices()
        {
            ResolveUnityAndPlayMaker();
            if (_devicesProperty == null) yield break;

            IEnumerable devices = null;
            try
            {
                devices = _devicesProperty.GetValue(null, null) as IEnumerable;
            }
            catch { }

            if (devices == null) yield break;
            foreach (object device in devices)
                if (device != null) yield return device;
        }

        private static bool ControlPressed(object device, string propertyName)
        {
            try
            {
                PropertyInfo p = device.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                object control = p == null ? null : p.GetValue(device, null);
                if (control == null) return false;

                PropertyInfo wasPressed = control.GetType().GetProperty("WasPressed", BindingFlags.Public | BindingFlags.Instance);
                object value = wasPressed == null ? null : wasPressed.GetValue(control, null);
                return value is bool && (bool)value;
            }
            catch
            {
                return false;
            }
        }

        private static string DeviceName(object device)
        {
            try
            {
                PropertyInfo p = device.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                return "controller:" + Convert.ToString(p == null ? null : p.GetValue(device, null));
            }
            catch
            {
                return "controller";
            }
        }

        private void Send(object ui, string eventName, string source)
        {
            try
            {
                MethodInfo method = ui.GetType().GetMethod(
                    "SendEvent",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (method == null)
                {
                    LogError("STAG MENU SendEvent method not found");
                    return;
                }

                Log("STAG MENU " + eventName + " by " + source);
                method.Invoke(ui, new object[] { eventName });
            }
            catch (Exception ex)
            {
                _uiList = null;
                LogError("STAG MENU " + eventName + " failed: " + ex);
            }
        }

        private static object GetProperty(object target, string name)
        {
            if (target == null) return null;
            PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p == null ? null : p.GetValue(target, null);
        }
    }
}
