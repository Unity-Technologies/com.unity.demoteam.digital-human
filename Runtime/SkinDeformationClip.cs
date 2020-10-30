using System;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;
using Unity.DemoTeam.Attributes;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DemoTeam.DigitalHuman
{
	[CreateAssetMenu(menuName = "Digital Human/Skin Deformation Clip")]
	public class SkinDeformationClip : ScriptableObject
	{
		public unsafe struct Frame
		{
			public float* deltaPositions;
			public float* deltaNormals;
			public float* fittedWeights;
			public Texture2D albedo;
		}

		[Serializable]
		public struct Subframe
		{
			public int frameIndexLo;
			public int frameIndexHi;
			public float fractionLo;
			public float fractionHi;
		}

		[HideInInspector]
		public int frameCount = 0;

		[HideInInspector]
		public float frameRate = 30.0f;

		[HideInInspector]
		public int frameVertexCount = 0;

		[HideInInspector]
		public int frameFittedWeightsCount = 0;

		[HideInInspector]
		public SkinDeformation[] frames = new SkinDeformation[0];

		[HideInInspector]
		public bool framesContainAlbedo;

		[HideInInspector]
		public bool framesContainDeltas;

		[HideInInspector]
		public bool framesContainFittedWeights;

		[HideInInspector]
		public NativeFrameStream frameData;

		[HideInInspector]
		public string frameDataStreamingAssetsPath = null;

		[HideInInspector]
		private bool frameDataPending = true;

		[HideInInspector]
		public int subframeCount = 0;

		[HideInInspector]
		public Subframe[] subframes = new Subframe[0];

		[HideInInspector]
		public int version = -1;

		//--- accessors ---
		public double Duration
		{
			get
			{
				return subframeCount / frameRate;
			}
		}

		public unsafe Frame GetFrame(int frameIndex)
		{
			if (frameDataPending)
			{
				Debug.Log("hotloading frame data");
				LoadFrameData();
			}

			var floatPtr = (float*)frameData.ReadFrame(frameIndex);
			{
				Frame frame;
				frame.deltaPositions = floatPtr + 0 * frameVertexCount;
				frame.deltaNormals = floatPtr + 3 * frameVertexCount;
				frame.fittedWeights = floatPtr + 6 * frameVertexCount;
				frame.albedo = frames[frameIndex].albedo;
				return frame;
			}
		}

		public int GetFrameSizeBytes()
		{
			return (6 * frameVertexCount + 1 * frameFittedWeightsCount) * sizeof(float);
		}

		public void PrepareFrame(int frameIndex)
		{
			frameData.SeekFrame(frameIndex);
		}

		//--- import settings begin ---
		public enum SourceType
		{
			ExternalObj,
			ProjectAssets,
		}

		[Serializable]
		public class ImportSettings
		{
			[Header("Source sequence")]
			[FormerlySerializedAs("readFrom")] public SourceType sourceFrom = SourceType.ExternalObj;
			[FormerlySerializedAs("externalObjPath")]
			[VisibleIf("sourceFrom", SourceType.ExternalObj)] public string externalObjPath;
			[FormerlySerializedAs("externalObjPattern")]
			[VisibleIf("sourceFrom", SourceType.ExternalObj)] public string externalObjPattern = "*.obj";
			[VisibleIf("sourceFrom", SourceType.ExternalObj)] public bool externalObjPreloadThreaded = true;
			[VisibleIf("sourceFrom", SourceType.ProjectAssets)] public string meshAssetFolder;
			[VisibleIf("sourceFrom", SourceType.ProjectAssets)] public string meshAssetPrefix;
			[VisibleIf("sourceFrom", SourceType.ProjectAssets)] public string albedoAssetFolder;
			[VisibleIf("sourceFrom", SourceType.ProjectAssets)] public string albedoAssetPrefix;

			[Space]
			[Tooltip("Enable this to treat the imported frames as keyframes")]
			public bool keyframes = false;
			[Tooltip("CSV specifying how the frames are distributed. The first column is ignored. Rows read as follows:\nRow 1: Frame indices\nRow 2: Keyframe indices\nRow 3: Frame progress (0-100) between keys")]
			[EditableIf("keyframes", true)]
			public TextAsset keyframesCSV;

			[Header("Source reference")]
			public bool referenceIsFirstFrame;
			[VisibleIf("sourceFrom", SourceType.ExternalObj)] public string referenceObjPath;
			[VisibleIf("sourceFrom", SourceType.ProjectAssets)] public Mesh referenceMeshAsset;

			[Header("Frame transform")]
			public Vector3 applyRotation = Vector3.zero;
			public float applyScaling = 0.01f;

			[Header("Frame processing")]
			[Tooltip("Regions are specified in text files. Each file should contain an array of vertex indices on the form: [i, j, k, ...]")]
			public TextAsset[] denoiseRegions;
			[Range(0.0f, 1.0f)]
			public float denoiseStrength = 0.0f;
			[Tooltip("Regions are specified in text files. Each file should contain an array of vertex indices on the form: [i, j, k, ...]")]
			public TextAsset[] transplantRegions;
			[Range(0.0f, 1.0f)]
			public float transplantStrength = 0.0f;
			public bool solveRegionPreview = false;
			public bool solveWelded = true;

			[Header("Frame transfer")]
			public TransferMode transferMode = TransferMode.ByVertexIndex;
			public Mesh transferTarget;
			public enum TransferMode
			{
				ByVertexIndex,
				ByVertexPosition,
			}

			[Header("Frame fitting")]
			public bool fitToBlendShapes = false;
			[TextArea(1, 20)]
			public string fittedIndices = "";
			public SkinDeformationFittingOptions.Method fittingMethod = SkinDeformationFittingOptions.Method.LinearLeastSquares;
			public SkinDeformationFittingOptions.Param fittingParam = SkinDeformationFittingOptions.Param.DeltaPosition;

			public ImportSettings Clone()
			{
				var c = this.MemberwiseClone() as ImportSettings;
				c.externalObjPath = c.externalObjPath.Clone() as string;
				c.externalObjPattern = c.externalObjPattern.Clone() as string;
				c.meshAssetFolder = c.meshAssetFolder.Clone() as string;
				c.meshAssetPrefix = c.meshAssetPrefix.Clone() as string;
				c.albedoAssetFolder = c.albedoAssetFolder.Clone() as string;
				c.albedoAssetPrefix = c.albedoAssetPrefix.Clone() as string;
				c.referenceObjPath = c.referenceObjPath.Clone() as string;
				c.denoiseRegions = c.denoiseRegions.Clone() as TextAsset[];
				c.transplantRegions = c.transplantRegions.Clone() as TextAsset[];
				c.fittedIndices = c.fittedIndices.Clone() as string;
				return c;
			}
		}

		[ReadOnly]
		[FormerlySerializedAs("lastImport")]
		public ImportSettings settingsLastImported = new ImportSettings();
		[FormerlySerializedAs("importSettings")]
		public ImportSettings settings = new ImportSettings();
		//--- import settings end ---

		//--- frame data serialization begin ---
		void OnEnable()
		{
			if (frameDataPending)
			{
				LoadFrameData();
			}
		}

		void OnDisable()
		{
			UnloadFrameData();
		}

		void OnDestroy()
		{
			UnloadFrameData();
		}

		void LoadFrameData()
		{
#if UNITY_EDITOR
			string filename = AssetDatabase.GetAssetPath(this) + "_frames.bin";
#else
			string filename = Application.streamingAssetsPath + frameDataStreamingAssetsPath;
			Debug.Log("LoadFrameData " + filename + ")");
#endif

			int frameOffset = 3 * sizeof(Int32);
			int frameSize = GetFrameSizeBytes();

			frameData.Dispose();
			frameData = new NativeFrameStream(filename, frameOffset, frameCount, frameSize, 2, 16);
			frameDataPending = false;

			if (!File.Exists(filename))
			{
				Debug.LogError("failed to load frame data (filename = " + filename + ")");
				return;
			}
		}

		public void UnloadFrameData()
		{
			frameData.Dispose();
			frameDataPending = true;
		}

#if UNITY_EDITOR
		public void SaveFrameData()
		{
			string filenameAsset = AssetDatabase.GetAssetPath(this);
			string filenameFrameData = filenameAsset + "_frames.bin";

			UnloadFrameData();

			if (File.Exists(filenameFrameData))
				File.Delete(filenameFrameData);

			using (FileStream stream = File.Create(filenameFrameData))
			{
				using (BinaryWriter writer = new BinaryWriter(stream))
				{
					writer.Write(frameCount);
					writer.Write(frameVertexCount);
					writer.Write(frameFittedWeightsCount);

					var fltCursor = 0;
					var fltBuffer = new float[6 * frameVertexCount + 1 * frameFittedWeightsCount];
					var dstBuffer = new byte[4 * fltBuffer.Length];

					for (int i = 0; i != frameCount; i++)
					{
						Debug.Assert(frames[i].deltaPositions.Length == frameVertexCount, "invalid vertex count");
						Debug.Assert(frames[i].fittedWeights.Length == frameFittedWeightsCount, "invalid fitted weights count");

						fltCursor = 0;

						// write positions
						for (int j = 0; j != frameVertexCount; j++)
						{
							fltBuffer[fltCursor++] = frames[i].deltaPositions[j].x;
							fltBuffer[fltCursor++] = frames[i].deltaPositions[j].y;
							fltBuffer[fltCursor++] = frames[i].deltaPositions[j].z;
						}

						// write normals
						for (int j = 0; j != frameVertexCount; j++)
						{
							fltBuffer[fltCursor++] = frames[i].deltaNormals[j].x;
							fltBuffer[fltCursor++] = frames[i].deltaNormals[j].y;
							fltBuffer[fltCursor++] = frames[i].deltaNormals[j].z;
						}

						// write fitted weights
						for (int j = 0; j != frameFittedWeightsCount; j++)
						{
							fltBuffer[fltCursor++] = frames[i].fittedWeights[j];
						}

						Buffer.BlockCopy(fltBuffer, 0, dstBuffer, 0, dstBuffer.Length);

						writer.Write(dstBuffer, 0, dstBuffer.Length);
						writer.Flush();
					}

					frameDataPending = true;
				}
			}
		}

		[ContextMenu("Copy To StreamingAssets")]
		public void CopyToStreamingAssets()
		{
			string filenameAsset = AssetDatabase.GetAssetPath(this);
			string filenameFrameData = filenameAsset + "_frames.bin";

			frameDataStreamingAssetsPath = "/SkinDeformationClip/" + AssetDatabase.AssetPathToGUID(filenameAsset) + "__" + this.name;

			var copySrc = filenameFrameData;
			var copyDst = Application.streamingAssetsPath + frameDataStreamingAssetsPath;

			//Debug.Log("filenameAsset: " + filenameAsset);
			//Debug.Log("copySrc: " + copySrc);
			//Debug.Log("copyDst: " + copyDst);

			var copyDstDir = copyDst.Substring(0, copyDst.LastIndexOf('/'));
			try
			{
				if (File.Exists(copyDst))
					File.Delete(copyDst);

				Directory.CreateDirectory(copyDstDir);
				File.Copy(copySrc, copyDst);
			}
			catch (Exception ex)
			{
				Debug.LogError(ex.ToString());
			}
		}
#endif
		//--- frame data serialization end ---
	}
}