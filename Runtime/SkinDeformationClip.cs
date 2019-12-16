using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Unity.DemoTeam.Attributes;

namespace Unity.DemoTeam.DigitalHuman
{
	[CreateAssetMenu(menuName = "Digital Human/Skin Deformation Clip")]
	public class SkinDeformationClip : ScriptableObject
	{
		public unsafe struct Frame
		{
			public const int FLT_STRIDE = 9;
			public const int FLT_OFFSET_POSITION = 0;
			public const int FLT_OFFSET_TANGENT = 3;
			public const int FLT_OFFSET_NORMAL = 6;
			public float* deltaPosTanNrm;
			//TODO don't write the data interleaved if we're not going to use it as such!
			//public float* deltaPositions;
			//public float* deltaTangents;
			//public float* deltaNormals;
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
		public string frameDataFilename = null;

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

				LoadFrameData(frameDataFilename);
			}

			var floatPtr = (float*)frameData.ReadFrame(frameIndex);
			{
				Frame frame;
				//frame.deltaPositions = floatPtr + 0 * frameVertexCount;
				//frame.deltaTangents = floatPtr + 3 * frameVertexCount;
				//frame.deltaNormals = floatPtr + 6 * frameVertexCount;
				frame.deltaPosTanNrm = floatPtr + 0 * frameVertexCount;
				frame.fittedWeights = floatPtr + 9 * frameVertexCount;
				frame.albedo = frames[frameIndex].albedo;
				return frame;
			}
		}

		public int GetFrameSizeBytes()
		{
			return (9 * frameVertexCount + frameFittedWeightsCount) * sizeof(float);
		}

		public void PrepareFrame(int frameIndex)
		{
			frameData.SeekFrame(frameIndex);
		}

		//--- import settings begin ---
		public enum TransferMode
		{
			PassThrough,
			PassThroughWithFirstFrameDelta,
		}

		[Serializable]
		public class ImportSettings
		{
			[Header("Asset paths")]
			public string keyframesCSV;
			[EditableIf("externalLoader", false)] public string meshFolder;
			[EditableIf("externalLoader", false)] public string meshPrefix;
			[EditableIf("externalLoader", false)] public string albedoFolder;
			[EditableIf("externalLoader", false)] public string albedoPrefix;
			public bool externalLoader = false;
			[EditableIf("externalLoader", true)] public string externalObjPath;
			[EditableIf("externalLoader", true)] public string externalObjPattern = "*.obj";

			[Header("Mesh transform")]
			public Vector3 applyRotation = Vector3.zero;
			public float applyScale = 1.0f;

			[Header("Mesh processing")]
			[Range(0.0f, 1.0f)]
			public float denoiseFactor = 0.0f;
			public TextAsset[] denoiseRegion;
			[Range(0.0f, 1.0f)]
			public float transplantFactor = 0.0f;
			public TextAsset[] transplantRegion;
			public bool solveRegionPreview = false;
			public bool solveWelded = false;

			[Header("Frame transfer")]
			//PACKAGETODO cleanup
			public Mesh transferTarget;
			public TextAsset transferRegion;
			public TransferMode transferMode;

			[Header("Frame fitting")]
			public bool fitToBlendShapes = false;
			[TextArea(1, 20)]
			public string fittedIndices = "";
			public SkinDeformationFitting.Method fittingMethod = SkinDeformationFitting.Method.LinearLeastSquares;
			public SkinDeformationFitting.Param fittingParam = SkinDeformationFitting.Param.DeltaPosition;

			public ImportSettings Clone()
			{
				var c = this.MemberwiseClone() as ImportSettings;
				c.keyframesCSV = c.keyframesCSV.Clone() as string;
				c.meshFolder = c.meshFolder.Clone() as string;
				c.meshPrefix = c.meshPrefix.Clone() as string;
				c.albedoFolder = c.albedoFolder.Clone() as string;
				c.albedoPrefix = c.albedoPrefix.Clone() as string;
				c.denoiseRegion = c.denoiseRegion.Clone() as TextAsset[];
				c.transplantRegion = c.transplantRegion.Clone() as TextAsset[];
				c.fittedIndices = c.fittedIndices.Clone() as string;
				return c;
			}
		}

		[ReadOnly]
		public ImportSettings lastBuild = new ImportSettings();
		public ImportSettings importSettings = new ImportSettings();
		//--- import settings end ---

		//--- frame data serialization begin ---
		void OnEnable()
		{
			if (frameDataPending)
			{
				LoadFrameData(frameDataFilename);
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

		void LoadFrameData(string filename)
		{
#if !UNITY_EDITOR
			filename = Application.streamingAssetsPath + Regex.Replace(frameDataFilename, "^Assets", "");
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

			/* OLD
			for (int i = 0; i != frameCount; i++)
			{
				frames[i].Allocate(frameVertexCount, frameFittedWeightsCount);
			}

			using (FileStream stream = File.OpenRead(filename))
			{
				using (BinaryReader reader = new BinaryReader(stream))
				{
					int __frameCount = reader.ReadInt32();
					int __frameVertexCount = reader.ReadInt32();
					int __frameFittedWeightsCount = reader.ReadInt32();

					Debug.Assert(__frameCount == frameCount);
					Debug.Assert(__frameVertexCount == frameVertexCount);
					Debug.Assert(__frameFittedWeightsCount == frameFittedWeightsCount);

					var srcCursor = 0;
					var srcBuffer = new float[3 * 3 * frameVertexCount + 1 * frameFittedWeightsCount];
					var dstBuffer = new byte[4 * srcBuffer.Length];

					for (int i = 0; i != frameCount; i++)
					{
						reader.Read(dstBuffer, 0, dstBuffer.Length);

						Buffer.BlockCopy(dstBuffer, 0, srcBuffer, 0, dstBuffer.Length);

						for (int j = 0; j != frameVertexCount; j++)
						{
							frames[i].deltaPositions[j].x = srcBuffer[srcCursor++];
							frames[i].deltaPositions[j].y = srcBuffer[srcCursor++];
							frames[i].deltaPositions[j].z = srcBuffer[srcCursor++];

							frames[i].deltaTangents[j].x = srcBuffer[srcCursor++];
							frames[i].deltaTangents[j].y = srcBuffer[srcCursor++];
							frames[i].deltaTangents[j].z = srcBuffer[srcCursor++];

							frames[i].deltaNormals[j].x = srcBuffer[srcCursor++];
							frames[i].deltaNormals[j].y = srcBuffer[srcCursor++];
							frames[i].deltaNormals[j].z = srcBuffer[srcCursor++];
						}

						for (int j = 0; j != frameFittedWeightsCount; j++)
						{
							frames[i].fittedWeights[j] = srcBuffer[srcCursor++];
						}

						srcCursor = 0;
					}

					frameDataPending = false;
				}
			}
			*/
		}

		public void UnloadFrameData()
		{
			frameData.Dispose();
			frameDataPending = true;
		}

		public void SaveFrameData(string filename)
		{
			UnloadFrameData();

			if (File.Exists(filename))
				File.Delete(filename);

			//if (filename != frameDataFilename)
			//{
			//    if (File.Exists(frameDataFilename))
			//        File.Delete(frameDataFilename);
			//    if (File.Exists(frameDataFilename + ".meta"))
			//        File.Delete(frameDataFilename + ".meta");
			//}

			using (FileStream stream = File.Create(filename))
			{
				using (BinaryWriter writer = new BinaryWriter(stream))
				{
					writer.Write(frameCount);
					writer.Write(frameVertexCount);
					writer.Write(frameFittedWeightsCount);

					var srcCursor = 0;
					var srcBuffer = new float[3 * 3 * frameVertexCount + 1 * frameFittedWeightsCount];
					var dstBuffer = new byte[4 * srcBuffer.Length];

					for (int i = 0; i != frameCount; i++)
					{
						srcCursor = 0;

						Debug.Assert(frames[i].deltaPositions.Length == frameVertexCount, "invalid vertex count");
						for (int j = 0; j != frameVertexCount; j++)
						{
							srcBuffer[srcCursor++] = frames[i].deltaPositions[j].x;
							srcBuffer[srcCursor++] = frames[i].deltaPositions[j].y;
							srcBuffer[srcCursor++] = frames[i].deltaPositions[j].z;

							srcBuffer[srcCursor++] = frames[i].deltaTangents[j].x;
							srcBuffer[srcCursor++] = frames[i].deltaTangents[j].y;
							srcBuffer[srcCursor++] = frames[i].deltaTangents[j].z;

							srcBuffer[srcCursor++] = frames[i].deltaNormals[j].x;
							srcBuffer[srcCursor++] = frames[i].deltaNormals[j].y;
							srcBuffer[srcCursor++] = frames[i].deltaNormals[j].z;
						}

						Debug.Assert(frames[i].fittedWeights.Length == frameFittedWeightsCount, "invalid fitted weights count");
						for (int j = 0; j != frameFittedWeightsCount; j++)
						{
							srcBuffer[srcCursor++] = frames[i].fittedWeights[j];
						}

						Buffer.BlockCopy(srcBuffer, 0, dstBuffer, 0, dstBuffer.Length);

						writer.Write(dstBuffer, 0, dstBuffer.Length);
						writer.Flush();
					}

					frameDataFilename = filename;
					frameDataPending = true;
				}
			}
		}
		//--- frame data serialization end ---

		[ContextMenu("Copy To StreamingAssets")]
		public void CopyToStreamingAssets()
		{
			Debug.Log("staging framedata for " + name);
			var pathRel = Regex.Replace(frameDataFilename, "^Assets", "");
			var copySrc = Application.dataPath + pathRel;
			var copyDst = Application.streamingAssetsPath + pathRel;
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
	}

#if UNITY_EDITOR
	public class SkinDeformationClipBuildProcessor : UnityEditor.Build.IPreprocessBuildWithReport
	{
		public int callbackOrder { get { return 0; } }
		public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
		{
			var clips = Resources.FindObjectsOfTypeAll<SkinDeformationClip>();
			foreach (var clip in clips)
			{
				clip.CopyToStreamingAssets();
			}
		}
	}
#endif
}