using System.IO;
using UnityEngine;

/// <summary>
/// Runtime detection of ParrelSync clone. Use this to make the clone always act as client
/// (e.g. hide Start button, only allow Join). ParrelSync creates a ".clone" file in the project root.
/// </summary>
public static class ParrelSyncRuntime
{
    private const string CloneFileName = ".clone";

    private static bool? _isCloneCache;

    /// <summary>
    /// True if the current run is inside a ParrelSync clone project (so this instance should join as client, not host).
    /// </summary>
    public static bool IsParrelSyncClone
    {
        get
        {
            if (_isCloneCache.HasValue)
                return _isCloneCache.Value;

            try
            {
                string projectPath = Application.dataPath;
                if (projectPath.EndsWith("/Assets") || projectPath.EndsWith("\\Assets"))
                    projectPath = Path.GetDirectoryName(projectPath);
                if (string.IsNullOrEmpty(projectPath))
                {
                    _isCloneCache = false;
                    return false;
                }
                string cloneFilePath = Path.Combine(projectPath, CloneFileName);
                _isCloneCache = File.Exists(cloneFilePath);
                return _isCloneCache.Value;
            }
            catch
            {
                _isCloneCache = false;
                return false;
            }
        }
    }
}
