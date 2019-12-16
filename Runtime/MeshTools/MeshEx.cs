using UnityEngine;

public static  class MeshEx
{
	public static void EnableSilentWrites(this Mesh mesh, bool enable)
	{
#if UNITY_2019_3_DEMOS_CAVE
		mesh.enableSilentWrites = enable;
#endif
	}
}
