using System;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;

namespace Unity.DemoTeam.DigitalHuman
{
	[Serializable]
	public class KdTree3
	{
		public struct Node
		{
			public int point;
			public int stepL;
			public int stepR;
		}

		public struct Point3
		{
			public float x;// note: 'x' MUST be first field
			public float y;
			public float z;
			public int index;
		}

		public int size;
		public Node[] nodes;
		public Point3[] points;

		public KdTree3(Vector3[] pointCloud, int pointCount)
		{
			BuildFrom(pointCloud, pointCount);
		}

		//----------------------
		// kd-tree construction

		public void BuildFrom(Vector3[] pointBuffer, int pointCount)
		{
			ArrayUtils.ResizeCheckedIfLessThan(ref this.nodes, pointCount);
			ArrayUtils.ResizeCheckedIfLessThan(ref this.points, pointCount);

			Profiler.BeginSample("kd-prep");
			for (int i = 0; i != pointCount; i++)
			{
				this.points[i].x = pointBuffer[i].x;
				this.points[i].y = pointBuffer[i].y;
				this.points[i].z = pointBuffer[i].z;
				this.points[i].index = i;
			}
			Profiler.EndSample();

			Profiler.BeginSample("kd-build");
			if (pointCount > 0)
			{
				unsafe
				{
					int numThreads = SystemInfo.processorCount;
					//Debug.Log("numThreads = " + numThreads);

					int schedLeaves = Mathf.NextPowerOfTwo(numThreads) * 2;
					int schedNodes = BalancedBinaryTreeInfo.NodeCountFromLeafCount(schedLeaves);
					int schedDepth = BalancedBinaryTreeInfo.DepthFromLeafCount(schedLeaves) - 1;

					//Debug.Log("numThreads=" + numThreads + ", schedLeaves=" + schedLeaves + ", schedNodes=" + schedNodes + ", schedDepth=" + schedDepth);

					var jobArray = (JobHandle*)UnsafeUtility.Malloc(sizeof(JobHandle) * schedNodes, 1, Allocator.Temp);
					var jobSched = jobArray;

					fixed (Node* __node = this.nodes)
					fixed (Point3* __points = this.points)
					{
						ScheduleNode(ref jobSched, null, __node, __points, 0, pointCount, 0, schedDepth);

						JobHandle.ScheduleBatchedJobs();

						long jobCount = jobSched - jobArray;
						for (long i = 0; i != jobCount; i++)
						{
							jobArray[i].Complete();
						}
					}

					UnsafeUtility.Free(jobArray, Allocator.Temp);
				}
			}
			Profiler.EndSample();

			size = pointCount;
		}

		unsafe static void ScheduleNode(ref JobHandle* jobSched, JobHandle* jobDepends, Node* node, Point3* points, int offset, int length, int depth, int depthSingleThreaded)
		{
			// pick median
			int median = length >> 1;

			// calc offsets
			var offsetL = offset;
			var offsetR = offset + median + 1;
			var lengthL = median;
			var lengthR = length - median - 1;
			var stepL = lengthL > 0 ? 1 : 0;
			var stepR = lengthR > 0 ? 1 + lengthL : 0;

			// make job
			var job = new BuildNodeJob()
			{
				node = node,
				points = points,
				offset = offset,
				length = length,
				depth = depth,
				leaf = (depth >= depthSingleThreaded),
			};

			var jobHandle = jobSched++;
			if (jobDepends != null)
				*jobHandle = job.Schedule(*jobDepends);
			else
				*jobHandle = job.Schedule();

			if (depth == 0)
				JobHandle.ScheduleBatchedJobs();

			if (job.leaf)
				return;

			// schedule subtrees
			if (lengthL > 0) ScheduleNode(ref jobSched, jobHandle, node + stepL, points, offsetL, lengthL, depth + 1, depthSingleThreaded);
			if (lengthR > 0) ScheduleNode(ref jobSched, jobHandle, node + stepR, points, offsetR, lengthR, depth + 1, depthSingleThreaded);
		}

		[BurstCompile]
		unsafe struct BuildNodeJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] public Node* node;
			[NativeDisableUnsafePtrRestriction] public Point3* points;// shared

			public int offset;
			public int length;
			public int depth;
			public bool leaf;

			public void Execute()
			{
				if (leaf)
					BuildNode(node, points, offset, length, depth);
				else
					BuildNodeDeferSubtrees(node, points, offset, length, depth);
			}
		}

		unsafe static void BuildNode(Node* node, Point3* points, int offset, int length, int depth)
		{
			// pick median
			int median = length >> 1;

			// pick splitting axis
			int axis = depth % 3;

			// split points by median
			SelectByAxis(median, points + offset, length, axis);

			// calc offsets
			var offsetL = offset;
			var offsetR = offset + median + 1;
			var lengthL = median;
			var lengthR = length - median - 1;
			var stepL = lengthL > 0 ? 1 : 0;
			var stepR = lengthR > 0 ? 1 + lengthL : 0;

			// make node
			node->point = offset + median;
			node->stepL = stepL;
			node->stepR = stepR;

			// build subtrees
			if (lengthL > 0) BuildNode(node + stepL, points, offsetL, lengthL, depth + 1);
			if (lengthR > 0) BuildNode(node + stepR, points, offsetR, lengthR, depth + 1);
		}

		unsafe static void BuildNodeDeferSubtrees(Node* node, Point3* points, int offset, int length, int depth)
		{
			// pick median
			int median = length >> 1;

			// pick splitting axis
			int axis = depth % 3;

			// split points by median
			SelectByAxis(median, points + offset, length, axis);

			// calc offsets
			var lengthL = median;
			var lengthR = length - median - 1;
			var stepL = lengthL > 0 ? 1 : 0;
			var stepR = lengthR > 0 ? 1 + lengthL : 0;

			// make node
			node->point = offset + median;
			node->stepL = stepL;
			node->stepR = stepR;
		}

		unsafe static void SelectByAxis(int nth, Point3* points, int length, int axis)
		{
			// note: this function has been adapted from public domain
			//       variants listed in 'KdTree_nth_element.txt'
			const int strideLsh = 2;

			var i = 0;
			var j = length - 1;
			var v = &points->x + axis;

			while (i < j)
			{
				var r = i;
				var w = j;

				float k = v[((r + w) >> 1) << strideLsh];

				while (r < w)
				{
					if (v[r << strideLsh] >= k)
					{
						Point3 pw = points[w];
						points[w] = points[r];
						points[r] = pw;
						w--;
					}
					else
					{
						r++;
					}
				}

				if (v[r << strideLsh] > k)
					r--;

				if (nth <= r)
					j = r;
				else
					i = r + 1;
			}
		}

		//-----------------
		// kd-tree queries

		public int FindNearest(ref Vector3 target)
		{
			var bestDist = float.PositiveInfinity;
			var bestNode = -1;

			Profiler.BeginSample("kd-nearest");
			FindNearest(ref bestDist, ref bestNode, 0, 0, ref target);
			Profiler.EndSample();

			if (bestNode != -1)
				return points[nodes[bestNode].point].index;
			else
				return -1;
		}

		public bool FindNearest(ref float bestDist, ref int bestNode, ref Vector3 target)
		{
			var __bestDist = float.PositiveInfinity;
			var __bestNode = -1;

			Profiler.BeginSample("kd-nearest");
			FindNearest(ref __bestDist, ref __bestNode, 0, 0, ref target);
			Profiler.EndSample();

			if (__bestNode != -1)
			{
				bestDist = __bestDist;
				bestNode = points[nodes[__bestNode].point].index;
				return true;
			}
			else
			{
				return false;
			}
		}

		unsafe void FindNearest(ref float bestDist, ref int bestNode, int node, int depth, ref Vector3 target)
		{
			// update best index
			int point = nodes[node].point;
			Vector3 r;
			r.x = target.x - points[point].x;
			r.y = target.y - points[point].y;
			r.z = target.z - points[point].z;

			var dist = r.x * r.x + r.y * r.y + r.z * r.z;
			if (dist < bestDist)
			{
				bestDist = dist;
				bestNode = node;
			}

			// pick search axis
			int axis = depth % 3;

			// pick near, far
			var delta = *(&r.x + axis);// avoid calling operator[]
			int stepN = delta < 0.0f ? nodes[node].stepL : nodes[node].stepR;
			int stepF = delta < 0.0f ? nodes[node].stepR : nodes[node].stepL;

			// search near
			if (stepN != 0)
			{
				FindNearest(ref bestDist, ref bestNode, node + stepN, depth + 1, ref target);
			}

			// search far
			if (stepF != 0 && delta * delta < bestDist)
			{
				FindNearest(ref bestDist, ref bestNode, node + stepF, depth + 1, ref target);
			}
		}

		public int RaycastApprox(ref Vector3 origin, ref Vector3 direction, int maxIterations = 100)
		{
			var position = origin;
			var numIterations = 0;

			var bestDist = float.PositiveInfinity;
			var bestNode = -1;

			while (numIterations++ < maxIterations)
			{
				var stepDist = float.PositiveInfinity;
				var stepNode = -1;

				FindNearest(ref stepDist, ref stepNode, ref position);

				if (stepDist < bestDist)
				{
					bestDist = stepDist;
					bestNode = stepNode;

					if (bestDist < float.Epsilon)
					{
						break;// crude termination criteria
					}
				}

				position = position + Mathf.Sqrt(stepDist) * direction;
			}

			return bestNode;
		}
	}

	public static class BalancedBinaryTreeInfo
	{
		public static int LeafCountFromDepth(int depth)
		{
			int leafCount = (depth > 0) ? 1 : 0;
			while (--depth > 0) leafCount = (leafCount << 1);
			return leafCount;
		}

		public static int NodeCountFromDepth(int depth)
		{
			int nodeCount = 0;
			while (--depth >= 0) nodeCount = (nodeCount << 1) | 1;
			return nodeCount;
		}

		public static int NodeCountFromLeafCount(int leafCount)
		{
			Debug.Assert(Mathf.IsPowerOfTwo(leafCount));
			int nodeCount = leafCount;
			while ((leafCount >>= 1) > 0) nodeCount += leafCount;
			return nodeCount;
		}

		public static int DepthFromLeafCount(int leafCount)
		{
			Debug.Assert(Mathf.IsPowerOfTwo(leafCount));
			int depth = 0;
			while (leafCount > 0) { leafCount >>= 1; depth++; }
			return depth;
		}
	}
}
