using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Yarn.Compiler;
using Yarn.Unity;

namespace Systems.Dialogue
{
    /// <summary>
    /// Compiles .yarn TextAssets at runtime into a YarnProject (no editor import required).
    /// </summary>
    public static class RuntimeYarnProjectFactory
    {
        public static YarnProject CreateFromTextAssets(IEnumerable<TextAsset> yarnFiles)
        {
            var project = ScriptableObject.CreateInstance<YarnProject>();
            var localization = ScriptableObject.CreateInstance<Localization>();
            localization.name = "RuntimeDefault";

            var combined = new System.Text.StringBuilder();
            foreach (var asset in yarnFiles)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.text))
                {
                    continue;
                }

                combined.AppendLine(asset.text.TrimEnd());
                combined.AppendLine();
            }

            if (combined.Length == 0)
            {
                Debug.LogError("[RuntimeYarn] No yarn sources provided.");
                return project;
            }

            var job = CompilationJob.CreateFromString(
                "CampusDialogue",
                combined.ToString(),
                null,
                Project.CurrentProjectFileVersion);
            var result = Compiler.Compile(job);

            foreach (var d in result.Diagnostics)
            {
                if (d.Severity == Diagnostic.DiagnosticSeverity.Error)
                {
                    Debug.LogError($"[RuntimeYarn] {d}");
                }
                else if (d.Severity == Diagnostic.DiagnosticSeverity.Warning)
                {
                    Debug.LogWarning($"[RuntimeYarn] {d}");
                }
            }

            if (result.ContainsErrors || result.Program == null)
            {
                Debug.LogError("[RuntimeYarn] Compilation failed.");
                return project;
            }

            foreach (var pair in result.StringTable)
            {
                localization.AddLocalizedString(pair.Key, pair.Value.text ?? string.Empty);
            }

            // Yarn.Google.Protobuf is a package precompiled DLL — not referenced by Assembly-CSharp.
            // Serialize via MessageExtensions.ToByteArray through reflection.
            project.compiledYarnProgram = ProgramToBytes(result.Program);
            project.baseLocalization = localization;
            project.localizationType = LocalizationType.YarnInternal;
            return project;
        }

        static byte[] ProgramToBytes(Yarn.Program program)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var extType = assembly.GetType("Google.Protobuf.MessageExtensions");
                if (extType == null)
                {
                    continue;
                }

                foreach (var candidate in extType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (candidate.Name != "ToByteArray")
                    {
                        continue;
                    }

                    var parameters = candidate.GetParameters();
                    if (parameters.Length != 1)
                    {
                        continue;
                    }

                    try
                    {
                        return (byte[])candidate.Invoke(null, new object[] { program });
                    }
                    catch
                    {
                        // Wrong overload; try next.
                    }
                }
            }

            throw new InvalidOperationException(
                "[RuntimeYarn] Google.Protobuf.MessageExtensions.ToByteArray not found. Is Yarn Spinner loaded?");
        }
    }
}
