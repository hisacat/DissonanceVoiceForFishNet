using Dissonance.Networking;

namespace Dissonance.Integrations.FishNet.Utils
{
    // Helper class for Dissonance events logging
    internal static class LoggingHelper
    {
        public static Log _logger = Logs.Create(LogCategory.Network, "FishNet");
        public static Log Logger => _logger;
#if UNITY_EDITOR
#pragma warning disable IDE0051
        [UnityEditor.InitializeOnEnterPlayMode]
        private static void OnEnterPlaymodeInEditor(UnityEditor.EnterPlayModeOptions options)
        {
            if (options.HasFlag(UnityEditor.EnterPlayModeOptions.DisableDomainReload))
            {
                _logger = Logs.Create(LogCategory.Network, "FishNet");
            }
        }
#pragma warning restore IDE0051
#endif

        private const string RunningAsTemplate = "Running as: {0}!";
        private const string StoppingAsTemplate = "Stopping as: {0}!";


        public static void RunningAs(NetworkMode mode)
        {
            Logger.Info(string.Format(RunningAsTemplate, mode.ToString()));
        }

        public static void StoppingAs(NetworkMode mode)
        {
            Logger.Info(string.Format(StoppingAsTemplate, mode.ToString()));
        }
    }
}