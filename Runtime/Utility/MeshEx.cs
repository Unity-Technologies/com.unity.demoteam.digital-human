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
	}
}
