// Project-local bridge for Tools/unitylive.py. No network listener or player code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoSim.EditorTools
{
    [InitializeOnLoad]
    internal static class UnityLiveBridge
    {
        [Serializable] private class Request
        {
            public string id, session, command, value, filter;
            public double expires;
            public int instanceId, limit, offset;
        }
        [Serializable] private class Session
        {
            public int protocol = 1;
            public string session;
            public double updated;
        }
        [Serializable] private class Item
        {
            public int instanceId;
            public string name, path, scene;
            public bool active;
        }
        [Serializable] private class Response
        {
            public string id, error = "", message = "";
            public bool ok, playing, paused, compiling, updating;
            public string unityVersion;
            public int selectedId, total, shown, next_offset = -1;
            public List<Item> items = new List<Item>();
            public List<string> logs = new List<string>();
            public bool logsTruncated;
        }

        private static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/UnityLive"));
        // SessionState survives domain reload but changes when the editor restarts.
        private static readonly string SessionId;
        private static double nextPoll, nextHeartbeat;
        private static double Now => (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

        static UnityLiveBridge()
        {
            SessionId = SessionState.GetString("MoSim.UnityLive.Session", "");
            if (string.IsNullOrEmpty(SessionId))
            {
                SessionId = Guid.NewGuid().ToString("N");
                SessionState.SetString("MoSim.UnityLive.Session", SessionId);
            }
            EditorApplication.update += Tick;
            EditorApplication.quitting += () => { try { File.Delete(Path.Combine(DirectoryPath, "session.json")); } catch (IOException) { } };
        }

        private static void Write(string path, object data)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.1;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (EditorApplication.timeSinceStartup >= nextHeartbeat)
                {
                    Write(Path.Combine(DirectoryPath, "session.json"), new Session { session = SessionId, updated = Now });
                    nextHeartbeat = EditorApplication.timeSinceStartup + 1;
                    // Remove abandoned replies/claims; never replay a claimed command.
                    foreach (string path in Directory.GetFiles(DirectoryPath))
                        if ((path.EndsWith(".response.json") || path.EndsWith(".working")) &&
                            File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddMinutes(-5)) File.Delete(path);
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                foreach (string path in Directory.GetFiles(DirectoryPath, "*.request.json"))
                {
                    Process(path);
                    break; // Yield to the editor between commands.
                }
            }
            catch (IOException) { } // Client cleanup can race with polling.
            catch (UnauthorizedAccessException) { }
        }

        private static void Process(string path)
        {
            string working = path + ".working";
            File.Move(path, working); // Claim exactly once, even across domain reload.
            var response = new Response();
            try
            {
                var request = JsonUtility.FromJson<Request>(File.ReadAllText(working));
                string expectedId = Path.GetFileName(path).Replace(".request.json", "");
                response.id = expectedId;
                if (request == null || request.id != expectedId || !Guid.TryParseExact(expectedId, "N", out _))
                    throw new InvalidOperationException("Invalid request ID");
                if (request.session != SessionId || request.expires <= Now)
                    throw new InvalidOperationException("Request expired or belongs to a previous editor session");
                Execute(request, response);
                response.ok = true;
            }
            catch (Exception exception)
            {
                response.error = (exception is TargetInvocationException && exception.InnerException != null
                    ? exception.InnerException : exception).ToString();
            }
            finally
            {
                File.Delete(working);
            }
            Write(path.Replace(".request.json", ".response.json"), response);
        }

        private static void Execute(Request request, Response response)
        {
            switch (request.command)
            {
                case "status":
                    response.playing = EditorApplication.isPlaying;
                    response.paused = EditorApplication.isPaused;
                    response.compiling = EditorApplication.isCompiling;
                    response.updating = EditorApplication.isUpdating;
                    response.unityVersion = Application.unityVersion;
                    response.selectedId = Selection.activeObject ? Selection.activeObject.GetInstanceID() : 0;
                    break;
                case "hierarchy":
                    int offset = Math.Max(0, request.offset), limit = Math.Max(1, Math.Min(200, request.limit));
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (!scene.isLoaded) continue;
                        foreach (var root in scene.GetRootGameObjects())
                            Visit(root.transform, root.name, scene.path, request.filter, offset, limit, response);
                    }
                    response.shown = response.items.Count;
                    response.next_offset = offset + response.shown < response.total ? offset + response.shown : -1;
                    break;
                case "select":
                    var target = EditorUtility.InstanceIDToObject(request.instanceId);
                    if (!target) throw new InvalidOperationException("Object no longer exists");
                    Selection.activeObject = target;
                    EditorGUIUtility.PingObject(target);
                    break;
                case "menu":
                    if (!EditorApplication.ExecuteMenuItem(request.value)) throw new InvalidOperationException("Menu command unavailable: " + request.value);
                    response.message = "Menu command invoked";
                    break;
                case "run":
                    Invoke(request.value, response);
                    break;
                case "play":
                    ValidateToggle(request.value);
                    bool playing = request.value == "on";
                    EditorApplication.delayCall += () => { EditorApplication.isPlaying = playing; };
                    response.message = "Play mode change scheduled; poll status to confirm";
                    break;
                case "pause":
                    ValidateToggle(request.value);
                    EditorApplication.isPaused = request.value == "on";
                    break;
                case "refresh":
                    EditorApplication.delayCall += () => AssetDatabase.Refresh();
                    response.message = "Refresh scheduled; compilation may temporarily suspend the bridge";
                    break;
                default: throw new InvalidOperationException("Unknown command: " + request.command);
            }
        }

        private static void ValidateToggle(string value)
        {
            if (value != "on" && value != "off") throw new InvalidOperationException("Expected on or off");
        }

        private static void Visit(Transform transform, string path, string scene, string filter, int offset, int limit, Response response)
        {
            if (string.IsNullOrEmpty(filter) || path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int index = response.total++;
                if (index >= offset && response.items.Count < limit)
                    response.items.Add(new Item { instanceId = transform.gameObject.GetInstanceID(), name = transform.name,
                        path = path, scene = scene, active = transform.gameObject.activeInHierarchy });
            }
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                Visit(child, path + "/" + child.name, scene, filter, offset, limit, response);
            }
        }

        private static void Invoke(string name, Response response)
        {
            int split = string.IsNullOrEmpty(name) ? -1 : name.LastIndexOf('.');
            if (split < 1) throw new InvalidOperationException("Expected Namespace.Type.Method");
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var candidate = assembly.GetType(name.Substring(0, split));
                if (candidate == null) continue;
                if (type != null) throw new InvalidOperationException("Type name is ambiguous");
                type = candidate;
            }
            var method = type?.GetMethod(name.Substring(split + 1), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method == null || method.ContainsGenericParameters || (method.ReturnType != typeof(void) && method.ReturnType != typeof(string)) ||
                method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false))
                throw new InvalidOperationException("Method must be synchronous, static, parameterless, and return void or string");
            bool failed = false;
            int logChars = 0;
            Application.LogCallback capture = (message, stack, kind) =>
            {
                if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) failed = true;
                string line = kind + ": " + message;
                if (response.logs.Count < 100 && logChars + line.Length <= 16000)
                { response.logs.Add(line); logChars += line.Length; }
                else response.logsTruncated = true;
            };
            Application.logMessageReceived += capture;
            try { response.message = method.Invoke(null, null) as string ?? "Method completed"; }
            finally { Application.logMessageReceived -= capture; }
            if (failed) throw new InvalidOperationException("Method logged an error; see captured logs");
        }
    }
}
