using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Finds every <see cref="AuthoringTool"/> in the project and is the one place that runs one.
    /// Everything that offers the tools to the author — the Scene view context menu, the tool palette —
    /// reads them from here, so a new tool appears on all of them by existing.
    /// </summary>
    public static class ToolRegistry
    {
        /// <summary>What a tool's own <c>[MenuItem]</c> says about where it sits in Unity's menus.</summary>
        public readonly struct MenuEntry
        {
            public string Path { get; }
            public int Priority { get; }

            public MenuEntry(string path, int priority)
            {
                Path = path;
                Priority = priority;
            }
        }

        static Dictionary<Type, AuthoringTool> _tools;
        static Dictionary<Type, MenuEntry> _menus;

        /// <summary>Tools already running, so a command dispatched once per selected object runs once.</summary>
        static readonly HashSet<Type> Running = new HashSet<Type>();

        /// <summary>Every registered tool, in the order their entries should be offered.</summary>
        public static IEnumerable<AuthoringTool> All
        {
            get
            {
                Load();
                return _tools.Values
                    .OrderBy(t => t.Info.Order)
                    .ThenBy(t => t.Info.ScenePath);
            }
        }

        public static AuthoringTool Get<T>() where T : AuthoringTool
        {
            Load();
            return _tools.TryGetValue(typeof(T), out AuthoringTool tool) ? tool : null;
        }

        /// <summary>Where the tool sits in Unity's own menus, or null if it claims no place there.</summary>
        public static MenuEntry? Menu(AuthoringTool tool)
        {
            Load();
            return _menus.TryGetValue(tool.GetType(), out MenuEntry entry) ? entry : (MenuEntry?)null;
        }

        /// <summary>
        /// The key the tool is bound to, or null. A tool opts in by putting a <c>[Shortcut]</c> on its
        /// command method under the id below; there is no way to register one at runtime.
        /// </summary>
        public static string Shortcut(AuthoringTool tool)
        {
            string id = "MoSim/" + tool.Info.Title;
            if (!ShortcutManager.instance.GetAvailableShortcutIds().Contains(id)) return null;

            ShortcutBinding binding = ShortcutManager.instance.GetShortcutBinding(id);
            string text = binding.ToString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// Runs a tool: guards against the repeat dispatch, opens an undo group, and logs the one line
        /// the tool reported. Nothing else invokes <see cref="AuthoringTool.Execute"/>.
        /// </summary>
        public static void Run(AuthoringTool tool)
        {
            if (tool == null) return;

            // Unity dispatches a "GameObject/" command once per selected object, so a single click can
            // call this several times. The whole selection is handled in one pass and results must never
            // be fed back in as sources, so later calls in the same dispatch are dropped.
            Type type = tool.GetType();
            if (!Running.Add(type)) return;
            EditorApplication.delayCall += () => Running.Remove(type);

            var run = new ToolRun(tool.Info.Title);
            int undoGroup = Undo.GetCurrentGroup();
            if (tool.UsesUndo) Undo.SetCurrentGroupName(run.Name);

            try
            {
                tool.Execute(run);
            }
            finally
            {
                if (tool.UsesUndo) Undo.CollapseUndoOperations(undoGroup);
            }

            if (!run.Failed && !string.IsNullOrEmpty(run.Summary))
                Debug.Log($"{run.Name}: {run.Summary}", run.Context);
        }

        static void Load()
        {
            if (_tools != null) return;

            _tools = new Dictionary<Type, AuthoringTool>();
            _menus = new Dictionary<Type, MenuEntry>();

            foreach (Type type in TypeCache.GetTypesDerivedFrom<AuthoringTool>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition) continue;

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Debug.LogWarning($"[ToolRegistry] '{type.Name}' is ignored: a tool needs a parameterless constructor.");
                    continue;
                }

                _tools.Add(type, (AuthoringTool)Activator.CreateInstance(type));

                MenuEntry? menu = ReadMenu(type);
                if (menu.HasValue) _menus.Add(type, menu.Value);
            }
        }

        /// <summary>
        /// The tool's own <c>[MenuItem]</c>, read back rather than declared a second time, so the path
        /// and priority a tool takes in Unity's menus are written exactly once.
        /// </summary>
        static MenuEntry? ReadMenu(Type type)
        {
            const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (MethodInfo method in type.GetMethods(Flags))
            {
                var attribute = method.GetCustomAttribute<MenuItem>();
                if (attribute == null || attribute.validate) continue;

                return new MenuEntry(attribute.menuItem, attribute.priority);
            }

            return null;
        }
    }
}
