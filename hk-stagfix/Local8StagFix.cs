using System;
using System.Collections;
using System.Reflection;
using Modding;

namespace KO.HollowKnight8.DreamFix
{
    public sealed class Local8DreamTransitionFix : Mod
    {
        private bool armed;
        private string lastTarget;
        private int sceneTicks;
        private bool wakeSent;
        private Type playMakerType;
        private Type gameObjectType;
        private Type objectType;
        private MethodInfo findObjectsOfType;
        private MethodInfo getComponentsByType;

        public Local8DreamTransitionFix() : base("Local8 Dream Transition Fix") { }

        public override string GetVersion()
        {
            return "0.3.47-alpha83-dreamfix";
        }

        public override void Initialize()
        {
            ModHooks.BeforeSceneLoadHook += BeforeSceneLoad;
            ModHooks.SceneChanged += SceneChanged;
            ModHooks.HeroUpdateHook += Tick;
            Log("alpha83 Dream transition proxy fix loaded");
        }

        private string BeforeSceneLoad(string scene)
        {
            try
            {
                lastTarget = scene ?? "";
                if (!IsDreamNailCollection(scene))
                    return scene;

                object primary = GetPrimaryHero();
                if (primary == null)
                {
                    LogError("DREAMFIX pre-load: primary hero missing");
                    return scene;
                }

                bool wasReturning = GetDreamBool(primary, "Dream Returning");
                if (!wasReturning)
                {
                    SetDreamBool(primary, "Dream Returning", true);
                    TryEnterWithoutInput(primary);
                    armed = true;
                    wakeSent = false;
                    sceneTicks = 0;
                    Log("DREAMFIX mirrored Dream Returning to P1 before " + scene);
                }
                else
                {
                    armed = false;
                    Log("DREAMFIX P1 already Dream Returning before " + scene);
                }
            }
            catch (Exception ex)
            {
                LogError("DREAMFIX pre-load failed: " + ex);
            }
            return scene;
        }

        private void SceneChanged(string scene)
        {
            if (!armed) return;
            if (IsDreamNailCollection(scene))
            {
                sceneTicks = 0;
                wakeSent = false;
                Log("DREAMFIX entered " + scene + "; waiting for vanilla Dream Return");
            }
            else if (!string.Equals(scene, lastTarget, StringComparison.OrdinalIgnoreCase))
            {
                armed = false;
            }
        }

        private void Tick()
        {
            if (!armed) return;

            string current = CurrentSceneName();
            if (!IsDreamNailCollection(current)) return;

            sceneTicks++;

            // Give the native Dream Return FSM plenty of time to send DREAM WAKE itself.
            if (sceneTicks < 20) return;

            try
            {
                object control = FindFsm("Witch Control", "Control");
                if (control == null) return;

                string state = ActiveStateName(control);
                if (!wakeSent && string.Equals(state, "Idle", StringComparison.OrdinalIgnoreCase))
                {
                    SendEvent(control, "DREAM WAKE");
                    wakeSent = true;
                    Log("DREAMFIX fallback DREAM WAKE sent to Witch Control");
                    return;
                }

                // Once the scene has left Idle/first waiting state, the native sequence owns everything.
                if (wakeSent && !string.Equals(state, "Idle", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(state, "Pause", StringComparison.OrdinalIgnoreCase))
                {
                    Log("DREAMFIX native dream sequence running state=" + state);
                    armed = false;
                    return;
                }

                // Final fallback only for the exact broken P2 path: make sure the primary
                // still carries the flag if another system reset it during roster respawn.
                if (sceneTicks == 120)
                {
                    object primary = GetPrimaryHero();
                    if (primary != null && !GetDreamBool(primary, "Dream Returning"))
                    {
                        SetDreamBool(primary, "Dream Returning", true);
                        TryEnterWithoutInput(primary);
                        Log("DREAMFIX restored P1 Dream Returning after scene spawn");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("DREAMFIX tick failed: " + ex);
                armed = false;
            }
        }

        private static bool IsDreamNailCollection(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return false;
            string n = scene.Replace("_", "").ToLowerInvariant();
            return n.Contains("dreamnailcollection");
        }

        private object GetPrimaryHero()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType("HeroController", false);
                if (t == null) continue;

                PropertyInfo p = t.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null)
                {
                    object v = p.GetValue(null, null);
                    if (v != null) return v;
                }

                FieldInfo f = t.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null)
                {
                    object v = f.GetValue(null);
                    if (v != null) return v;
                }
            }
            return null;
        }

        private bool GetDreamBool(object hero, string variable)
        {
            object fsm = FindFsmOnHero(hero, "Dream Return");
            if (fsm == null) return false;
            object vars = GetProperty(fsm, "FsmVariables");
            if (vars == null) return false;
            MethodInfo find = vars.GetType().GetMethod("FindFsmBool", new Type[] { typeof(string) });
            if (find == null) return false;
            object b = find.Invoke(vars, new object[] { variable });
            if (b == null) return false;
            object value = GetProperty(b, "Value");
            return value is bool && (bool)value;
        }

        private void SetDreamBool(object hero, string variable, bool value)
        {
            object fsm = FindFsmOnHero(hero, "Dream Return");
            if (fsm == null) throw new InvalidOperationException("Dream Return FSM missing on P1");
            object vars = GetProperty(fsm, "FsmVariables");
            if (vars == null) throw new InvalidOperationException("Dream Return FsmVariables missing");
            MethodInfo find = vars.GetType().GetMethod("FindFsmBool", new Type[] { typeof(string) });
            if (find == null) throw new InvalidOperationException("FindFsmBool missing");
            object b = find.Invoke(vars, new object[] { variable });
            if (b == null) throw new InvalidOperationException(variable + " missing");
            PropertyInfo vp = b.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (vp == null || !vp.CanWrite) throw new InvalidOperationException(variable + ".Value not writable");
            vp.SetValue(b, value, null);
        }

        private object FindFsmOnHero(object hero, string fsmName)
        {
            object go = GetProperty(hero, "gameObject");
            if (go == null) return null;
            ResolveTypes();
            if (playMakerType == null || gameObjectType == null) return null;
            if (getComponentsByType == null)
            {
                getComponentsByType = gameObjectType.GetMethod(
                    "GetComponents",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(Type) },
                    null);
            }
            if (getComponentsByType == null) return null;
            Array fsms = getComponentsByType.Invoke(go, new object[] { playMakerType }) as Array;
            if (fsms == null) return null;
            foreach (object fsm in fsms)
            {
                string name = Convert.ToString(GetProperty(fsm, "FsmName"));
                if (string.Equals(name, fsmName, StringComparison.OrdinalIgnoreCase))
                    return fsm;
            }
            return null;
        }

        private object FindFsm(string objectName, string fsmName)
        {
            ResolveTypes();
            if (objectType == null || playMakerType == null) return null;

            if (findObjectsOfType == null)
            {
                MethodInfo[] methods = objectType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (MethodInfo m in methods)
                {
                    if (m.Name != "FindObjectsOfType") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (!m.IsGenericMethod && ps.Length == 1 && ps[0].ParameterType == typeof(Type))
                    {
                        findObjectsOfType = m;
                        break;
                    }
                }
            }
            if (findObjectsOfType == null) return null;

            Array fsms = findObjectsOfType.Invoke(null, new object[] { playMakerType }) as Array;
            if (fsms == null) return null;
            foreach (object fsm in fsms)
            {
                string name = Convert.ToString(GetProperty(fsm, "FsmName"));
                object go = GetProperty(fsm, "gameObject");
                string goName = Convert.ToString(GetProperty(go, "name"));
                if (string.Equals(name, fsmName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(goName, objectName, StringComparison.OrdinalIgnoreCase))
                    return fsm;
            }
            return null;
        }

        private static string ActiveStateName(object fsm)
        {
            object value = GetProperty(fsm, "ActiveStateName");
            if (value != null) return Convert.ToString(value);
            object state = GetProperty(fsm, "ActiveState");
            return Convert.ToString(GetProperty(state, "Name"));
        }

        private static void SendEvent(object fsm, string evt)
        {
            MethodInfo m = fsm.GetType().GetMethod(
                "SendEvent",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(string) },
                null);
            if (m != null) m.Invoke(fsm, new object[] { evt });
        }

        private static void TryEnterWithoutInput(object hero)
        {
            try
            {
                MethodInfo m = hero.GetType().GetMethod(
                    "EnterWithoutInput",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(bool) },
                    null);
                if (m != null) m.Invoke(hero, new object[] { true });
            }
            catch { }
        }

        private string CurrentSceneName()
        {
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("UnityEngine.SceneManagement.SceneManager", false);
                    if (t == null) continue;
                    MethodInfo m = t.GetMethod("GetActiveScene", BindingFlags.Public | BindingFlags.Static);
                    if (m == null) continue;
                    object scene = m.Invoke(null, null);
                    object name = GetProperty(scene, "name");
                    if (name != null) return Convert.ToString(name);
                }
            }
            catch { }
            return "";
        }

        private void ResolveTypes()
        {
            if (playMakerType != null && gameObjectType != null && objectType != null) return;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (playMakerType == null) playMakerType = a.GetType("HutongGames.PlayMaker.PlayMakerFSM", false);
                if (gameObjectType == null) gameObjectType = a.GetType("UnityEngine.GameObject", false);
                if (objectType == null) objectType = a.GetType("UnityEngine.Object", false);
            }
        }

        private static object GetProperty(object target, string name)
        {
            if (target == null) return null;
            PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (p != null) return p.GetValue(target, null);
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            return f == null ? null : f.GetValue(target);
        }
    }
}
