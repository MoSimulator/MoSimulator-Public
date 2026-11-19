using System;
using System.Linq;
using UnityEngine;

namespace Editor
{
    /// <summary>What a tool is called and where it is offered, declared once and read by every surface.</summary>
    public readonly struct ToolInfo
    {
        /// <summary>Submenu in the Scene view's context menu, and the group in the tool palette.</summary>
        public string Category { get; }

        /// <summary>The entry's label. Also the undo group name and the prefix on the tool's log line.</summary>
        public string Title { get; }

        /// <summary>Lower sorts first, across categories. Entries that tie keep their path order.</summary>
        public int Order { get; }

        /// <summary>Shown when the tool runs against a selection that turns out to hold nothing for it.</summary>
        public string EmptyMessage { get; }

        public ToolInfo(string category, string title, int order = 0, string emptyMessage = null)
        {
            Category = category;
            Title = title;
            Order = order;
            EmptyMessage = emptyMessage;
        }

        /// <summary>Path the entry takes in the Scene view's context menu.</summary>
        public string ScenePath => string.IsNullOrEmpty(Category) ? Title : Category + "/" + Title;
    }

    /// <summary>
    /// A command that acts on the current selection. Deriving from this is all it takes to be offered
    /// in the Scene view context menu and the tool palette, and to be run with the reentrancy guard,
    /// undo group and result log the tools all want.
    /// </summary>
    /// <remarks>
    /// Unity's "GameObject/" menu cannot be filled at runtime — <c>[MenuItem]</c> wants a compile-time
    /// path — so a tool that wants a place there carries the two-line stanza below. Everything else the
    /// registry derives, including the menu path and priority, which it reads back off the attribute.
    /// <code>
    /// [MenuItem("GameObject/Split Colliders", false, 38)] static void Menu() => Run&lt;SplitColliders&gt;();
    /// [MenuItem("GameObject/Split Colliders", true)] static bool Validate() => CanRun&lt;SplitColliders&gt;();
    /// </code>
    /// </remarks>
    public abstract class AuthoringTool
    {
        public abstract ToolInfo Info { get; }

        /// <summary>
        /// Whether the current selection gives the tool anything to work on. Drives both the menu
        /// validator and whether the Scene view entry is offered or greyed, so the answer is given once.
        /// </summary>
        public abstract bool IsAvailable();

        /// <summary>Set false for a tool that changes nothing undoable, such as one that only selects.</summary>
        protected internal virtual bool UsesUndo => true;

        protected internal abstract void Execute(ToolRun run);

        /// <summary>Runs the named tool. The body of a tool's <c>[MenuItem]</c> command method.</summary>
        protected static void Run<T>() where T : AuthoringTool
        {
            ToolRegistry.Run(ToolRegistry.Get<T>());
        }

        /// <summary>Whether the named tool is available. The body of its <c>[MenuItem]</c> validator.</summary>
        protected static bool CanRun<T>() where T : AuthoringTool
        {
            AuthoringTool tool = ToolRegistry.Get<T>();
            return tool != null && tool.IsAvailable();
        }

        /// <summary>Whether the selection offers at least one component the tool could use.</summary>
        protected static bool Has<T>(TargetScope scope, Func<T, bool> filter = null) where T : Component
        {
            return SelectionSet.Targets<T>(scope).Any(t => t != null && (filter == null || filter(t)));
        }
    }
}
