using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	public class MeshIslands
	{
		public int[] vertexIsland;
		public int vertexCount;

		public LinkedIndexListArray islandVertices;
		public int islandCount;

		public MeshIslands(MeshAdjacency meshAdjacency)
		{
			LoadFrom(meshAdjacency);
		}

		public void LoadFrom(MeshAdjacency meshAdjacency)
		{
			vertexCount = meshAdjacency.vertexCount;

			ArrayUtils.ResizeCheckedIfLessThan(ref vertexIsland, vertexCount);

			islandCount = 0;
			islandVertices.Allocate(vertexCount, vertexCount);

			for (int i = 0; i != vertexCount; i++)
			{
				vertexIsland[i] = i;
				islandVertices.Append(i, i);
			}

			for (int i = 0; i != vertexCount; i++)
			{
				foreach (int j in meshAdjacency.vertexVertices[i])
				{
					int islandA = vertexIsland[i];
					int islandB = vertexIsland[j];
					if (islandB != islandA)
					{
						// update vertex->island lookup
						foreach (int vertex in islandVertices[islandB])
						{
							vertexIsland[vertex] = islandA;
						}

						// move adjacent island
						islandVertices.AppendMove(islandA, islandB);
					}
				}
			}

			// remove empty islands
			for (int i = 0; i != vertexCount; i++)
			{
				if (islandVertices.lists[i].size != 0)
					islandVertices.lists[islandCount++] = islandVertices.lists[i];
			}

			for (int i = islandCount; i != vertexCount; i++)
			{
				islandVertices.lists[i].head = -1;
				islandVertices.lists[i].size = 0;
			}

			// update vertex->island lookup
			for (int i = 0; i != islandCount; i++)
			{
				foreach (int vertex in islandVertices[i])
				{
					vertexIsland[vertex] = i;
				}
			}
		}
	}
}
