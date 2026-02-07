#if false
#if UNITY_EDITOR


using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Fab {
    public class MegascansUtilities : MonoBehaviour {
        
        

        // Tells if an asset type is scatter or not. 
        public static bool isScatterAsset(JObject assetJson, List<string> importedMeshpaths)
        {
            try
            {
                string[] tags = assetJson["tags"].ToObject<string[]>();
                string[] categories = assetJson["categories"].ToObject<string[]>();
                int childCount = GetMeshChildrenCount(importedMeshpaths);

                foreach(string tag in tags)
                {
                    if (tag.ToLower() == "scatter")
                    {
                        return (childCount > 1); //Returns false if the is only one variation of asset.
                    } else if (tag.ToLower() == "cmb_asset")
                    {
                        return (childCount > 1); //Returns false if the is only one variation of asset.
                    }
                }

                foreach (string category in categories)
                {
                    if (category.ToLower() == "scatter")
                    {
                        return (childCount > 1); //Returns false if the is only one variation of asset.
                    }
                    else if (category.ToLower() == "cmb_asset")
                    {
                        return (childCount > 1); //Returns false if the is only one variation of asset.
                    }
                }

                return (childCount > 1);
            }
            catch (Exception ex)
            {
                Debug.Log("Exception::MegascansUtilities::IsScatterAsset:: " + ex.ToString());
                HideProgressBar();
            }

            return false;
        }

        public static int GetMeshChildrenCount(List<string> importedMeshpaths)
        {
            try
            {
                if(importedMeshpaths.Count > 0)
                {
                    UnityEngine.Object loadedGeometry = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(importedMeshpaths[0]);
                    GameObject testGO = (GameObject)Instantiate(loadedGeometry);
                    int count = testGO.transform.childCount;
                    DestroyImmediate(testGO);
                    return count;
                }
            } catch (Exception ex)
            {
                Debug.Log("Exception::MegascansUtilities::GetMeshChildrenCount:: " + ex.ToString());
                HideProgressBar();
            }

            return 1;
        }


        /// <summary>
        /// Retrieves selected folders in Project view.
        /// </summary>
        public static List<string> GetSelectedFolders (List<UnityEngine.Object> selections) {
            List<string> folders = new List<string> ();

            foreach (UnityEngine.Object obj in selections) //Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath (obj);
                if (!string.IsNullOrEmpty (path)) {
                    folders.Add (path);
                }
            }
            return folders;
        }

        /// <summary>
        /// Retrieves selected GameObjects with MeshRenderer component in Scene view.
        /// </summary>
        public static List<MeshRenderer> GetSelectedMeshRenderers()
        {
            List<MeshRenderer> meshRenderers = new List<MeshRenderer>();

            foreach (GameObject g in Selection.gameObjects)
            {
                if (g.GetComponent<MeshRenderer>() != null)
                {
                    meshRenderers.Add(g.GetComponent<MeshRenderer>());
                }
            }

            return meshRenderers;
        }

        /// <summary>
        /// Recursively gather all files under the given path including all its subfolders.
        /// </summary>
        public static List<string> GetFiles (string path, string fileType = null) {
            List<string> files = new List<string> ();
            Queue<string> queue = new Queue<string> ();
            queue.Enqueue (path);
            while (queue.Count > 0) {
                path = queue.Dequeue ();
                foreach (string subDir in Directory.GetDirectories (path)) {
                    queue.Enqueue (subDir);
                }
                foreach (string s in Directory.GetFiles (path)) {
                    if (fileType != null && s.Contains (fileType)) {
                        if (s.Contains (fileType)) {
                            files.Add (s);
                        }
                    } else {
                        files.Add (s);
                    }

                }
            }
            return files;
        }

        static float maxNumberOfOperations = 0;
        static float currentOperationCount = 0;

        public static void CalculateNumberOfOperations(JObject assetData, int dispType, int texPack, bool hasBillboardLODOnly)
        {
            JArray meshComps = (JArray)assetData["meshList"];
            int prefabCount = meshComps.Count;

            JArray lodList = (JArray)assetData["lodList"];
            int meshCount = meshComps.Count;

            JArray textureComps = (JArray)assetData["components"];

            List<string> texTypes = new List<string>();

            for (int i = 0; i < textureComps.Count; ++i)
            {
                texTypes.Add((string)textureComps[i]["type"]);
            }

            int texCount = 0;

            if (texTypes.Contains("albedo") || texTypes.Contains("diffuse"))
                texCount++;

            if (texTypes.Contains("normal"))
                texCount++;

            if (texTypes.Contains("displacement") && dispType != 0)
                texCount++;

            if (texTypes.Contains("translucency"))
                texCount++;

            if (texTypes.Contains("occlusion"))
                texCount++;

            if (texPack == 0)
            {
                if (texTypes.Contains("metal") || texTypes.Contains("roughness") || texTypes.Contains("glossiness") || texTypes.Contains("occlusion") || texTypes.Contains("displacement"))
                    texCount++;
            }
            else if (texPack == 1)
            {
                if ((texTypes.Contains("metal") || texTypes.Contains("roughness") || texTypes.Contains("glossiness") || texTypes.Contains("occlusion") || texTypes.Contains("displacement")))
                    texCount++;

                if (texTypes.Contains("specular"))
                    texCount++;
            }

            string type = (string)assetData["type"];
            if (type.ToLower().Contains("3dplant") && !hasBillboardLODOnly)
            {
                texCount *= 2;
            }

            maxNumberOfOperations = (float)(prefabCount + meshCount + texCount);
            maxNumberOfOperations += 1.0f; //For the material
        }

        public static void UpdateProgressBar(float change = 0, string header = "Import Megascans Asset", string message = "Processing Asset")
        {
            currentOperationCount += change;
            if (currentOperationCount != maxNumberOfOperations)
                EditorUtility.DisplayProgressBar(header, message, (currentOperationCount / maxNumberOfOperations));
            else
                HideProgressBar();
        }

        public static void HideProgressBar()
        {
            currentOperationCount = 0;
            maxNumberOfOperations = 0;
            EditorUtility.ClearProgressBar();
        }

        public static List<float> getLODHeightList(int numberOfFiles)
        {
            switch (numberOfFiles)
            {
                case 1:
                    return new List<float> { 0.01f };
                case 2:
                    return new List<float> { 0.4f, 0.01f };
                case 3:
                    return new List<float> { 0.5f, 0.2f, 0.01f };
                case 4:
                    return new List<float> { 0.5f, 0.3f, 0.18f, 0.01f };
                case 5:
                    return new List<float> { 0.5f, 0.3f, 0.2f, 0.1f, 0.01f };
                case 6:
                    return new List<float> { 0.55f, 0.35f, 0.24f, 0.15f, 0.07f, 0.01f };
                case 7:
                    return new List<float> { 0.6f, 0.4f, 0.3f, 0.21f, 0.13f, 0.06f, 0.01f };
                default:
                    return new List<float> { 0.65f, 0.45f, 0.35f, 0.26f, 0.18f, 0.11f, 0.06f, 0.01f };
            }
        }
    }
}

#endif

public enum Pipeline
{
    HDRP,
    LWRP,
    Standard
}
#endif
