namespace GameSystems.Modding
{
    [System.Serializable]
    public class ModpackManifest
    {
        public string id;
        public string name;
        public string version;
        public string author;
        public string description;
        public int gameYear;

        /// <summary>Name of the GameOption asset this modpack targets. Disambiguates games sharing a year.</summary>
        public string gameId;
        public string gameName;
        public string gameType;

        public string modDll;

        /// <summary>Editor platform the bundle was built on (Application.platform), blank on older builds.</summary>
        public string buildHost;

        public ModpackEntry modpack;
    }

    [System.Serializable]
    public class ModpackEntry
    {
        public string metadataGuid;
        public string metadataName;
    }
}