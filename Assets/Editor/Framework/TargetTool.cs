using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// A tool that does the same thing to each of the components the selection offers. It declares what
    /// it wants and how far to look for it; the loop, the empty-selection dialog, the prefab unpacking
    /// and the tally in the log line are all handled here.
    /// </summary>
    public abstract class TargetTool<T> : AuthoringTool where T : Component
    {
        /// <summary>How far into the selection to look for targets.</summary>
        protected virtual TargetScope Scope => TargetScope.Children;

        /// <summary>Narrows the targets past their type — a mesh filter that actually holds a mesh, say.</summary>
        protected virtual bool Filter(T target) => true;

        /// <summary>
        /// Whether each target's prefab link is severed before <see cref="Apply"/> sees it. Left off for
        /// tools that decide per target whether to act at all: unpacking is destructive to the prefab
        /// connection and should not be spent on a part the run is going to skip. Those call
        /// <see cref="HierarchyOps.Unpack"/> themselves, once the answer is known.
        /// </summary>
        protected virtual bool UnpackBeforeApply => false;

        /// <summary>Acts on one target. Returns whether it did, which is what the log line counts.</summary>
        protected abstract bool Apply(T target, ToolRun run);

        /// <summary>Runs before the first target, for a tool that carries state across the pass.</summary>
        protected virtual void Begin(ToolRun run) { }

        /// <summary>Runs after the last target: the place for a closing pass and the summary line.</summary>
        protected virtual void Finish(IReadOnlyList<T> targets, ToolRun run) { }

        public override bool IsAvailable()
        {
            return Targets().Any();
        }

        protected internal override void Execute(ToolRun run)
        {
            T[] targets = Targets().ToArray();
            if (targets.Length == 0)
            {
                run.Fail(Info.EmptyMessage ?? $"The selection holds no {typeof(T).Name}.");
                return;
            }

            run.Considered = targets.Length;
            Begin(run);

            foreach (T target in targets)
            {
                // Destroyed by an earlier target in the same pass; the pass keeps going.
                if (target == null) continue;

                if (UnpackBeforeApply) HierarchyOps.Unpack(target.gameObject);
                if (Apply(target, run)) run.Applied++;
            }

            Finish(targets, run);

            if (run.Summary == null)
                run.Report($"acted on {run.Applied} of {targets.Length} target(s).");
        }

        IEnumerable<T> Targets()
        {
            return SelectionSet.Targets<T>(Scope).Where(t => t != null && Filter(t));
        }
    }
}
