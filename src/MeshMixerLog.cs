using System;

namespace KSPMeshMixer
{
    /// <summary>Console logging in the game's own style, and a hard stop when Unity is unavailable.</summary>
    internal static class MeshMixerLog
    {
        private const string Prefix = "[KSP MeshMixer] ";

        public static void Info(string message)
        {
            if (!MeshMixerSettings.Current.VerboseLogging && !IsImportant(message))
            {
                return;
            }

            Write(message);
        }

        /// <summary>Always logged: the per-part summary of what was (and was not) converted.</summary>
        public static void Report(string message)
        {
            Write(message);
        }

        public static void Warn(string message)
        {
            Write("warning: " + message);
        }

        public static void Error(string message, Exception exception = null)
        {
            Write("error: " + message + (exception == null ? "" : Environment.NewLine + exception));
        }

        private static bool IsImportant(string message)
        {
            return message != null && message.StartsWith("'", StringComparison.Ordinal);
        }

        private static void Write(string message)
        {
            try
            {
                UnityEngine.Debug.Log(Prefix + message);
            }
            catch (Exception)
            {
                // Outside Unity (tests, tooling) there is no logger; dropping the line is correct.
            }
        }
    }
}
