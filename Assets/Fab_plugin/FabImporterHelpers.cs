#if UNITY_EDITOR

using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

namespace Fab
{
	public class MegascansMaterialUtils : MonoBehaviour
	{

		public static Texture2D PackORMTexture(Dictionary<string, string> textures, string texturesPath, bool isHDRP)
		{
			bool hasAO = textures.ContainsKey("occlusion");
			bool hasMetal = textures.ContainsKey("metal");
			bool hasRough = textures.ContainsKey("roughness");
			bool hasGloss = textures.ContainsKey("glossiness");
			// If hasAO only and shader is not HDRP, we don't need to pack anything here
			if((hasMetal || hasRough || hasGloss) || (hasAO && isHDRP))
			{
				// Create a temporary folder for the textures
				string temporaryTexturesPath = FabUtilities.ValidateFolderCreate(texturesPath, "Packed_Textures");

				// Get or create textures
				Texture2D TextureMetal = null;
				Texture2D TextureAO = null;
				Texture2D TextureRough = null;
				Texture2D TextureGloss = null;
				string pathTextureMetal = "";
				string pathTextureAO = "";
				string pathTextureRough = "";
				string pathTextureGloss = "";
				int width = 0;
				int height = 0;
				bool haveConsistentSizes = true;
				if(hasAO)
				{
					pathTextureAO = Path.Combine(temporaryTexturesPath, Path.GetFileName(textures["occlusion"]));
					TextureAO = ImportTexture(textures["occlusion"], pathTextureAO, false, false, true);
					if(width == 0 && height == 0) {
						width = TextureAO.width;
						height = TextureAO.height;
					} else {
						if((width != TextureAO.width) || (height != TextureAO.height)) {
							haveConsistentSizes = false;
						}
					}
				}
				if(hasMetal)
				{
					pathTextureMetal = Path.Combine(temporaryTexturesPath, Path.GetFileName(textures["metal"]));
					TextureMetal = ImportTexture(textures["metal"], pathTextureMetal, false, false, true);
					if(width == 0 && height == 0) {
						width = TextureMetal.width;
						height = TextureMetal.height;
					} else {
						if((width != TextureMetal.width) || (height != TextureMetal.height)) {
							haveConsistentSizes = false;
						}
					}
				}
				if(hasRough || hasGloss)
				{
					if(hasRough)
					{
						pathTextureRough = Path.Combine(temporaryTexturesPath, Path.GetFileName(textures["roughness"]));
						TextureRough = ImportTexture(textures["roughness"], pathTextureRough, false, false, true);
						if(width == 0 && height == 0) {
							width = TextureRough.width;
							height = TextureRough.height;
						} else {
							if((width != TextureRough.width) || (height != TextureRough.height)) {
								haveConsistentSizes = false;
							}
						}
					}
					else
					{
						pathTextureGloss = Path.Combine(temporaryTexturesPath, Path.GetFileName(textures["glossiness"]));
						TextureGloss = ImportTexture(textures["glossiness"], pathTextureGloss, false, false, true);
						if(width == 0 && height == 0) {
							width = TextureGloss.width;
							height = TextureGloss.height;
						} else {
							if((width != TextureGloss.width) || (height != TextureGloss.height)) {
								haveConsistentSizes = false;
							}
						}
					}
				}

				if(!haveConsistentSizes)
				{
					// TODO: We could avoid this at export, at import, or by dynamically resizing textures together
					Debug.Log("Textures to pack were not of a consistent size");
					return null;
				}

				// Create a black texture by default
				Texture2D packedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        		Color[] blackPixels = new Color[width * height];
				Color32 blackColor = new Color32(0, 0, 0, 255);
				for (int i = 0; i < blackPixels.Length; i++)
				{
					blackPixels[i] = blackColor;
				}
				packedTexture.SetPixels(blackPixels);
				packedTexture.Apply();

				// Do the packing
				Color[] aoPixels = hasAO ? TextureAO.GetPixels() : blackPixels;
				Color[] metalPixels = hasMetal ? TextureMetal.GetPixels() : blackPixels;
				Color[] glossPixels = blackPixels;
				if(hasRough) {
					// Invert the roughness map
					glossPixels = TextureRough.GetPixels();
					for (int i = 0; i < glossPixels.Length; i++)
					{
						Color c = glossPixels[i];
						c.r = 1f - c.r;
						glossPixels[i] = c;
					}
				}
				else if(hasGloss)
				{
					glossPixels = TextureGloss.GetPixels();
				}
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						int index = x + width * y + 0;
						float r = metalPixels[index].r;
						float g = aoPixels[index].r;
						float b = 0;
						float a = glossPixels[index].r;
						packedTexture.SetPixel(x, y, new Color(r, g, b, a));
					}
				}
				packedTexture.Apply();
				// TODO : make unique names or handle conflicts
				AssetDatabase.CreateAsset(packedTexture, Path.Combine(temporaryTexturesPath, "packedORM.asset"));

				// Clean up the imported textures
				if(TextureMetal) AssetDatabase.DeleteAsset(pathTextureMetal);
				if(TextureAO) AssetDatabase.DeleteAsset(pathTextureAO);
				if(TextureRough) AssetDatabase.DeleteAsset(pathTextureRough);
				if(TextureGloss) AssetDatabase.DeleteAsset(pathTextureGloss);
				return packedTexture;
			}
			return null;
		}

		public static Texture2D PackAlbedoTexture(Dictionary<string, string> textures, string texturesPath)
		{
			bool hasAlbedo = textures.ContainsKey("albedo");
			bool hasOpacity = textures.ContainsKey("opacity");
			if(!hasAlbedo) return null;

			string temporaryTexturesPath = FabUtilities.ValidateFolderCreate(texturesPath, "Packed_Textures");
			string albedoPath = Path.Combine(hasOpacity ? temporaryTexturesPath : texturesPath, Path.GetFileName(textures["albedo"]));
			Texture2D TextureAlbedo = ImportTexture(textures["albedo"], albedoPath, false, true, true);

			if(hasOpacity)
			{
				string opacityPath = Path.Combine(temporaryTexturesPath, Path.GetFileName(textures["opacity"]));
				Texture2D TextureOpacity = ImportTexture(textures["opacity"], Path.Combine(temporaryTexturesPath, Path.GetFileName(textures["opacity"])), false, false, true);

				// Textures need to be the same size
				if((TextureOpacity.height != TextureAlbedo.height) || (TextureOpacity.width != TextureAlbedo.width))
				{
					return null;
				}
				
				Texture2D packedTexture = new Texture2D(TextureOpacity.width, TextureOpacity.height, TextureFormat.RGBA32, false);

				// Create a black texture by default
        		Color[] albedoPixels = TextureAlbedo.GetPixels();
        		Color[] opacityPixels = TextureOpacity.GetPixels();
				int w = TextureOpacity.width;
				for (int y = 0; y < TextureOpacity.height; y++)
				{
					for (int x = 0; x < w; x++)
					{
						int index = x + w * y + 0;
						Color albedo = albedoPixels[index];
						float r = albedo.r;
						float g = albedo.g;
						float b = albedo.b;
						float a = opacityPixels[index].r;
						packedTexture.SetPixel(x, y, new Color(r, g, b, a));
					}
				}
				packedTexture.Apply();
				AssetDatabase.CreateAsset(packedTexture, Path.Combine(temporaryTexturesPath, "packedColor.asset"));

				// Clean up the imported textures
				if(TextureAlbedo) AssetDatabase.DeleteAsset(albedoPath);
				if(TextureOpacity) AssetDatabase.DeleteAsset(opacityPath);

				return packedTexture;
			}
			else 
			{
				return TextureAlbedo;
			}
		}


		public static Material ProcessMaterial(string rootPath, string materialName, JObject texturesObject, bool isPlant, bool flipGreenChannel)
		{

			Dictionary<string, string> textures = texturesObject.ToObject<Dictionary<string, string>>();

			// This would not be efficient for thousands of materials
			string texturesPath = FabUtilities.ValidateFolderCreate(rootPath, "Textures");
			string materialsPath = FabUtilities.ValidateFolderCreate(rootPath, "Materials");
			string rp = Path.Combine(materialsPath, materialName) + ".mat";

			// Check if the material already exists
			Material mat = (Material)AssetDatabase.LoadAssetAtPath(rp, typeof(Material));
			if(mat)
			{
				Debug.Log("Ignoring material creation as material already exists at " + rp);
				return mat;
			}

			// Create the material with the appropriate shader
			int shaderType = (int)FabUtilities.GetProjectPipeline();
			bool isHDRP = shaderType == 0;
			bool isURP = shaderType == 1;
			bool isStandard = shaderType == 2;
			int dispType = textures.ContainsKey("displacement") ? 3 : 0;

			string shaderName = "UNKNOWN";
			if(isHDRP) shaderName = dispType < 3 ? "HDRP/Lit" : "HDRP/LitTessellation";
			if(isURP) shaderName = "Universal Render Pipeline/Lit";
			if(isStandard) shaderName = "Standard";
			mat = new Material(Shader.Find(shaderName));
			AssetDatabase.CreateAsset(mat, rp);

			// Set some generic properties
			if (isHDRP)
			{
				if(textures.ContainsKey("translucency") || textures.ContainsKey("transmission"))
				{
					//AddDiffusionProfile();
					Debug.Log("Diffusion profile is temporary disabled for translucency and transmission");
				}
				mat.renderQueue = 2225; 

				mat.EnableKeyword("_DISABLE_SSR_TRANSPARENT");

				mat.SetShaderPassEnabled("DistortionVectors", false);
				mat.SetShaderPassEnabled("MOTIONVECTORS", false);
				mat.SetShaderPassEnabled("TransparentDepthPrepass", false);
				mat.SetShaderPassEnabled("TransparentDepthPostpass", false); 
				mat.SetShaderPassEnabled("TransparentBackface", false);
				mat.SetShaderPassEnabled("RayTracingPrepass", false);

				mat.SetColor("_EmissionColor", Color.white);
				mat.SetFloat("_AlphaDstBlend", 0.0f);
				mat.SetFloat("_StencilRefDepth", 8f);
				mat.SetFloat("_StencilWriteMask", 6f);
				mat.SetFloat("_StencilWriteMaskGBuffer", 14f);
				mat.SetFloat("_StencilWriteMaskMV", 40f);
				mat.SetFloat("_StencilRefMV", 40f);
				mat.SetFloat("_ZTestDepthEqualForOpaque", 3f);
				mat.SetFloat("_ZWrite", 1.0f);
			}

			// Handle the packed texture (metallic, ao, _, smoothness)
			Texture2D packedORM = PackORMTexture(textures, texturesPath, isHDRP);
			if (packedORM != null)
			{
				if (!textures.ContainsKey("metal")) mat.SetFloat("_Metallic", 1.0f);
				if(isHDRP)
				{
					mat.SetTexture("_MaskMap", packedORM);
				}
				else {
					mat.SetTexture("_MetallicGlossMap", packedORM);
					if (textures.ContainsKey("occlusion")) mat.SetTexture("_OcclusionMap", packedORM);
				}
			}

			// Handle the packed texture (metallic, ao, _, smoothness)
			Texture2D packedAlbedo = PackAlbedoTexture(textures, texturesPath);
			if (packedAlbedo != null)
			{
				// Albedo parameters
				if (isStandard)
				{
					mat.SetTexture("_MainTex", packedAlbedo);
				}
				else{
					mat.SetTexture(isHDRP ? "_BaseColorMap" : "_BaseMap", packedAlbedo);
					mat.SetColor("_BaseColor", Color.white);
				}

				// OPacity parameters
				if(textures.ContainsKey("opacity"))
				{
					float alphaCutoff = 0.33f;
					if (isStandard)
					{
						mat.SetFloat("_Cutoff", alphaCutoff);
					}
					if (isURP) // URP
				    {
				        mat.SetFloat("_Cutoff", alphaCutoff);
						mat.SetFloat("_Cull", 0);
				    }
				    if (isHDRP) // HDRP
				    {
						mat.SetFloat("_AlphaCutoff", alphaCutoff);
				        mat.SetInt("_DoubleSidedEnable", 1);
				        mat.SetInt("_CullMode", (int)UnityEngine.Rendering.CullMode.Off);
				        mat.SetInt("_CullModeForward", (int)UnityEngine.Rendering.CullMode.Back);
						mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
						mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
						mat.SetInt("_ZWrite", 1);
						mat.SetInt("_ZTestGBuffer", (int)UnityEngine.Rendering.CompareFunction.Equal);
				    }
				}
			}

			// Iterate over textures
			foreach(var (channel, texture_file) in textures)
			{
				string textureName = Path.GetFileName(texture_file);
				string destTexPath = Path.Combine(texturesPath, textureName);

				if (channel == "normal")
				{
					Texture2D tex = ImportTexture(texture_file, destTexPath, true, false, false, flipGreenChannel);
					mat.SetTexture(isHDRP ? "_NormalMap" : "_BumpMap", tex);

					mat.EnableKeyword("_NORMALMAP");
					if(isHDRP) mat.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
					// TODO: we should handle flipnmapgreenchannel here
				}
				else if(channel == "displacement")
				{
					Texture2D tex = ImportTexture(texture_file, destTexPath, false, false);
					mat.SetTexture(isHDRP ? "_HeightMap" : "_ParallaxMap", tex);

					if (isHDRP)
					{
						// Set parametrization to Amplitude
						mat.SetInt("_HeightMapParametrization", 1); 
						// Amplitude properties
						// The following properties are already set to optimum amount in the shader file as stated below
						// _HeightTessAmplitude("Amplitude", Float) = 2.0 
						// _HeightOffset("Height Offset", Float) = 0
						// _HeightTessCenter("Height Center", Range(0.0, 1.0)) = 0.5

						mat.EnableKeyword("_HEIGHTMAP");
						mat.EnableKeyword("_DISPLACEMENT_LOCK_TILING_SCALE");
						if (dispType == 1)
						{
							mat.EnableKeyword("_VERTEX_DISPLACEMENT");
							mat.EnableKeyword("_VERTEX_DISPLACEMENT_LOCK_OBJECT_SCALE");
						}
						else if (dispType == 2)
						{
							mat.EnableKeyword("_PARALLAXMAP");
							mat.EnableKeyword("_PIXEL_DISPLACEMENT");
							mat.EnableKeyword("_PIXEL_DISPLACEMENT_LOCK_OBJECT_SCALE");
						}
						else if (dispType == 3)
						{
							mat.EnableKeyword("_TESSELLATION_ON");
							mat.SetFloat("_DisplacementLockObjectScale", 1.0f);
							mat.SetFloat("_DisplacementLockTilingScale", 1.0f);
							mat.SetFloat("_TessellationMode", 1.0f);
							mat.SetFloat("_TessellationFactor", 8.0f);
							mat.SetFloat("_TessellationFactorMinDistance", 10.0f);
							mat.SetFloat("_TessellationFactorMaxDistance", 30.0f);
						}
					}
					mat.SetInt("_DisplacementMode", (dispType == 3) ? 1 : dispType);
				}
				else if (channel == "translucency")
				{
					Texture2D tex = ImportTexture(texture_file, destTexPath);
					if (isHDRP) // HDRP
					{
						mat.SetInt("_DiffusionProfile", 2);
						mat.SetTexture("_SubsurfaceMaskMap", tex);
						mat.SetFloat("_TransmissionMask", 1.0f);
						mat.SetFloat("_Thickness", 0.2f);
						
						if (!textures.ContainsKey("transmission"))
						{
							mat.SetTexture("_TransmissionMaskMap", tex);
							mat.SetTexture("_ThicknessMap", tex);
							mat.SetVector("_ThicknessRemap", new Vector4(0f, 0.2f, 0f, 0f));
						}

						if (isPlant)
						{
							//mat.SetInt("_DiffusionProfile", 2);
							mat.SetFloat("_CoatMask", 0.0f);
							mat.SetInt("_EnableWind", 1); // Doesn't exist in any shader file
						}
						MegascansMaterialUtils.AddSSSSettings(mat);
					}
				}
				else if (channel == "transmission")
				{
					Texture2D tex = ImportTexture(texture_file, destTexPath, false, false);
					if (isHDRP) // HDRP
					{
						mat.SetInt("_DiffusionProfile", 2);
						mat.SetFloat("_SubsurfaceMask", 1.0f);
						mat.SetTexture("_TransmissionMaskMap", tex);
						mat.SetFloat("_TransmissionMask", 1.0f);
						mat.SetTexture("_ThicknessMap", tex);
						mat.SetVector("_ThicknessRemap", new Vector4(0f, 0.2f, 0f, 0f));
						
						if (!textures.ContainsKey("translucency"))
						{
							mat.SetTexture("_SubsurfaceMaskMap", tex);
						}
						MegascansMaterialUtils.AddSSSSettings(mat);
					}
				}
				else if (channel == "opacity")
				{
					mat.EnableKeyword("_ALPHATEST_ON");
					if(isHDRP)
					{
						mat.SetInt("_AlphaCutoffEnable", 1);
						mat.SetOverrideTag("RenderType", "TransparentCutout");
						mat.EnableKeyword("_DOUBLESIDED_ON");
						mat.DisableKeyword("_BLENDMODE_ALPHA"); // Doesn't exist in any shader file
						mat.renderQueue = 2450;
					}
					else if(isStandard) mat.SetFloat("_Mode", 1);
					else if(isURP) mat.SetFloat("_AlphaClip", 1);
				}
				else if ((channel == "occlusion") && !isHDRP)
				{
					Texture2D tex = ImportTexture(texture_file, destTexPath, false, false);
					mat.SetTexture("_OcclusionMap", tex);
				}
				else if (channel == "specular")
				{
					// if (texPack == 1)
					// {
					//     ImportTexture(texture_file, destTexPath);
					//     tex = texPrcsr.ImportTexture();
						
					// 	if (isStandard)
					// 	{
					// 		mat.SetTexture("_SpecGlossMap", tex); 
					// 		mat.SetColor("_SpecColor", new UnityEngine.Color(1.0f, 1.0f, 1.0f));
					// 	}
						
					// 	if (isURP)
					// 	{
					// 		mat.SetTexture("_SpecGlossMap", tex);
					// 		mat.SetColor("_SpecColor", new UnityEngine.Color(1.0f, 1.0f, 1.0f));	
					// 	}
						
					// 	if (isHDRP) // HDRP
					// 	{
					// 		mat.SetTexture("_SpecularColorMap", tex);
					// 		mat.SetColor("_SpecularColor", new UnityEngine.Color(1.0f, 1.0f, 1.0f));
					// 	}
					// }
				}
			}

			// If Translucency or Transmission
			if (textures.ContainsKey("translucency") || textures.ContainsKey("transmission"))
			{
				if(isHDRP)
				{
					mat.EnableKeyword("_SUBSURFACE_MASK_MAP");
					mat.SetFloat("_EnableSubsurfaceScattering", 1); // Doesn't exist in any shader file
					mat.SetFloat("_SurfaceType", 0.0f);
					mat.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
					mat.SetInt("_MaterialID", 0);
					mat.EnableKeyword("_MATERIAL_FEATURE_SUBSURFACE_SCATTERING");
					mat.EnableKeyword("_MATERIAL_FEATURE_TRANSMISSION");
					if (isPlant) { mat.EnableKeyword("_VERTEX_WIND"); } // Doesn't exist in any shader file
					// mat.SetOverrideTag("RenderType", "Transparent");
					// mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
				}
				else{
					mat.SetOverrideTag("RenderType", "Transparent");
					if (shaderName == "Universal Render Pipeline/Lit") mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
				}
			}

			int workflow = 0; // Force a metallic workflow
			if (mat.shader == Shader.Find("Standard"))
			{
				if (textures.ContainsKey("masks") && workflow == 0)
					mat.EnableKeyword("_METALLICGLOSSMAP");
			}
			else if (mat.shader == Shader.Find("Universal Render Pipeline/Lit"))
			{
				if (workflow == 1)
				{
					if (textures.ContainsKey("specular"))
					{
						mat.SetFloat("_WorkflowMode", 0);
						mat.EnableKeyword("_METALLICSPECGLOSSMAP");
						mat.EnableKeyword("_SPECGLOSSMAP");
						mat.EnableKeyword("_SPECULAR_SETUP");
					}
					if (textures.ContainsKey("occlusion"))
					{
						mat.EnableKeyword("_OCCLUSIONMAP");
					}
				}
				else if (workflow == 0)
				{
					if (textures.ContainsKey("masks"))
					{
						mat.EnableKeyword("_METALLICSPECGLOSSMAP");
						if (textures.ContainsKey("occlusion")) { mat.EnableKeyword("_OCCLUSIONMAP"); }
					}
				}
			}
			else if (mat.shader == Shader.Find("HDRP/Lit") || mat.shader == Shader.Find("HDRP/LitTessellation"))
			{
				if (workflow == 1)
				{
					if (textures.ContainsKey("specular"))
					{
						mat.SetFloat("_MaterialID", 4);
						mat.EnableKeyword("_SPECULARCOLORMAP");
						mat.EnableKeyword("_MATERIAL_FEATURE_SPECULAR_COLOR");
					}
				}
				if (workflow == 0)
				{
					if (textures.ContainsKey("metal") || textures.ContainsKey("roughness") || textures.ContainsKey("glossiness") || textures.ContainsKey("occlusion"))
					{
						mat.EnableKeyword("_MASKMAP");
						mat.SetFloat("_MaterialID", 1);
					}
				}
			}
			return mat;
		}

		
		// 		public static void AddDiffusionProfile()
		// 		{
		// #if UNITY_PIPELINE_HDRP

		// 			// CHECKING FOR THE PROFILE ASSET FILE
					
		// 			string ProfilePath = "Assets/Fab_Plugin/DiffusionProfiles/QuixelDiffusionProfile01.asset";
		// 			var ProfileObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ProfilePath);

		// 			if (!ProfileObject)
		// 			{

		// 				ProfileObject = ScriptableObject.CreateInstance<DiffusionProfileSettings>();
		// 				ProfileObject.name = "QuixelDiffusionProfile01";

		// 				if (!AssetDatabase.IsValidFolder("Assets/Fab_Plugin/DiffusionProfiles"))
		// 				{
		// 					AssetDatabase.CreateFolder("Assets/Fab_Plugin", "DiffusionProfiles");
		// 				}

		// 				AssetDatabase.CreateAsset(ProfileObject, ProfilePath);
		// 				AssetDatabase.SaveAssets();
		// 				AssetDatabase.Refresh();

		// 				ProfileObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ProfilePath);
		// 			}



		// 			// CHECKING FOR THE PROFILE ASSET PARAMETERS

		// 			SerializedObject serializedProfile = new SerializedObject(ProfileObject);
		// 			SerializedProperty scatteringDistanceProp = serializedProfile.FindProperty("profile.scatteringDistance");
		// 			SerializedProperty transmissionTintProp = serializedProfile.FindProperty("profile.transmissionTint");

		// 			Color checkColor = new Color(1.0f, 1.0f, 1.0f);

		// 			if (scatteringDistanceProp.colorValue != checkColor || transmissionTintProp.colorValue != checkColor)
		// 			{
		// 				scatteringDistanceProp.colorValue = checkColor;
		// 				transmissionTintProp.colorValue = checkColor;

		// 				serializedProfile.ApplyModifiedProperties();
		// 				AssetDatabase.SaveAssets();
		// 				AssetDatabase.Refresh();
		// 			}


		// 			// CHECKING IF THE PROFILE ASSET FILE IS ALREADY LOADED IN THE DIFFUSION PROFILE LIST

		// 			// var hdrpSettings = GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>() as HDRenderPipelineGlobalSettings; -- HDRenderPipelineGlobalSettings is protected.
		// 			var hdrpGlobalSettings = GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>() as ScriptableObject;
		// 			Debug.Log($"RenderPipelineGlobalSettings -->\nObject --> {hdrpGlobalSettings}\nType --> {hdrpGlobalSettings.GetType()}\n");

		// 			//var props = hdrpGlobalSettings.GetType().GetProperties(); -- this won't give you the non-public properties
		// 			var props = hdrpGlobalSettings.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		// 			Debug.Log($"HDRPGlobalSettings Properties -->\nObject --> {props}\n");

		// 			// In case, you wish to check all properties of HDRPGlobalSettings. Amongst them, you'll find the (diffusionProfileSettingsList) as well.

		// 			int itrA = 1;
		// 			foreach (var prop in props)
		// 			{
		// 				Debug.Log($"HDRPGlobalSettings Property {itrA} -->\nProperty --> {prop}\nType --> {prop.GetType()}\nPropertyType --> {prop.PropertyType}\nPropertyTypeName --> {prop.PropertyType.Name}\nName --> {prop.Name}\nValue --> {prop.GetValue(hdrpGlobalSettings)}\n");
		// 				itrA++;
		// 			}

		// 			// Todo: this crashes
		// 			bool chkDProfile = false;
		// 			var DProfileListProp = hdrpGlobalSettings.GetType().GetProperty("diffusionProfileSettingsList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		// 			var DProfileListObject = DProfileListProp.GetValue(hdrpGlobalSettings);
		// 			var DProfileListArray = DProfileListObject as DiffusionProfileSettings[];
		// 			List<DiffusionProfileSettings> DProfileList = new List<DiffusionProfileSettings>(DProfileListArray);

		// 			for (int i = 0; i < DProfileList.Count; i++)
		// 			{
		// 				if (DProfileList[i] == null)
		// 				{
		// 					Debug.Log($"Diffusion Profile {i+1} -->\nProfile --> {DProfileList[i]}\nProfileName --> This Profile is Missing.\n");
		// 					continue;
		// 				}
		// 				else
		// 				{
		// 					Debug.Log($"Diffusion Profile {i+1} -->\nProfile --> {DProfileList[i]}\nProfileName --> {DProfileList[i].name}\n");
		// 					if (DProfileList[i].name == "QuixelDiffusionProfile01"){chkDProfile = true;}
		// 				}

		// 			}


		// 			// ADDING THE PROFILE ASSET FILE IN THE DIFFUSION PROFILE LIST

		// 			DiffusionProfileSettings profile = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(ProfilePath);

		// 			//if (chkDProfile == true){return;}
		// 			if (chkDProfile == false)
		// 			{
		// 				DProfileList.Add(profile);
		// 				DProfileListProp.SetValue(hdrpGlobalSettings, DProfileList.ToArray());
		// 				EditorUtility.SetDirty(hdrpGlobalSettings);
		// 				AssetDatabase.SaveAssetIfDirty(hdrpGlobalSettings);
		// 				AssetDatabase.SaveAssets();
		// 				AssetDatabase.Refresh();
		// 			}


		// 			// CHECKING IF THE PROFILE ASSET FILE IS ALREADY LOADED IN THE SCENE VOLUME PROFILE
		// 			// This is work in progress. 
		// 			// We either need to add the Diffusion Profile in the HDRP Global Settings' Default Volume Profile OR in the Scene Volume Profile. 

		// 			/*********************************************************************************************************************************
					
		// 			// Code being developed for checking Diffusion Profile in the HDRP Global Settings' Default Volume Profile. Later we'll add the code for inserting it. 
		// 			var volProfileProp = hdrpGlobalSettings.GetType().GetProperty("volumeProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		// 			Debug.Log($"Volume Profile Property --> {volProfileProp}\n");
					
		// 			var volProfileObject = volProfileProp.GetValue(hdrpGlobalSettings);
					
		// 			// Code completely developed for checking and inserting (if needed) the Diffusion Profile in the Scene Volume Profile.
					
					
		// 			//Volume[] volumes = UnityEngine.Object.FindObjectsOfType<Volume>(true);
		// 			Volume[] volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		// 			//Debug.Log($"VOLUMES FOUND --> {volumes}\n");
					
		// 			int itr = 0;
		// 			foreach (var volume in volumes)
		// 			{
		// 				Debug.Log($"Volume {itr+1} -->\nObject --> {volume}\nName --> {volume.name}\nSharedProfile --> {volume.sharedProfile}\nWhether Global --> {volume.isGlobal}\n");
		// 				if (volume.isGlobal == true){break;}
		// 				itr++;
		// 			}
					
					
		// 			if (!volumes[itr].sharedProfile.TryGet<DiffusionProfileList>(out var VolumeDProfileList))
		// 			{
		// 				Debug.Log($"Scene Volume Profile Does Not Have DiffusionProfileList.\n");
		// 				VolumeDProfileList = volumes[itr].sharedProfile.Add<DiffusionProfileList>();
		// 				Debug.Log($"What's been added is --> {VolumeDProfileList}\n");
						
		// 				DiffusionProfileSettings[] NewProfilesArray = new DiffusionProfileSettings[0];
		// 				List<DiffusionProfileSettings> NewProfilesList = new List<DiffusionProfileSettings>(NewProfilesArray);
						
		// 				//DiffusionProfileSettings profile = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(ProfilePath);
		// 				NewProfilesList.Add(profile);
		// 				VolumeDProfileList.diffusionProfiles.Override(NewProfilesList.ToArray());
		// 				EditorUtility.SetDirty(volumes[itr].sharedProfile);
		// 				EditorUtility.SetDirty(volumes[itr]);
		// 				AssetDatabase.SaveAssets();
		// 				AssetDatabase.Refresh();
		// 			}
		// 			else
		// 			{
		// 				Debug.Log($"Scene Volume Profile Already has DiffusionProfileList -->\nVolume's Profile List --> {VolumeDProfileList}\nProfiles in that List --> {VolumeDProfileList.diffusionProfiles}\nValue --> {VolumeDProfileList.diffusionProfiles.value}\nNo. of Profiles in that List --> {VolumeDProfileList.diffusionProfiles.value.Length}\n");
		// 			}
					
					
		// 			// For Finding All (active & non-active) volume profiles.
					
		// 			string[] guids = AssetDatabase.FindAssets("t:VolumeProfile");
					
		// 			itr = 1; 
		// 			foreach (string guid in guids)
		// 			{
		// 				string path = AssetDatabase.GUIDToAssetPath(guid);
		// 				VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
		// 				Debug.Log($"VolumeProfile {itr} -->\nGUID --> {guid}\nPath --> {path}\nProfile --> {profile}\nProfile Name --> {profile.name}\nWhether Diffusion Profile List --> {profile.TryGet<DiffusionProfileList>(out var diffusionList)}\n");
		// 				itr++;
		// 			}
					
		// 			*********************************************************************************************************************************/

		// #endif
		// 		}
		

		public static void AddSSSSettings(Material mat)
		{

			// Setting up specific SSS Settings.

			mat.SetFloat("_StencilRef", 4f);
			mat.SetFloat("_ReceivesSSR", 1f);
			mat.SetFloat("_ReceivesSSRTransparent", 1f);
			mat.SetFloat("_StencilRefGBuffer", 14f); // - (Ajwad had written here --> Check with plants)

			// Inserting Diffusion Profile
			// #if UNITY_PIPELINE_HDRP
			// 			DiffusionProfileSettings DProfile = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>("Assets/Fab_Plugin/DiffusionProfiles/QuixelDiffusionProfile01.asset");
			// 			HDMaterial.SetDiffusionProfile(mat, DProfile);
			// #endif
			EditorUtility.SetDirty(mat);

			// See after plugging in volume code, if you need to put delay at this point. 
			// UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat));
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			//UnityEngine.Object DProfileObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Fab_Plugin/DiffusionProfiles/QuixelDiffusionProfile01.asset");
			//SerializedObject serializedProfile = new SerializedObject(DProfileObject);

			//SerializedProperty hashProp = serializedProfile.FindProperty("profile.hash");
			//Debug.Log($"HASHPROP is --> {hashProp}\n");
			//uint intHash = (uint)hashProp.intValue;
			//Debug.Log($"INTHASH is --> {intHash}\n");
			//float floatHash = BitConverter.ToSingle(BitConverter.GetBytes(intHash), 0);
			//Debug.Log($"FLOATHASH is --> {floatHash}\n");
			//mat.SetFloat("_DiffusionProfileHash", floatHash);


		}

		public static Texture2D ImportTexture(string sourcePath, string destPath, bool normalMap = false, bool sRGB = true, bool readable = false, bool flipGreenChannel = false)
        {
            FabUtilities.CopyFileToProject(sourcePath, destPath);
            TextureImporter tImp = AssetImporter.GetAtPath(destPath) as TextureImporter;
            tImp.sRGBTexture = sRGB;
            tImp.isReadable = readable;
            tImp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if(normalMap && flipGreenChannel) tImp.flipGreenChannel = true;
            AssetDatabase.ImportAsset(destPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(destPath);
        }
	}
}
#endif
