using System;
using System.Collections;
using System.Reflection;
using HutongGames.PlayMaker;
using Modding;
using UnityEngine;

namespace KO.HollowKnight8.StagFix
{
    public sealed class Local8StagFix : Mod
    {
        private PlayMakerFSM _list;
        private float _nextSearch;
        private Type _inputManagerType;
        private PropertyInfo _devicesProp;
        private bool _loggedOpen;

        public Local8StagFix() : base("Local8 Stag Menu Fix") { }
        public override string GetVersion() => "0.3.25-alpha40-stagfix";

        public override void Initialize()
        {
            ModHooks.HeroUpdateHook += Tick;
            Log("alpha40 stag menu input fix loaded");
        }

        private void Tick()
        {
            var ui = FindList();
            if (ui == null)
            {
                _loggedOpen = false;
                return;
            }

            if (!_loggedOpen)
            {
                Log("STAG MENU open state=" + ui.ActiveStateName);
                _loggedOpen = true;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            {
                Send(ui, "UP", "keyboard");
                return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                Send(ui, "DOWN", "keyboard");
                return;
            }

            foreach (var device in GetDevices())
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

        private PlayMakerFSM FindList()
        {
            if (_list != null && _list.gameObject != null && _list.gameObject.activeInHierarchy)
                return _list;

            _list = null;
            if (Time.unscaledTime < _nextSearch) return null;
            _nextSearch = Time.unscaledTime + 0.05f;

            var map = GameObject.Find("Stag Map");
            if (map == null || !map.activeInHierarchy) return null;

            Transform child = map.transform.Find("UI List Stag");
            if (child == null)
            {
                foreach (var t in map.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name == "UI List Stag")
                    {
                        child = t;
                        break;
                    }
                }
            }

            if (child == null || !child.gameObject.activeInHierarchy) return null;
            _list = PlayMakerFSM.FindFsmOnGameObject(child.gameObject, "ui_list");
            return _list;
        }

        private void Send(PlayMakerFSM ui, string evt, string who)
        {
            try
            {
                Log("STAG MENU " + evt + " by " + who + " state=" + ui.ActiveStateName);
                ui.SendEvent(evt);
            }
            catch (Exception ex)
            {
                LogError("STAG MENU " + evt + " failed: " + ex);
            }
        }

        private IEnumerable GetDevices()
        {
            try
            {
                if (_inputManagerType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("InControl.InputManager", false);
                        if (t != null)
                        {
                            _inputManagerType = t;
                            _devicesProp = t.GetProperty("Devices", BindingFlags.Public | BindingFlags.Static);
                            break;
                        }
                    }
                }

                if (_devicesProp == null) yield break;
                var devices = _devicesProp.GetValue(null, null) as IEnumerable;
                if (devices == null) yield break;
                foreach (var d in devices)
                    if (d != null) yield return d;
            }
            finally { }
        }

        private static bool ControlPressed(object device, string property)
        {
            try
            {
                var p = device.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                var control = p == null ? null : p.GetValue(device, null);
                if (control == null) return false;
                var wp = control.GetType().GetProperty("WasPressed", BindingFlags.Public | BindingFlags.Instance);
                return wp != null && (bool)wp.GetValue(control, null);
            }
            catch { return false; }
        }

        private static string DeviceName(object device)
        {
            try
            {
                var p = device.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                return "controller:" + (p == null ? "?" : Convert.ToString(p.GetValue(device, null)));
            }
            catch { return "controller"; }
        }
    }
}