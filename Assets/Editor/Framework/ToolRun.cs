using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// One invocation of an authoring tool. It carries the name the undo group and the log line are
    /// filed under, counts what the tool touched, and collects the one-line summary it reports.
    /// </summary>
    public sealed class ToolRun
    {
        /// <summary>The tool's title: its undo group name and the prefix on its log line.</summary>
        public string Name { get; }

        /// <summary>How many targets the tool was handed.</summary>
        public int Considered { get; internal set; }

        /// <summary>How many of them it actually acted on.</summary>
        public int Applied { get; internal set; }

        /// <summary>Object the log line pings, when the result has one obvious home.</summary>
        public Object Context { get; set; }

        /// <summary>True once <see cref="Fail"/> has been called; the run logs nothing further.</summary>
        public bool Failed { get; private set; }

        internal string Summary { get; private set; }

        internal ToolRun(string name)
        {
            Name = name;
        }

        /// <summary>The one line the run logs when it finishes. Sentence case, no name prefix.</summary>
        public void Report(string summary)
        {
            Summary = summary;
        }

        /// <summary>
        /// Tells the author why nothing happened and ends the run quietly. For the cases the tool can
        /// only discover once it starts looking; a tool that cannot run at all should say so from
        /// <see cref="AuthoringTool.IsAvailable"/> instead, and be greyed out rather than clicked.
        /// </summary>
        public void Fail(string message)
        {
            Failed = true;
            EditorUtility.DisplayDialog(Name, message, "OK");
        }
    }
}
