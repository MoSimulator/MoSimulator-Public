using System.Collections.Generic;
using System.Linq;
using RobotFramework.Components;
using UnityEditor;

namespace Editor
{
    /// <summary>Writing a <see cref="RollerGroup"/>'s roller list for authoring tools.</summary>
    public static class RollerGroups
    {
        /// <summary>
        /// Fills the group's roller list, one entry per roller, every multiplier 1 and nothing flipped.
        /// </summary>
        /// <remarks>
        /// RollerInstance holds a slot per roller type and picks whichever is filled, so only the
        /// InteractionRoller slot is written and the rest are left null. The fields are private and
        /// serialized, which is where they belong - a tool reaching in is not a reason to widen the
        /// component's surface, so they go through SerializedObject.
        /// </remarks>
        public static void Wire(RollerGroup group, IReadOnlyList<InteractionRoller> rollers)
        {
            var serialized = new SerializedObject(group);

            SerializedProperty list = serialized.FindProperty("rollers");
            list.arraySize = rollers.Count;

            for (int i = 0; i < rollers.Count; i++)
            {
                SetEntry(list.GetArrayElementAtIndex(i), rollers[i]);
            }

            serialized.ApplyModifiedProperties();
        }

        /// <summary>Appends missing rollers, preserving all existing entries and their settings.</summary>
        public static int Append(RollerGroup group, IReadOnlyList<InteractionRoller> rollers)
        {
            var serialized = new SerializedObject(group);
            SerializedProperty list = serialized.FindProperty("rollers");
            var existing = new HashSet<InteractionRoller>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var roller = list.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("IntRoller").objectReferenceValue as InteractionRoller;
                if (roller != null) existing.Add(roller);
            }

            int added = 0;
            foreach (InteractionRoller roller in rollers.Where(r => r != null))
            {
                if (!existing.Add(roller)) continue;
                int index = list.arraySize++;
                // Unity can copy the preceding entry when growing an array; reset every field.
                SetEntry(list.GetArrayElementAtIndex(index), roller);
                added++;
            }

            if (added > 0) serialized.ApplyModifiedProperties();
            return added;
        }

        static void SetEntry(SerializedProperty entry, InteractionRoller roller)
        {
            entry.FindPropertyRelative("IntRoller").objectReferenceValue = roller;
            entry.FindPropertyRelative("GenRoller").objectReferenceValue = null;
            entry.FindPropertyRelative("PvffController").objectReferenceValue = null;
            entry.FindPropertyRelative("Flywheel").objectReferenceValue = null;
            entry.FindPropertyRelative("Multiplier").floatValue = 1f;
            entry.FindPropertyRelative("flipped").boolValue = false;
        }
    }
}
