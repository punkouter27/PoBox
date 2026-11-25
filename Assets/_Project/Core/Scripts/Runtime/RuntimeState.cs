using System;
using System.Reflection;
using UnityEngine;

namespace PoBox
{
    /// <summary>
    /// One place that puts the runtime's static state back to its first frame, for every way play mode can start.
    ///
    /// Nine components publish themselves as <c>Instance</c> and a few keep a static switch (<see cref="Audio.AudioDirector.Muted"/>,
    /// <see cref="Audio.Haptics.Enabled"/>, <see cref="Audio.SpeechCommentary.Enabled"/>). With Domain Reload off (Enter Play
    /// Mode options) those survive from one play to the next, so the second run of a test or a scene could talk to the
    /// first run's director. Each class used to hand-reset what it remembered (HudView did, and only its own app mode,
    /// which is not an Instance and still resets itself); this walks the game's own assemblies instead, so a new
    /// singleton is covered without another edit.
    /// </summary>
    public static class RuntimeState
    {
        static readonly Assembly[] Assemblies = { typeof(RuntimeState).Assembly, typeof(Mj.MjBoxer).Assembly };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => ResetAll();

        /// <summary>Clears every <c>Instance</c> the game's assemblies publish. Safe to call more than once.</summary>
        public static void ResetAll()
        {
            foreach (Assembly assembly in Assemblies)
                foreach (Type type in Types(assembly))
                {
                    if (type.IsAbstract || type.IsGenericTypeDefinition) continue;
                    FieldInfo backing = type.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
                    if (backing == null || backing.FieldType.IsValueType) continue;
                    backing.SetValue(null, null);
                }
        }

        /// <summary>True when every published <c>Instance</c> has a backing field this can clear (the guard the EditMode test reads).</summary>
        public static bool CanResetAll(out string missing)
        {
            foreach (Assembly assembly in Assemblies)
                foreach (Type type in Types(assembly))
                {
                    PropertyInfo instance = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (instance == null || !instance.PropertyType.IsClass) continue;
                    if (type.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic) == null)
                    {
                        missing = type.FullName;
                        return false;
                    }
                }
            missing = null;
            return true;
        }

        static Type[] Types(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return Array.FindAll(e.Types, t => t != null); }
        }
    }
}
