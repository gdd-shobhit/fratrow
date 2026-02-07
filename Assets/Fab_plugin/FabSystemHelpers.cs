#if UNITY_EDITOR

using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager;
using System.Net.Sockets;
using System.Text;
using UnityEngine.Rendering;

namespace Fab
{
    public class FabUtilities : MonoBehaviour
    {
        public static void sendStatus(string status, string message = "", string id="undefined", string path="undefined")
        {
            try
            {
                int shaderType = (int)FabUtilities.GetProjectPipeline();
                string rendererName = shaderType switch
                {
                    0 => "HDRP",
                    1 => "URP",
                    2 => "Standard",
                    _ => "Unknown"
                };

                TcpClient socketConnection = new TcpClient("localhost", 24563);
                socketConnection.SendTimeout = 1;
                NetworkStream stream = socketConnection.GetStream();
                if (stream.CanWrite)
                {
                    path = path.Replace("\\\\", "\\").Replace("\\", "\\\\");
                    string payloadString = "{\"status\":\"" + status + "\", \"id\":\"" + id + "\", \"path\":\"" + path + "\", \"message\": \"" + message + "\", \"app_name\" : \"unity\", \"renderer\" : \"" + rendererName + "\", \"plugin_version\" : \"" + Fab.Info.Version + "\", \"app_version\": \"" + Application.unityVersion + "\"}";
                    byte[] clientMessageAsByteArray = Encoding.ASCII.GetBytes(payloadString + "\0");
                    stream.Write(clientMessageAsByteArray, 0, clientMessageAsByteArray.Length);
                    Debug.Log("Sent callback: " + payloadString);
                    stream.Close();
                }
                socketConnection.Dispose();
            }
            catch (SocketException socketException)
            {
                Debug.Log("Socket exception: " + socketException);
            }
            return;
        }

        private static bool IsGitAvailableOnSystem()
        {
            try
            {
                using (var process = new System.Diagnostics.Process())
                {
                    process.StartInfo.FileName = "git";
                    process.StartInfo.Arguments = "--version";
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool SetupPackageInProject(string packageName, string gitPackageName=null)
        {
            // We'll use this to detect if the url is for khronos
            bool isGithubPackage = packageName.StartsWith("https://github.com");
            if (isGithubPackage)
            {
                if(!IsGitAvailableOnSystem())
                {
                    Debug.LogError($"Git not detected, cannot install {gitPackageName}");
                    return false;
                }
                else if(gitPackageName == null)
                {
                    Debug.LogError($"Git detected but associated packageName is null, cannot install {gitPackageName}");
                    return false;
                }
            }

            // First, we need to check if the package is available in the project
            // We can do this wether the packageName is a url or not
            ListRequest listRequest = Client.List();
            double listRequestStart = EditorApplication.timeSinceStartup;
            while (EditorApplication.timeSinceStartup - listRequestStart < 60.0)
            {
                if (listRequest.IsCompleted)
                {
                    if (listRequest.Status == StatusCode.Success)
                    {
                        // List request successful, proceed
                        foreach (var package in listRequest.Result)
                        {
                            bool isPackageInstalled = (!isGithubPackage && (package.name == packageName)) || (isGithubPackage && (package.name == gitPackageName));
                            if (isPackageInstalled)
                            {
                                Debug.Log($"Package {packageName} is already available in the project!");
                                return true;
                            }
                        }

                        // If the package is a github package, try to clone it
                        if (isGithubPackage)
                        {
                            Debug.Log($"Installing {gitPackageName} to your project...");
                            AddRequest addRequest = Client.Add(packageName); // This triggers an installation through url
                            double addRequestStart = EditorApplication.timeSinceStartup;
                            while (EditorApplication.timeSinceStartup - addRequestStart < 300.0)
                            {
                                if (addRequest.IsCompleted)
                                {
                                    if (addRequest.Status == StatusCode.Success)
                                    {
                                        Debug.Log($"Package {addRequest.Result.name} successfully installed");
                                        return true;
                                    }
                                    else
                                    {
                                        Debug.LogError($"Git package failed to install: {addRequest.Error.message}");
                                        return false;
                                    }
                                }
                            }
                            Debug.LogError("Git AddRequest timeout, this should not happen");
                            return false;
                        }

                        // If we're here, the package was not installed, let's search for it first, then add it
                        SearchRequest searchRequest = Client.Search(packageName);
                        double searchRequestStart = EditorApplication.timeSinceStartup;
                        while (EditorApplication.timeSinceStartup - searchRequestStart < 60.0)
                        {
                            if (searchRequest.IsCompleted)
                            {
                                if (searchRequest.Status == StatusCode.Success)
                                {
                                    // Package was found, we now need to install it with an addrequest
                                    Debug.Log($"Installing {packageName} to your project, this may take a few seconds");
                                    AddRequest addRequest = Client.Add(packageName);
                                    double addRequestStart = EditorApplication.timeSinceStartup;
                                    while (EditorApplication.timeSinceStartup - addRequestStart < 60.0)
                                    {
                                        if (addRequest.IsCompleted)
                                        {
                                            if (addRequest.Status == StatusCode.Success)
                                            {
                                                // Package was successfully added to the project !
                                                Debug.Log($"Required package {packageName} was successfully added to the project");
                                                return true;
                                            }
                                            else if (addRequest.Status >= StatusCode.Failure)
                                            {
                                                // Package was not found, not uch more we can do
                                                Debug.Log($"Required package {packageName} failed to install to the project");
                                                Debug.Log(addRequest.Error.message);
                                                return false;
                                            }
                                            else
                                            {
                                                Debug.Log("Add request is in a weird status, this should not happen");
                                                Debug.Log(addRequest.Status);
                                                return false;
                                            }
                                        }
                                    }
                                }
                                else if (searchRequest.Status >= StatusCode.Failure)
                                {
                                    // Package does not seem to exist, this should not happen
                                    Debug.Log(searchRequest.Error.message);
                                    return false;
                                }
                                else
                                {
                                    Debug.Log("Search request is in a weird status, this should not happen");
                                    Debug.Log(searchRequest.Status);
                                    return false;
                                }
                            }
                        }
                        Debug.Log("SearchRequest timeout after 5.0s, this should not happen");
                        return false;
                    }
                    else if (listRequest.Status >= StatusCode.Failure)
                    {
                        // List request was a failure
                        Debug.Log($"Did not manage to list installed packages while looking for {packageName}");
                        Debug.Log(listRequest.Error.message);
                        return false;
                    }
                    else
                    {
                        Debug.Log("List request is in a weird status, this should not happen");
                        Debug.Log(listRequest.Status);
                        return false;
                    }
                }
            }
            Debug.Log("Listrequest timeout after 5.0s, this should not happen");
            return false;
        }

        /// <summary>
        /// Check whether the child folder you're trying to make already exists, if not, create it and return the directory.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="child"></param>
        /// <returns></returns>
        public static string ValidateFolderCreate (string parent, string child, bool iterativeSuffix = false) {

            string tempPath = FixSlashes (Path.Combine (parent, child));

            if (!AssetDatabase.IsValidFolder(tempPath))
            {
                string newPath = AssetDatabase.CreateFolder(parent, child);
                return AssetDatabase.GUIDToAssetPath(newPath);
            }
            else
            {
                // The folder exists. If we specified to add a suffix, do it and create the new directory now
                if (iterativeSuffix)
                {
                    string uniquePath = AssetDatabase.GenerateUniqueAssetPath(tempPath);
                    string fixedParent = Path.GetDirectoryName(uniquePath);
                    string fixedChild = Path.GetFileName(uniquePath);
                    AssetDatabase.CreateFolder(fixedParent, fixedChild);
                    return uniquePath;
                }
                else
                {
                    return tempPath;
                }
            }
        }

        public static string FixSlashes (string txt) {
            txt = txt.Replace ("\\", "/");
            txt = txt.Replace (@"\\", "/");
            return txt;
        }

        public static void CopyFileToProject(string sourcePath, string destPath)
        {
            try
            {
                if (File.Exists(sourcePath))
                {
                    File.Copy(sourcePath, destPath, true);
                    AssetDatabase.ImportAsset(destPath);
                } else
                {
                    Debug.Log("Source file does not exist.");
                }
            }
            catch (Exception ex)
            {
                Debug.Log("Error copying file to project " + ex.ToString());
                // MegascansUtilities.HideProgressBar();
            }
        }

        static readonly HashSet<string> BannedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Some common extensions that should not be copied if present
            ".cs", ".js", ".mdb", ".pdb", ".so", ".dylib", ".a", ".asmdef", ".asmref", ".rsp",
            ".dll", ".exe", ".bat", ".cmd", ".ps1", ".sh", ".py", ".pl"
        };

        public static void CopyDirectory(string sourceDirectory, string targetDirectory)
        {
            var diSource = new DirectoryInfo(sourceDirectory);
            var diTarget = new DirectoryInfo(targetDirectory);

            CopyAll(diSource, diTarget);
        }

        public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
        {
            Directory.CreateDirectory(target.FullName);
            foreach (FileInfo fi in source.GetFiles())
            {
                if(BannedExtensions.Contains(fi.Extension.ToLower())) continue;
                fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
            }
            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
            {
                if(diSourceSubDir.Name == "Editor") continue;
                DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
                CopyAll(diSourceSubDir, nextTargetSubDir);
            }
        }

        public static string SanitizeName(string name)
        {
            return name.Replace(" ", "_").Replace("\\", "_").Replace("__", "_");
        }

        public static int GetProjectPipeline()
		{
			// From https://discussions.unity.com/t/hdrp-lwrp-detection-from-editor-script/708145/21
			if (GraphicsSettings.defaultRenderPipeline != null)
			{
				string PipelineName = GraphicsSettings.defaultRenderPipeline.GetType().Name;
				if (PipelineName == "HDRenderPipelineAsset")
				{
					// Debug.Log("High Definition Render Pipeline (HDRP) is being used.");
#if !UNITY_PIPELINE_HDRP
                    Debug.LogError("Project uses HDRP Pipeline, but the associated assembly definition was not loaded.");
                    Debug.LogError("If you have changed your pipeline recently, please reimport the Fab package.");
                    return -1;
#else
					return 0;
#endif
				}
				else if (PipelineName == "UniversalRenderPipelineAsset")
				{
					// Debug.Log("Universal Render Pipeline (URP) is being used.");
					return 1;
				}
				else
				{
					Debug.Log(PipelineName + " unsupported (not URP, HDRP or Built-in)");
					return -1;
				}
			}
			else
			{
				// Debug.Log("Using Built-in (Legacy) Render Pipeline");
				return 2;
			}
		}
    }
}

#endif
