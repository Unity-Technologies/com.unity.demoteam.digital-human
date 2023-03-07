using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
	public static class MeshEx
	{
#if UNITY_2020_1_OR_NEWER
		const MeshUpdateFlags UPDATE_FLAGS_SILENT =
			MeshUpdateFlags.DontNotifyMeshUsers |
			MeshUpdateFlags.DontRecalculateBounds |
			MeshUpdateFlags.DontResetBoneBounds;
#endif

		public static void EnableSilentWrites(this Mesh mesh, bool enable)
		{
#if UNITY_2019_3_DEMOS_CAVE
			mesh.enableSilentWrites = enable;
#endif
		}

		public static void SilentlySetVertices(this Mesh mesh, Vector3[] positions)
		{
#if UNITY_2020_1_OR_NEWER
			mesh.SetVertices(positions, 0, positions.Length, UPDATE_FLAGS_SILENT);
#else
			mesh.EnableSilentWrites(true);
			mesh.SetVertices(positions, 0, positions.Length);
			mesh.EnableSilentWrites(false);
#endif
		}

		public static void SilentlySetNormals(this Mesh mesh, Vector3[] normals)
		{
#if UNITY_2020_1_OR_NEWER
			mesh.SetNormals(normals, 0, normals.Length, UPDATE_FLAGS_SILENT);
#else
			mesh.EnableSilentWrites(true);
			mesh.SetNormals(normals, 0, normals.Length);
			mesh.EnableSilentWrites(false);
#endif
		}
		
		public static void SilentlySetTangents(this Mesh mesh, Vector4[] tangents)
		{
#if UNITY_2020_1_OR_NEWER
			mesh.SetTangents(tangents, 0, tangents.Length, UPDATE_FLAGS_SILENT);
#else
			mesh.EnableSilentWrites(true);
			mesh.SetTangents(tangents, 0, tangents.Length);
			mesh.EnableSilentWrites(false);
#endif
		}

		public static void SilentlyRecalculateTangents(this Mesh mesh)
		{
#if UNITY_2020_1_OR_NEWER
			mesh.RecalculateTangents(UPDATE_FLAGS_SILENT);
#else
			mesh.EnableSilentWrites(true);
			mesh.RecalculateTangents();
			mesh.EnableSilentWrites(false);
#endif
		}

		public static void SilentlyRecalculateNormals(this Mesh mesh)
		{
#if UNITY_2020_1_OR_NEWER
			mesh.RecalculateNormals(UPDATE_FLAGS_SILENT);
#else
			mesh.EnableSilentWrites(true);
			mesh.RecalculateNormals();
			mesh.EnableSilentWrites(false);
#endif
		}

		public static void SilentlyRecalculateBounds(this Mesh mesh)
		{
#if UNITY_2020_1_OR_NEWER
			mesh.RecalculateBounds(UPDATE_FLAGS_SILENT);
#else
			mesh.EnableSilentWrites(true);
			mesh.RecalculateBounds();
			mesh.EnableSilentWrites(false);
#endif
		}

		static int getVertexFormatSizeInBytes(VertexAttributeFormat f)
		{
			switch (f)
			{
				case VertexAttributeFormat.Float32:
					return 4;
				case VertexAttributeFormat.Float16:
					return 2;
				case VertexAttributeFormat.UNorm8:
					return 1;
				case VertexAttributeFormat.SNorm8:
					return 1;
				case VertexAttributeFormat.UNorm16:
					return 2;
				case VertexAttributeFormat.SNorm16:
					return 2;
				case VertexAttributeFormat.UInt8:
					return 1;
				case VertexAttributeFormat.SInt8:
					return 1;
				case VertexAttributeFormat.UInt16:
					return 2;
				case VertexAttributeFormat.SInt16:
					return 2;
				case VertexAttributeFormat.UInt32:
					return 4;
				case VertexAttributeFormat.SInt32:
					return 4;
				default:
					return -1;
			}
		}
#if UNITY_2021_2_OR_NEWER
		//reorders vertex buffer layouts, so that explicit streams contain only attributes given in explicitAttributeStreams, everything else goes to defaultStreamIndex
		//Note that the relative order of attributes in the same stream is the order they appear in "GetVertexAttributes" list
		public static void ChangeVertexStreamLayout(this Mesh mesh, Tuple<VertexAttribute, int>[] explicitAttributeStreams, int defaultStreamIndex)
		{
			var originalMeshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            var newMeshDataArray = Mesh.AllocateWritableMeshData(1);

            var originalMeshData = originalMeshDataArray[0];
            var newMeshData = newMeshDataArray[0];
 
            //extract old layout
            VertexAttributeDescriptor[] originalAttributes = mesh.GetVertexAttributes();
            int[] originalAttributeOffsets = new int[originalAttributes.Length];
            int[] originalStrides = new int[originalMeshData.vertexBufferCount];
 
            for (int i = 0; i != originalAttributes.Length; ++i)
            {
                int stream = originalAttributes[i].stream;
                originalAttributeOffsets[i] += originalStrides[stream];
                originalStrides[stream] += originalAttributes[i].dimension *
                                           getVertexFormatSizeInBytes(originalAttributes[i].format);
            }

            //setup new layout
            Dictionary<VertexAttribute, int> attributeToStreamMapping = new Dictionary<VertexAttribute, int>(explicitAttributeStreams.Length);

            foreach (var attributeMapping in explicitAttributeStreams)
            {
	            attributeToStreamMapping[attributeMapping.Item1] = attributeMapping.Item2;
            }
            
             
            VertexAttributeDescriptor[] attributes = mesh.GetVertexAttributes();
            int[] attributeOffsets = new int[attributes.Length];
            int[] strides = {0, 0, 0, 0}; //Unity supports at max 4 streams
            
            for (int i = 0; i != attributes.Length; ++i)
            {
	            int streamIndex;
	            if (!attributeToStreamMapping.TryGetValue(attributes[i].attribute, out streamIndex))
	            {
		            streamIndex = defaultStreamIndex;
	            }

	            attributes[i].stream = streamIndex;

                int stream = attributes[i].stream;
                attributeOffsets[i] += strides[stream];
                strides[stream] += attributes[i].dimension * getVertexFormatSizeInBytes(attributes[i].format);
            }

            newMeshData.SetVertexBufferParams(originalMeshData.vertexCount, attributes);

            unsafe
            {
                for (int i = 0; i < originalAttributes.Length; ++i)
                {
                    int typeSizeInBytes = attributes[i].dimension * getVertexFormatSizeInBytes(attributes[i].format);
                    //copy attributes
                    int sourceStream = originalAttributes[i].stream;
                    NativeSlice<byte> originalData = originalMeshData.GetVertexData<byte>(sourceStream)
                        .Slice(originalAttributeOffsets[i]);

                    int targetStream = attributes[i].stream;
                    NativeSlice<byte> newData =
                        newMeshData.GetVertexData<byte>(targetStream).Slice(attributeOffsets[i]);


                    void* srcPtr = originalData.GetUnsafeReadOnlyPtr();
                    void* dstPtr = newData.GetUnsafePtr();

                    UnsafeUtility.MemCpyStride(dstPtr, strides[targetStream], srcPtr, originalStrides[sourceStream],
                        typeSizeInBytes, originalMeshData.vertexCount);
                }
            }

            if (originalMeshData.indexFormat == IndexFormat.UInt16)
            {
                var srcIndices = originalMeshData.GetIndexData<ushort>();
                newMeshData.SetIndexBufferParams(srcIndices.Length, IndexFormat.UInt16);

                var dstIndices = newMeshData.GetIndexData<ushort>();
                for (int i = 0; i < srcIndices.Length; ++i)
                    dstIndices[i] = srcIndices[i];
            }
            else
            {
                var srcIndices = originalMeshData.GetIndexData<int>();
                newMeshData.SetIndexBufferParams(srcIndices.Length, IndexFormat.UInt32);
                var dstIndices = newMeshData.GetIndexData<int>();
                for (int i = 0; i < srcIndices.Length; ++i)
                    dstIndices[i] = srcIndices[i];
            }

            newMeshData.subMeshCount = originalMeshData.subMeshCount;

            for (int i = 0; i != newMeshData.subMeshCount; ++i)
            {
                SubMeshDescriptor submesh = originalMeshData.GetSubMesh(i);
                newMeshData.SetSubMesh(i, submesh);
            }
            //overwrite original mesh
            Matrix4x4[] bindPoses = mesh.bindposes;
            Mesh.ApplyAndDisposeWritableMeshData(newMeshDataArray, mesh);
            mesh.bindposes = bindPoses;
            originalMeshDataArray.Dispose();
		}
#endif
	}
}
