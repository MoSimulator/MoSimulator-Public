using System;
using Steam;
using Steamworks;
using UnityEditor;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    /// <summary>
    /// Brings the Steam API up for editor tooling and shuts it down again afterwards. If Steam is already
    /// running (play mode), that session is borrowed and left alone on dispose.
    /// </summary>
    public sealed class SteamEditorSession : IDisposable
    {
        private readonly bool _ownsClient;
        private bool _disposed;

        private SteamEditorSession(bool ownsClient)
        {
            _ownsClient = ownsClient;
            if (ownsClient)
            {
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            }
        }

        public static bool TryOpen(out SteamEditorSession session, out string error)
        {
            session = null;
            error = null;

            if (SteamClient.IsValid)
            {
                session = new SteamEditorSession(false);
                return true;
            }

            try
            {
                SteamClient.Init(SteamStarter.AppId, true);
            }
            catch (Exception exception)
            {
                error = $"Steam failed to initialise: {exception.Message} " +
                        "Make sure the Steam client is running, you are logged in, and your account owns the game.";
                return false;
            }

            session = new SteamEditorSession(true);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_ownsClient)
            {
                return;
            }

            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            SteamClient.Shutdown();
        }
    }
}
