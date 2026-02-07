#if UNITY_EDITOR

using Newtonsoft.Json.Linq;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace Fab {

    public class Info
    {
        // Update this when updating plugin version
        public static string Version = "0.3.5";
    }

    public class DefaultStartup
    {
        [MenuItem("Assets/Activate or deactivate Fab plugin", false, 1999)]
        public static void Init()
        {
            FabLink.ToggleServer();
        }
    }

    public class FabServer {
        private TcpListener tcpListener;
        private Thread tcpListenerThread;
        private TcpClient connectedTcpClient;

        private bool isRunning;

        public List<string> jsonData = new List<string> ();

        // Use this for initialization
        public void StartServer () {
            if(!isRunning)
            {
                // Start TcpServer background thread 		
                tcpListenerThread = new Thread(new ThreadStart(ListenForIncommingRequests));
                tcpListenerThread.IsBackground = true;
                tcpListenerThread.Start();
                isRunning = true;
            }
        }

        public void EndServer()
        {
            isRunning = false;
            tcpListener.Stop();
            tcpListenerThread.Abort();
        }

        private void ListenForIncommingRequests () {
            try {
                tcpListener = new TcpListener (IPAddress.Parse ("127.0.0.1"), 23081);
                tcpListener.Start ();
                Debug.Log("Fab plugin (version " + Fab.Info.Version + ") started, listening on port 23081");
                Byte[] bytes = new Byte[4096];
                while (true) {
                    using (connectedTcpClient = tcpListener.AcceptTcpClient ()) {
                        using (NetworkStream stream = connectedTcpClient.GetStream ()) {
                            int length;
                            string clientMessage = "";
                            while ((length = stream.Read (bytes, 0, bytes.Length)) != 0) {
                                try {
                                    byte[] incommingData = new byte[length];
                                    Array.Copy(bytes, 0, incommingData, 0, length);
                                    UTF8Encoding encodingUnicode = new UTF8Encoding();
                                    clientMessage += encodingUnicode.GetString(incommingData);
                                } catch (Exception ex)
                                {
                                    Debug.LogError("Error encoding json data");
                                    Debug.LogError(ex.ToString());
                                }
                            }
                            // Only add message to queue if not empty (might have been a simple ping otherwise)
                            if(clientMessage != "") {
                                Debug.Log("Received data: " + clientMessage.ToString());
                                jsonData.Add (clientMessage);
                            }
                        }
                    }
                }
            } catch (SocketException socketException) {
                Debug.LogError("Error getting requests from launcher");
                Debug.LogError(socketException.ToString ());
            }
        }
    }

    [InitializeOnLoad]
    [ExecuteInEditMode]
    public class FabLink {
        static private bool isRunning = false;
        static private FabServer listener;
        static FabLink()
        {
            listener = new FabServer();
            EditorApplication.update += ImportPayloads;
            ToggleServer();
        }

        public static void ToggleServer(bool runServer = true)
        {
            if (isRunning)
            {
                try
                {
                    if (isRunning)
                    {
                        listener.EndServer();
                        isRunning = false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log(ex.ToString());
                }
            }
            else
            {
                isRunning = true;
                listener.StartServer();
            }
        }

        static void ImportPayloads() {
            if (listener != null && isRunning) {
                if (listener.jsonData.Count > 0) {
                    try {
                        JArray receivedArray = JArray.Parse(listener.jsonData[0]);
                        List<JObject> payloads = new List<JObject>();
                        for (int i = 0; i < receivedArray.Count; ++i)
                        {
                            JObject payload = receivedArray[i].ToObject<JObject>();
                            payloads.Add(payload);
                        }

                        // Used to focus the content browser on the last import
                        string lastFolderPath = null;

                        for (int i = 0; i < payloads.Count; ++i)
                        {
                            // Parse the received payload
                            // TODO : we'd want to make sure this can't cause issues
                            JObject payload = payloads[i];
                            string id = (string)(payload["id"] ?? "undefined");
                            string path = (string)(payload["path"] ?? "undefined");
                            JArray models = payload["meshes"] as JArray;
                            JArray materials = payload["materials"] as JArray;
                            JArray native_files = payload["native_files"] as JArray;
                            JArray additional_textures = payload["additional_textures"] as JArray;
                            JObject metadata = payload["metadata"].ToObject<JObject>();
                            JObject metadataFab = metadata["fab"].ToObject<JObject>();
                            JObject metadataFabListing = metadataFab["listing"].ToObject<JObject>();
                            JObject category = metadataFabListing["category"].ToObject<JObject>();
                            string format = (string)(metadataFab["format"] ?? "");
                            string quality = (string)(metadataFab["quality"] ?? "");
                            string category_slug = (string)(category["slug"] ?? "");
                            bool isQuixel = (bool)(metadataFab["isQuixel"] ?? false);
                            string title = (string)(metadataFabListing["title"] ?? "");
                            string listingType = (string)(metadataFabListing["listingType"] ?? "");
                            bool isPlant = isQuixel && category_slug.StartsWith("nature-plants--plants");


                            // TODO: those could use some more love, or UI settings
                            // Currently deactivated and not necessarily plugged in for now
                            // bool SETTINGSsetupCollision = false;
                            // bool SETTINGSapplyToSelection = false;
                            // bool SETTINGSaddAssetToScene = false;
                            // bool SETTINGScreatePrefabs = false;
                            // bool SETTINGSenablePlugin = true;
                            // string SETTINGSLODFadeMode = "";
                            // string SETTINGSImportPath = "/Fab";

                            // Directory paths
                            // TODO : needs a bit of cleanup to match the UE structure
                            string fabPath = FabUtilities.ValidateFolderCreate("Assets", "Fab_content");
                            string megascansPath = "";
                            if(isQuixel) 
                            {
                                megascansPath = FabUtilities.ValidateFolderCreate(fabPath, "Megascans");
                            }
                            string assetDirectoryName = (title!="") ? FabUtilities.SanitizeName(title) : id;
                            if(isQuixel && (quality!=""))
                            {
                                assetDirectoryName = assetDirectoryName + "_" + quality;
                            }
                            string rootPath = "";

                            // Only create a folder for assets that do not have a unitypackage file
                            bool hasUnityPackageFile = (native_files != null) && native_files.Count > 0;
                            if (!hasUnityPackageFile)
                            {
                                rootPath = FabUtilities.ValidateFolderCreate(isQuixel ? megascansPath : fabPath, assetDirectoryName, true);
                            }
                            else
                            {
                                // Import native (.unitypackage) files
                                int numUnityPackages = 0;
                                for (int j = 0; j < native_files.Count; j++)
                                {
                                    string native_file_path = (string)native_files[j];
                                    string extension = Path.GetExtension(native_file_path);
                                    if (extension.ToLower() == ".unitypackage")
                                    {
                                        numUnityPackages += 1;
                                        // Don't show the interactive import window if multiple payloads are received (aka bulk)
                                        bool needsInteractiveImport = (payloads.Count == 1) && (native_files.Count == 1);
                                        AssetDatabase.ImportPackage(native_file_path, needsInteractiveImport);
                                    }
                                    else
                                    {
                                        FabUtilities.sendStatus("warning", "A native file with unsupported extension " + extension + " was provided", id, path);
                                        continue;
                                    }
                                }
                                if (numUnityPackages == 0)
                                {
                                    FabUtilities.sendStatus("critical", "No .unitypackage files found in payload, this should not happen", id, path);
                                }
                                else
                                {
                                    FabUtilities.sendStatus("success", numUnityPackages + " Unity package(s) imported", id, path);
                                }
                                continue;
                            }

                            // Import materials
                            List<Material> imported_materials = new List<Material>{};
                            if(materials != null) {
                                for(int j = 0 ; j < materials.Count ; j++)
                                {
                                    try
                                    {
                                        // Parse material payload
                                        JObject material_payload = materials[j].ToObject<JObject>();
                                        string name = (string)(material_payload["name"] ?? "material");
                                        JObject texturesObject = material_payload["textures"].ToObject<JObject>();
                                        bool flipnmapgreenchannel = (bool)(material_payload["flipnmapgreenchannel"] ?? false);
                                        Material material = MegascansMaterialUtils.ProcessMaterial(rootPath, name + "_" + j.ToString(), texturesObject, isPlant, flipnmapgreenchannel);
                                        imported_materials.Add(material);
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.Log("Error importing material");
                                        Debug.Log(ex.ToString());
                                        FabUtilities.sendStatus("warning", "An issue occured during material import", id, path);
                                        imported_materials.Add(null);
                                    }
                                }
                            }

                            // Import models, potentially with assigning materials to them
                            List<string> imported_models = new List<string>{};
                            if(models != null)
                            {
                                for(int j = 0 ; j < models.Count ; j++)
                                {
                                    try
                                    {
                                        // Parse model payload
                                        JObject model_payload = models[j].ToObject<JObject>();
                                        string model_file = (string)model_payload["file"];
                                        string name = (string)(model_payload["name"] ?? "");
                                        int material_index = Convert.ToInt32(model_payload["material_index"] ?? -1);
                                        
                                        JArray lods = JArray.Parse(((string)payload["lods"] ?? "[]"));

                                        // Trigger an import according to the extension
                                        string[] usdExtensions = { ".usda", ".usdc", ".usdz", ".usd" };
                                        string[] gltfExtensions = { ".glb", ".gltf" };
                                        string[] exchangeExtensions = { ".fbx", ".obj" };
                                        string extension = Path.GetExtension(model_file).ToLower();

                                        if (Array.IndexOf(gltfExtensions, extension) > -1)
                                        {
                                            // GLTF importers (gltffast vs Khronos) are tricky in unity. In a few words:
                                            // Khronos : better compatibility with edge cases / warnings, better support for extensions, more updates
                                            // gltffast : more "official", quicker, easier to setup, better HDRP, doesn't require a git install, can fail for dubious gltf
                                            // TODO : need to define when to use which, or let the user choose with a setting or based on what they have
                                            // bool gltfKhronosSetup = FabUtilities.SetupPackageInProject("https://github.com/KhronosGroup/UnityGLTF.git#release/2.17.8", "org.khronos.unitygltf");
                                            // if (!gltfKhronosSetup)
                                            // {
                                            //     Debug.Log("Failed installing org.khronos.unitygltf, required for gltf imports");
                                            // }
                                            bool gltffastSetup = FabUtilities.SetupPackageInProject("com.unity.cloud.gltfast");
                                            if (!gltffastSetup)
                                            {
                                                Debug.Log("Failed installing com.unity.cloud.gltfast, required for gltf imports");
                                            }
                                            if(!gltffastSetup)
                                            {
                                                FabUtilities.sendStatus("warning", "Failed installing a gltf package, cannot import gltf assets", id, path);
                                                continue;
                                            }

                                            if (extension == ".glb")
                                            {
                                                string importPath = Path.Combine(rootPath, Path.GetFileName(model_file));
                                                FabUtilities.CopyFileToProject(model_file, importPath);
                                                AssetDatabase.ImportAsset(importPath);
                                                AssetDatabase.Refresh();
                                                imported_models.Add(importPath);
                                            }
                                            else
                                            {
                                                // Not perfect as this doesn't take into account a deeply nested gltf with relative imports
                                                string gltfDirectory = Path.GetDirectoryName(model_file);
                                                if(isQuixel && gltfDirectory.Contains("standard"))
                                                {
                                                    gltfDirectory = Path.GetDirectoryName(gltfDirectory);
                                                    // TODO : we could remove any other gltf that is not in stnadard ?
                                                }
                                                FabUtilities.CopyDirectory(gltfDirectory, rootPath);
                                                imported_models.Add(rootPath);

                                                // TODO: ideally, add a filter on userdata to make sure postprocessors only act on imported files through plugin
                                            }
                                        }
                                        else if (Array.IndexOf(usdExtensions, extension) > -1)
                                        {
                                            if (!FabUtilities.SetupPackageInProject("com.unity.importer.usd"))
                                            {
                                                string[] versionParts = Application.unityVersion.Split('.');
                                                if (versionParts.Length > 0 && int.TryParse(versionParts[0], out int major) && major < 2023)
                                                {
                                                    FabUtilities.sendStatus("warning", $"USD import not supported with Unity {major}", id, path);
                                                }
                                                else
                                                {
                                                    FabUtilities.sendStatus("warning", "Failed installing com.unity.importer.usd", id, path);
                                                }
                                                continue;
                                            }
                                            FabUtilities.CopyDirectory(Path.GetDirectoryName(model_file), rootPath);
                                            AssetDatabase.ImportAsset(rootPath);

                                            AssetDatabase.Refresh();

                                            // once copied, find the first usd object in the new path and try to mark it as "imported"
                                            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { rootPath });
                                            var paths = guids
                                                .Select(AssetDatabase.GUIDToAssetPath)
                                                .Where(p => usdExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                                                .ToList();
                                            string firstUsd = paths.Count > 0 ? paths[0] : null;
                                            if (firstUsd != null)
                                            {
                                                // Below, we need a hack to check the type through reflection, and avoid
                                                // compilation-time issues when package has not been installed yet
                                                var importer = AssetImporter.GetAtPath(firstUsd);
                                                if (importer == null)
                                                {
                                                    Debug.Log($"No importer found for: {firstUsd}, this should not happen");
                                                    return;
                                                }

                                                Type importerType = Type.GetType("UnityEditor.Importer.USD.UsdModularImporter, Unity.Importer.USD.Editor", throwOnError: false);
                                                if (importerType == null)
                                                {
                                                    Debug.LogError($"No USD importer found for: {firstUsd}, this should not happen");
                                                    continue;
                                                }

                                                if (!importerType.IsInstanceOfType(importer))
                                                {
                                                    Debug.LogError($"Importer of type '{importer.GetType().FullName}' is not of USD-type.");
                                                    continue;
                                                }

                                                var isUsdRootField = importerType.GetField("isUsdRoot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                                var saveAndReimport = importerType.GetMethod("SaveAndReimport", BindingFlags.Public | BindingFlags.Instance) ?? typeof(AssetImporter).GetMethod("SaveAndReimport", BindingFlags.Public | BindingFlags.Instance);
                                                if (isUsdRootField != null)
                                                {
                                                    // Read current value
                                                    var isUsdRoot = (bool)isUsdRootField.GetValue(importer);
                                                    if (!isUsdRoot)
                                                    {
                                                        isUsdRootField.SetValue(importer, true);
                                                        EditorUtility.SetDirty(importer);
                                                        saveAndReimport?.Invoke(importer, null);
                                                    }
                                                }
                                                else
                                                {
                                                    Debug.LogWarning("Could not find isUsdRoot through reflection, this should not happen");
                                                    throw new Exception("Could not find isUsdRoot through reflection, this should not happen");
                                                }
                                            }

                                            imported_models.Add(rootPath);
                                        }
                                        else if (Array.IndexOf(exchangeExtensions, extension) > -1)
                                        {
                                            // For fbx and obj files, add the material as userdata if it is present
                                            string model_file_copied = Path.Combine(rootPath, Path.GetFileName(model_file));
                                            FabUtilities.CopyFileToProject(model_file, model_file_copied);

                                            AssetImporter importer = AssetImporter.GetAtPath(model_file_copied);

                                            // Safely get the importer
                                            if (importer == null)
                                            {
                                                Debug.LogError($"Failed to get importer for asset at {model_file_copied}");
                                                continue;
                                            }

                                            // Cast to ModelImporter for FBX
                                            ModelImporter modelImporter = importer as ModelImporter;
                                            if (modelImporter == null)
                                            {
                                                Debug.LogError($"Importer at {model_file_copied} is not a ModelImporter.");
                                                continue;
                                            }

                                            modelImporter.userData = "FABIMPORT"; // Flag the importer to identify which assets are coming from this scope

                                            // If there's a material, add it's name to userData
                                            if(material_index != -1 && material_index < imported_materials.Count) 
                                            {
                                                Material material = imported_materials[material_index];
                                                if(material != null)
                                                {
                                                    string material_path = AssetDatabase.GetAssetPath(material);
                                                    importer.userData += ";" + material_path;
                                                }
                                            }
                                            AssetDatabase.ImportAsset(model_file_copied);
                                            imported_models.Add(model_file_copied);
                                        }
                                        else{
                                            FabUtilities.sendStatus("critical", "Received an unrecognized file format " + extension, id, path);
                                            continue;
                                        }

                                        lastFolderPath = rootPath;
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.Log("Error importing model file " + (string)models[j]["file"]);
                                        Debug.Log(ex.ToString());
                                        // MegascansUtilities.HideProgressBar();
                                        FabUtilities.sendStatus("critical", "An issue occured during import", id, path);
                                        listener.jsonData.RemoveAt(0);
                                        return;
                                    }
                                }
                            }

                            // Import additional textures
                            if(additional_textures != null)
                            {
                                if(additional_textures.Count>0)
                                {
                                    string additionalTexturesPath = FabUtilities.ValidateFolderCreate(rootPath, "Additional_Textures");
                                    for(int j = 0 ; j < additional_textures.Count ; j++)
                                    {
                                        try
                                        {
                                            string texturePath = Path.Combine(additionalTexturesPath, Path.GetFileName((string)additional_textures[i]));
                                            FabUtilities.CopyFileToProject((string)additional_textures[i], texturePath);
                                            AssetDatabase.ImportAsset(texturePath);
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.Log(ex.ToString());
                                        }
                                    }
                                }
                            }

                            if(imported_materials.Count > 0 || imported_models.Count > 0)
                            {
                                int numberOfSuccessfulMaterials = 0;
                                for(int j = 0 ; j < imported_materials.Count ; j++)
                                {
                                    if(imported_materials[j] != null)
                                    {
                                        numberOfSuccessfulMaterials = numberOfSuccessfulMaterials + 1;
                                    }
                                }
                                bool materialsImportSuccess = (imported_materials.Count > 0) ? (numberOfSuccessfulMaterials == imported_materials.Count) : true;
                                bool modelsImportSuccess = (imported_models.Count > 0) ? true : materialsImportSuccess;
                                if(materialsImportSuccess && modelsImportSuccess)
                                {
                                    FabUtilities.sendStatus("success", "Import successful", id, path);
                                }
                                else
                                {
                                    Debug.Log("No models nor materials were imported");
                                    FabUtilities.sendStatus("critical", "Import failed, please check your Unity console for errors", id, path);
                                }
                            } 
                            else
                            {
                                Debug.Log("No models nor materials were imported");
                                FabUtilities.sendStatus("critical", "Import failed, please check your Unity console for errors", id, path);
                            }
                        }

                        // Highlight the last imported asset at the end of the import operation.
                        if (lastFolderPath != null)
                        {
                            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath(lastFolderPath, typeof(UnityEngine.Object));
                            Selection.activeObject = folder;
                            EditorGUIUtility.PingObject(folder);
                        }

                        listener.jsonData.RemoveAt(0);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("Error parsing json data.");
                        Debug.LogError("Received data: " + listener.jsonData);
                        Debug.LogError(ex.ToString());
                        listener.jsonData.RemoveAt(0);
                    }
                }
            }
        }
    }
}

public class FbxMaterialPostprocessor : AssetPostprocessor
{
    Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        ModelImporter importer = assetImporter as ModelImporter;
        if (!string.IsNullOrEmpty(importer.userData))
        {
            // Debug.Log("Postprocess userdata: " + importer.userData);
            string[] splitted_userdata = importer.userData.Split(";");
            if (splitted_userdata.Length > 0 && splitted_userdata[0] == "FABIMPORT")
            {
                if (splitted_userdata.Length > 1)
                {
                    string material_path = splitted_userdata[1];
                    Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(material_path);
                    if (existingMaterial != null)
                    {
                        // Debug.Log("Assigned existing material '{existingMaterial.name}' to '{renderer.name}'");
                        return existingMaterial;
                    }
                }
            }
        }
        return null;
    }
}

public class GltfImagesPostProcessor : AssetPostprocessor
{
    static readonly HashSet<string> imageExtensions = new HashSet<string>{".png",".jpg",".jpeg",".tga",".bmp",".tif",".tiff"};
    enum ImageType { Color, Linear, Normal }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        // Use this to avoid spamming in case of issue
        bool oneExceptionAtLeastCaught = false;

        foreach (var gltfPath in imported)
        {
            // TODO add a check to see if it was imported through the plugin with userdata
            if (gltfPath.EndsWith(".gltf"))
            {
                try
                {
                    // Check existence of the gltf file
                    var baseFolder = Path.GetDirectoryName(gltfPath).Replace('\\', '/');
                    var full = Path.Combine(Directory.GetCurrentDirectory().Replace('\\', '/'), gltfPath).Replace('\\', '/');
                    if (!File.Exists(full)) return;

                    // 1 - First parse the gltf json to identify textures
                    // Open the gltf json for parsing
                    var gltfJson = JObject.Parse(File.ReadAllText(full));
                    // Get the textures to image link
                    var textureToImageIndex = new Dictionary<int, int>();
                    JArray gltfTextures = gltfJson["textures"] as JArray;
                    if (gltfTextures != null)
                    {
                        for (int i = 0; i < gltfTextures.Count; i++)
                        {
                            var sourceIndex = gltfTextures[i]?["source"]?.Value<int?>();
                            if (sourceIndex.HasValue) textureToImageIndex[i] = sourceIndex.Value;
                        }
                    }
                    // Get the image to uri link
                    var imageIndexToUri = new Dictionary<int, string>();
                    JArray gltfImages = gltfJson["images"] as JArray;
                    if (gltfImages != null)
                    {
                        for (int i = 0; i < gltfImages.Count; i++)
                        {
                            var uri = gltfImages[i]?["uri"]?.Value<string>();
                            if (!string.IsNullOrEmpty(uri) && !uri.StartsWith("data:"))
                            {
                                imageIndexToUri[i] = uri.Replace('\\', '/');
                            }
                        }
                    }

                    // 2 - Then for each uri, find its image type based on its usage in the gltf json
                    var uriToImageType = new Dictionary<string, HashSet<ImageType>>();
                    void SetImageType(int? textureIndex, ImageType t)
                    {
                        if (!textureIndex.HasValue)
                        {
                            return;
                        }
                        if (!textureToImageIndex.TryGetValue(textureIndex.Value, out var imageIndex))
                        {
                            return;
                        }
                        if (!imageIndexToUri.TryGetValue(imageIndex, out var uri))
                        {
                            return;
                        }
                        if (!uriToImageType.TryGetValue(uri, out var ImageTypes))
                        {
                            // Use a hashset as a single texture can be referenced as multiple usages (basecolor and emissive for isntance)
                            ImageTypes = new HashSet<ImageType>();
                            uriToImageType[uri] = ImageTypes;
                        }
                        ImageTypes.Add(t);
                    }
                    JArray gltfMaterials = gltfJson["materials"] as JArray;
                    if (gltfMaterials != null)
                    {
                        foreach (var m in gltfMaterials)
                        {
                            SetImageType(m["pbrMetallicRoughness"]?["baseColorTexture"]?["index"]?.Value<int?>(), ImageType.Color);
                            SetImageType(m["pbrMetallicRoughness"]?["metallicRoughnessTexture"]?["index"]?.Value<int?>(), ImageType.Linear);
                            SetImageType(m["normalTexture"]?["index"]?.Value<int?>(), ImageType.Normal);
                            SetImageType(m["occlusionTexture"]?["index"]?.Value<int?>(), ImageType.Linear);
                            SetImageType(m["emissiveTexture"]?["index"]?.Value<int?>(), ImageType.Color);
                        }
                    }

                    // Apply import settings
                    foreach (var uriImageTypePair in uriToImageType)
                    {
                        var uri = uriImageTypePair.Key;
                        var imageTypes = uriImageTypePair.Value;

                        // Get the texture asset path
                        var assetTexturePath = ResolveAssetPath(baseFolder, uri);
                        if (assetTexturePath == null)
                        {
                            continue;
                        }

                        // Check that it is a correct image
                        var ext = Path.GetExtension(assetTexturePath).ToLowerInvariant();
                        if (!imageExtensions.Contains(ext))
                        {
                            continue;
                        }

                        // Finally, get the texture importer if available
                        var textureImporter = AssetImporter.GetAtPath(assetTexturePath) as TextureImporter;
                        if (textureImporter == null) continue;
                        bool isRGB = textureImporter.sRGBTexture;
                        bool isDefault = textureImporter.textureType == TextureImporterType.Default;
                        bool isNormal = textureImporter.textureType == TextureImporterType.NormalMap;

                        // We'll only trigger a reimport if settings have changed
                        bool needsSaveAndReimport = false;

                        // Handle the different cases, and don't do anything in case of texture reuse across linear/color
                        bool needsNormal = imageTypes.Contains(ImageType.Normal);
                        bool needsColor = imageTypes.Contains(ImageType.Color);
                        bool needsLinear = imageTypes.Contains(ImageType.Linear);
                        if (needsNormal)
                        {
                            needsSaveAndReimport = isRGB || !isNormal;
                            textureImporter.textureType = TextureImporterType.NormalMap;
                            textureImporter.sRGBTexture = false;
                        }
                        else if (needsColor && !needsLinear)
                        {
                            needsSaveAndReimport = !isRGB || !isDefault;
                            textureImporter.textureType = TextureImporterType.Default;
                            textureImporter.sRGBTexture = true;
                        }
                        else if (needsLinear && !needsColor)
                        {
                            needsSaveAndReimport = isRGB || !isDefault;
                            textureImporter.textureType = TextureImporterType.Default;
                            textureImporter.sRGBTexture = false;
                        }

                        // Finally, save and reimport if needed
                        if (needsSaveAndReimport)
                        {
                            textureImporter.SaveAndReimport();
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!oneExceptionAtLeastCaught)
                    {
                        Debug.Log($"Failed setting colorspace info for {gltfPath}, this should not happen");
                        Debug.Log(ex.ToString());
                        oneExceptionAtLeastCaught = true;
                    }
                }
            }
        }
    }

    static string ResolveAssetPath(string baseFolder, string relative)
    {
        var projectPath = Directory.GetCurrentDirectory().Replace('\\','/') + "/";
        var absolutePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), baseFolder, relative)).Replace('\\','/');
        if (!absolutePath.StartsWith(projectPath))
        {
            // Avoid any ../ issue
            return null;
        }
        return File.Exists(absolutePath) ? absolutePath.Substring(projectPath.Length) : null;
    }
}

#endif
