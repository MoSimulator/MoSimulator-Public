using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Reduces the selection and everything under it to what draws it: every component other than the
    /// rendering ones is removed — rigidbodies, joints, colliders, rollers and behaviours of every kind.
    /// Objects are left where they are, so the model still looks exactly as it did.
    /// </summary>
    public sealed class StripComponents : AuthoringTool
    {
        public override ToolInfo Info => new ToolInfo("Cleanup", "Strip Components", 25,
            "The selection already holds nothing but rendering components.");

        [MenuItem("GameObject/Strip Components", false, 46)]
        static void Menu() => Run<StripComponents>();

        [MenuItem("GameObject/Strip Components", true)]
        static bool Validate() => CanRun<StripComponents>();

        public override bool IsAvailable() => StripComponentsRun.IsAvailable(false);

        protected internal override void Execute(ToolRun run) => StripComponentsRun.Execute(run, false);
    }

    /// <summary>
    /// As <see cref="StripComponents"/>, but the colliders stay. For taking a part off the simulation
    /// while it keeps the collision shapes that were authored for it.
    /// </summary>
    public sealed class StripComponentsKeepColliders : AuthoringTool
    {
        public override ToolInfo Info => new ToolInfo("Cleanup", "Strip Components (Keep Colliders)", 26,
            "The selection already holds nothing but rendering components and colliders.");

        [MenuItem("GameObject/Strip Components (Keep Colliders)", false, 47)]
        static void Menu() => Run<StripComponentsKeepColliders>();

        [MenuItem("GameObject/Strip Components (Keep Colliders)", true)]
        static bool Validate() => CanRun<StripComponentsKeepColliders>();

        public override bool IsAvailable() => StripComponentsRun.IsAvailable(true);

        protected internal override void Execute(ToolRun run) => StripComponentsRun.Execute(run, true);
    }

    /// <summary>The pass both strip commands run, which differ only in whether colliders are kept.</summary>
    static class StripComponentsRun
    {
        public static bool IsAvailable(bool keepColliders)
        {
            return SelectionSet.Roots().Any(root => ComponentStrip.HasAny(root, keepColliders));
        }

        public static void Execute(ToolRun run, bool keepColliders)
        {
            GameObject[] roots = SelectionSet.Roots();
            if (roots.Length == 0) return;

            run.Considered = roots.Length;

            var total = new ComponentStrip.Result();

            foreach (GameObject root in roots)
            {
                if (!ComponentStrip.HasAny(root, keepColliders)) continue;

                // Only once the root is known to hold something: unpacking severs the prefab link and
                // should not be spent on a selection this pass would leave untouched.
                HierarchyOps.Unpack(root);

                ComponentStrip.Result stripped = ComponentStrip.Strip(root, keepColliders, run.Name);
                total.Removed += stripped.Removed;
                total.Objects += stripped.Objects;
                run.Applied++;
            }

            if (total.Removed == 0)
            {
                run.Fail("The selection holds nothing to remove.");
                return;
            }

            run.Context = roots[0];
            run.Report($"removed {total.Removed} component(s) from {total.Objects} object(s)." +
                       (keepColliders ? " Colliders kept." : string.Empty));
        }
    }
}
