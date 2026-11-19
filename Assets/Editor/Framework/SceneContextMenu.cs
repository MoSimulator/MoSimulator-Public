using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Owns the Scene view's right-click menu. Every <see cref="AuthoringTool"/> appears on it without
    /// asking, under the category and order its <see cref="ToolInfo"/> declares; <see cref="Add"/> is
    /// there for the commands that are not tools, such as a mode that shows a tick when it is on.
    /// </summary>
    /// <remarks>
    /// Unity's own Scene view context menu does not surface "GameObject/" items, and the Scene view
    /// binds right-click to camera look, so the click is recognised by hand: a right press and release
    /// with no meaningful drag in between, which leaves right-drag navigation untouched. The standard
    /// edit commands are mirrored at the bottom so nothing is lost by taking the click over.
    /// </remarks>
    [InitializeOnLoad]
    public static class SceneContextMenu
    {
        /// <summary>Squared pixel distance a right-click may travel before it counts as a camera drag.</summary>
        const float DragToleranceSqr = 16f;

        class Entry
        {
            public string Path;
            public int Priority;
            public Action Command;
            public Func<bool> Enabled;
            public Func<bool> Checked;
        }

        static readonly List<Entry> Contributed = new List<Entry>();

        static Vector2 _rightPressPosition;
        static bool _rightPressed;
        static SceneView _rightPressView;
        static bool _rightClickHandled;
        static bool _rightDragged;

        sealed class PendingMenu
        {
            public SceneView View;
            public GameObject Target;
            public Vector2 ScreenPosition;
            public int[] SelectionIds;
            public bool Ready;
        }

        static PendingMenu _pendingMenu;

        static SceneContextMenu()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>
        /// Adds an entry for a command that is not an <see cref="AuthoringTool"/> — a mode toggle, or a
        /// passthrough to one of Unity's own commands. Call it from a static constructor or an
        /// [InitializeOnLoadMethod] so it survives assembly reloads.
        /// </summary>
        public static void Add(string path, Action command, Func<bool> enabled = null,
            int priority = 0, Func<bool> isChecked = null)
        {
            if (string.IsNullOrEmpty(path) || command == null) return;

            Contributed.RemoveAll(e => e.Path == path);
            Contributed.Add(new Entry
            {
                Path = path,
                Priority = priority,
                Command = command,
                Enabled = enabled,
                Checked = isChecked
            });
        }

        /// <summary>Adds an entry that runs an existing menu command, such as one of Unity's own.</summary>
        public static void AddMenuCommand(string path, string menuItemPath,
            Func<bool> enabled = null, int priority = 0)
        {
            Add(path, () => EditorApplication.ExecuteMenuItem(menuItemPath), enabled, priority);
        }

        static void OnSceneGui(SceneView sceneView)
        {
            Event e = Event.current;

            // A new input supersedes a menu that has not appeared yet.
            if (e.type == EventType.MouseDown || e.type == EventType.KeyDown) _pendingMenu = null;

            if (SnapToMeshCenter.HandleKey(e)) return;

            if (ResizeInteractionRoller.HandleFinishEvent(e) || ResizeInteractionRoller.Active)
            {
                _rightPressed = false;
                _pendingMenu = null;
                return;
            }

            if (ShowPendingMenu(sceneView, e)) return;
            if (!CompleteRightClick(sceneView, e)) return;

            GameObject target = ColliderSelectMode.Enabled
                ? ColliderSelectMode.Pick(HandleUtility.GUIPointToWorldRay(e.mousePosition))
                : null;
            // The context menu must also work on meshes without a pickable collider.
            if (target == null) target = HandleUtility.PickGameObject(e.mousePosition, false);
            if (target == null) return;

            Vector2 screenPosition = GUIUtility.GUIToScreenPoint(e.mousePosition);
            _rightClickHandled = true;
            e.Use();
            if (!Selection.gameObjects.Contains(target)) Selection.activeGameObject = target;

            var pending = new PendingMenu
            {
                View = sceneView,
                Target = target,
                ScreenPosition = screenPosition,
                SelectionIds = Selection.instanceIDs
            };
            _pendingMenu = pending;
            EditorApplication.delayCall += () =>
            {
                if (_pendingMenu != pending) return;
                if (pending.View == null)
                {
                    _pendingMenu = null;
                    return;
                }

                // Finish the input/selection callbacks, then open in this view's GUI context.
                pending.Ready = true;
                pending.View.Repaint();
            };
        }

        static bool CompleteRightClick(SceneView sceneView, Event e)
        {
            if (RightPress(e))
            {
                _pendingMenu = null;
                _rightPressPosition = e.mousePosition;
                _rightPressView = sceneView;
                _rightPressed = true;
                _rightClickHandled = false;
                _rightDragged = false;
                return false;
            }

            if (sceneView != _rightPressView) return false;
            if (_rightPressed && e.type == EventType.MouseDrag &&
                (e.mousePosition - _rightPressPosition).sqrMagnitude > DragToleranceSqr)
                _rightDragged = true;

            bool released = e.type == EventType.MouseUp && e.button == 1;
            if (!released && !CompletedContextClick(e)) return false;

            // Windows can deliver both MouseUp and ContextClick for the same gesture, in either
            // order. Consume the companion too, even after the deferred menu has been shown.
            if (_rightClickHandled)
            {
                e.Use();
                return false;
            }

            bool clicked = _rightPressed && !_rightDragged &&
                           (e.mousePosition - _rightPressPosition).sqrMagnitude <= DragToleranceSqr;
            _rightPressed = false;
            return clicked;
        }

        static bool ShowPendingMenu(SceneView sceneView, Event e)
        {
            PendingMenu pending = _pendingMenu;
            if (pending == null || !pending.Ready || pending.View != sceneView ||
                e.type != EventType.Repaint) return false;

            _pendingMenu = null;
            if (pending.Target == null || EditorWindow.focusedWindow != sceneView ||
                !Selection.instanceIDs.SequenceEqual(pending.SelectionIds)) return false;

            // ShowAsContext would use the repaint's mouse position. Convert the saved anchor back
            // inside the original Scene view so moving the pointer cannot move the menu.
            Vector2 position = GUIUtility.ScreenToGUIPoint(pending.ScreenPosition);
            Build().DropDown(new Rect(position, Vector2.zero));
            return true;
        }

        /// <summary>
        /// Is the right button going down? On macOS a ContextClick counts as the press: Cocoa raises it
        /// on the button going down rather than coming up, and may raise it in place of the MouseDown.
        /// Recording it as a press is what leaves right-drag navigation alone - honouring it as a click
        /// would open the menu and consume the event the moment a camera drag started.
        /// </summary>
        static bool RightPress(Event e)
        {
            if (e.type == EventType.MouseDown && e.button == 1) return true;
#if UNITY_EDITOR_OSX
            return e.type == EventType.ContextClick;
#else
            return false;
#endif
        }

        /// <summary>
        /// Is this a ContextClick standing for a click already finished? Only where the platform raises
        /// it after the release; on macOS it arrives on the press and is taken by <see cref="RightPress"/>,
        /// so there the menu waits for a release that has not travelled.
        /// </summary>
        static bool CompletedContextClick(Event e)
        {
#if UNITY_EDITOR_OSX
            return false;
#else
            return e.type == EventType.ContextClick;
#endif
        }

        /// <summary>
        /// The menu for the current selection. Built after the click has put the picked object into the
        /// selection, so an entry's availability is asked of exactly what a click would act on.
        /// </summary>
        static GenericMenu Build()
        {
            var menu = new GenericMenu();

            foreach (Entry entry in Entries().OrderBy(entry => entry.Priority).ThenBy(entry => entry.Path))
            {
                var content = new GUIContent(entry.Path);
                bool enabled = entry.Enabled == null || entry.Enabled();
                bool ticked = entry.Checked != null && entry.Checked();

                // Entries that do not apply are shown greyed rather than hidden, so the menu keeps a
                // stable shape and the tools stay where the hand expects them.
                if (enabled) menu.AddItem(content, ticked, () => entry.Command());
                else menu.AddDisabledItem(content);
            }

            menu.AddSeparator(string.Empty);
            foreach (string command in new[] { "Cut", "Copy", "Paste", "Duplicate", "Delete" })
            {
                string path = "Edit/" + command;
                menu.AddItem(new GUIContent(command), false, () => EditorApplication.ExecuteMenuItem(path));
            }

            return menu;
        }

        /// <summary>Every registered tool, plus anything added from code.</summary>
        static IEnumerable<Entry> Entries()
        {
            foreach (AuthoringTool tool in ToolRegistry.All)
            {
                AuthoringTool captured = tool;
                yield return new Entry
                {
                    Path = captured.Info.ScenePath,
                    Priority = captured.Info.Order,
                    Command = () => ToolRegistry.Run(captured),
                    Enabled = captured.IsAvailable
                };
            }

            foreach (Entry entry in Contributed) yield return entry;
        }
    }
}
