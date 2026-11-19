using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MoSimLib.Editor
{
    public class LeakTracker : EditorWindow
    {
        private static HashSet<int> _snapshotMaterialIDs = new HashSet<int>();

        [MenuItem("Tools/Leak Tracker/Take Snapshot")]
        public static void StartTracking()
        {
            _snapshotMaterialIDs.Clear();
            Material[] currentMaterials = Resources.FindObjectsOfTypeAll<Material>();
        
            foreach (Material mat in currentMaterials)
            {
                if (mat != null) _snapshotMaterialIDs.Add(mat.GetInstanceID());
            }
        
            Debug.Log($"[Leak Detector] Snapshot saved with {_snapshotMaterialIDs.Count} materials.");
        }

        [MenuItem("Tools/Leak Tracker/Log Leaks to CSV")]
        public static void LogLeakingMaterials()
        {
            Material[] currentMaterials = Resources.FindObjectsOfTypeAll<Material>();
            List<string[]> csvRows = new List<string[]>();
            int totalNewMaterials = 0;

            foreach (Material mat in currentMaterials)
            {
                if (mat != null && !_snapshotMaterialIDs.Contains(mat.GetInstanceID()))
                {
                    totalNewMaterials++;
                    string matName = string.IsNullOrEmpty(mat.name) ? "Unnamed Material" : mat.name;
                    string instanceID = mat.GetInstanceID().ToString();
                
                    csvRows.Add(new string[] { matName, instanceID });
                }
            }

            ExportToCSV(csvRows);
            Debug.Log($"[Leak Detector] Analysis complete. Found {totalNewMaterials} leaked materials. CSV saved to LocalLow.");
        }

        private static void ExportToCSV(List<string[]> rows)
        {
            string folderPath = Application.persistentDataPath;
            string filePath = Path.Combine(folderPath, "LeakedMaterialsLog.csv");

            using (StreamWriter writer = new StreamWriter(filePath, false))
            {
                writer.WriteLine("Material Name,Instance ID");
                foreach (var row in rows)
                {
                    string safeName = row[0].Replace("\"", "\"\"");
                    writer.WriteLine($"\"{safeName}\",{row[1]}");
                }
            }
        }
    }
}