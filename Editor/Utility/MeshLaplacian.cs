using CSparse;
using CSparse.Double;
using CSparse.Double.Factorization;
using CSparse.Storage;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DemoTeam.DigitalHuman
{
	public class MeshLaplacian
	{
		public int internalCount;
		public double[] vertexDifferentialX;
		public double[] vertexDifferentialY;
		public double[] vertexDifferentialZ;
	}

	public class MeshLaplacianTransform
	{
		public int vertexCount;

		public int[] constraintIndices;
		public double constraintWeight;

		public SparseMatrix Ls;
		public SparseMatrix Lc;
		public SparseMatrix LcT;
		public SparseMatrix LcT_Lc;
		public SparseCholesky LcT_Lc_chol;

		public MeshLaplacianTransform(MeshAdjacency meshAdjacency, int[] constraintIndices)
		{
			BuildFrom(meshAdjacency, constraintIndices);
		}

		public void BuildFrom(MeshAdjacency meshAdjacency, int[] constraintIndices)
		{
			vertexCount = meshAdjacency.vertexCount;

			this.constraintIndices = constraintIndices.Clone() as int[];
			this.constraintWeight = 1.0;

			// count unconstrained laplacian non-zero fields
			int nzmax = vertexCount;
			for (int i = 0; i != vertexCount; i++)
			{
				nzmax += meshAdjacency.vertexVertices.lists[i].size;
			}

			// build Ls
			EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build Ls", 0.0f);
			var Ls_storage = new CoordinateStorage<double>(vertexCount, vertexCount, nzmax);
			for (int i = 0; i != vertexCount; i++)// D
			{
				//TODO proper fix
				//Ls_storage.At(i, i, meshAdjacency.vertexVertices.lists[i].size);
				Ls_storage.At(i, i, Mathf.Max(1, meshAdjacency.vertexVertices.lists[i].size));
			}
			for (int i = 0; i != vertexCount; i++)// A
			{
				foreach (var j in meshAdjacency.vertexVertices[i])
				{
					Ls_storage.At(i, j, -1.0);
				}
			}
			Ls = Converter.ToCompressedColumnStorage(Ls_storage) as SparseMatrix;

			// build Lc
			EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build Lc", 0.0f);
			var Lc_storage = new CoordinateStorage<double>(vertexCount + constraintIndices.Length, vertexCount, nzmax + constraintIndices.Length);
			for (int i = 0; i != vertexCount; i++)
			{
				//TODO proper fix
				//Lc_storage.At(i, i, meshAdjacency.vertexVertices.lists[i].size);
				Lc_storage.At(i, i, Mathf.Max(1, meshAdjacency.vertexVertices.lists[i].size));
			}
			for (int i = 0; i != vertexCount; i++)
			{
				foreach (var j in meshAdjacency.vertexVertices[i])
				{
					Lc_storage.At(i, j, -1.0);
				}
			}
			for (int i = 0; i != constraintIndices.Length; i++)
			{
				Lc_storage.At(vertexCount + i, constraintIndices[i], constraintWeight);
			}
			Lc = Converter.ToCompressedColumnStorage(Lc_storage) as SparseMatrix;

			// build LcT
			EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build LcT", 0.0f);
			LcT = Lc.Transpose() as SparseMatrix;

			// build LcT_Lc
			EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build LcT_Lc", 0.0f);
			LcT_Lc = LcT.Multiply(Lc) as SparseMatrix;

			// build LcT_Lc_chol
			EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build LcT_Lc_chol", 0.0f);
			LcT_Lc_chol = SparseCholesky.Create(LcT_Lc, ColumnOrdering.MinimumDegreeAtPlusA);

			// done
			EditorUtilityProxy.ClearProgressBar();
		}

		public void ComputeMeshLaplacian(MeshLaplacian meshLaplacian, MeshBuffers meshBuffers)
		{
			Debug.Assert(vertexCount == meshBuffers.vertexCount);

			// Ls x = diffcoords
			unsafe
			{
				var vertexPositionX = new double[vertexCount];
				var vertexPositionY = new double[vertexCount];
				var vertexPositionZ = new double[vertexCount];

				fixed (Vector3* src = meshBuffers.vertexPositions)
				fixed (double* dstX = vertexPositionX)
				fixed (double* dstY = vertexPositionY)
				fixed (double* dstZ = vertexPositionZ)
				{
					for (int i = 0; i != vertexCount; i++)
					{
						dstX[i] = src[i].x;
						dstY[i] = src[i].y;
						dstZ[i] = src[i].z;
					}
				}

				ArrayUtils.ResizeCheckedIfLessThan(ref meshLaplacian.vertexDifferentialX, vertexCount);
				ArrayUtils.ResizeCheckedIfLessThan(ref meshLaplacian.vertexDifferentialY, vertexCount);
				ArrayUtils.ResizeCheckedIfLessThan(ref meshLaplacian.vertexDifferentialZ, vertexCount);

				Ls.Multiply(vertexPositionX, meshLaplacian.vertexDifferentialX);
				Ls.Multiply(vertexPositionY, meshLaplacian.vertexDifferentialY);
				Ls.Multiply(vertexPositionZ, meshLaplacian.vertexDifferentialZ);

				meshLaplacian.internalCount = vertexCount;
			}
		}

		public void ResolveMeshBuffers(MeshBuffers meshBuffers, MeshLaplacian meshLaplacian)
		{
			Debug.Assert(vertexCount == meshBuffers.vertexCount);
			Debug.Assert(vertexCount == meshLaplacian.internalCount);

			int constraintCount = constraintIndices.Length;

			// c = 'm' spatial constraints [c0 c1 ... cm]
			// Lc = [Ls I|0] where dim(I) = m
			// Lc x = [diffcoords c]
			// x* = (Lc^T Lc)^-1 Lc^T [diffcoords c]
			unsafe
			{
				var constrainedDifferentialX = new double[vertexCount + constraintCount];
				var constrainedDifferentialY = new double[vertexCount + constraintCount];
				var constrainedDifferentialZ = new double[vertexCount + constraintCount];

				fixed (double* srcX = meshLaplacian.vertexDifferentialX)
				fixed (double* srcY = meshLaplacian.vertexDifferentialY)
				fixed (double* srcZ = meshLaplacian.vertexDifferentialZ)
				fixed (double* dstX = constrainedDifferentialX)
				fixed (double* dstY = constrainedDifferentialY)
				fixed (double* dstZ = constrainedDifferentialZ)
				{
					UnsafeUtility.MemCpy(dstX, srcX, sizeof(double) * vertexCount);
					UnsafeUtility.MemCpy(dstY, srcY, sizeof(double) * vertexCount);
					UnsafeUtility.MemCpy(dstZ, srcZ, sizeof(double) * vertexCount);

					for (int k = 0; k != constraintCount; k++)
					{
						int j = constraintIndices[k];
						dstX[vertexCount + k] = constraintWeight * meshBuffers.vertexPositions[j].x;
						dstY[vertexCount + k] = constraintWeight * meshBuffers.vertexPositions[j].y;
						dstZ[vertexCount + k] = constraintWeight * meshBuffers.vertexPositions[j].z;
					}
				}

				var LcT_constrainedDifferentialX = new double[vertexCount + constraintCount];
				var LcT_constrainedDifferentialY = new double[vertexCount + constraintCount];
				var LcT_constrainedDifferentialZ = new double[vertexCount + constraintCount];

				LcT.Multiply(constrainedDifferentialX, LcT_constrainedDifferentialX);
				LcT.Multiply(constrainedDifferentialY, LcT_constrainedDifferentialY);
				LcT.Multiply(constrainedDifferentialZ, LcT_constrainedDifferentialZ);

				var resultPositionX = new double[vertexCount + constraintCount];
				var resultPositionY = new double[vertexCount + constraintCount];
				var resultPositionZ = new double[vertexCount + constraintCount];

				Profiler.BeginSample("chol-solve");
				LcT_Lc_chol.Solve(LcT_constrainedDifferentialX, resultPositionX);
				LcT_Lc_chol.Solve(LcT_constrainedDifferentialY, resultPositionY);
				LcT_Lc_chol.Solve(LcT_constrainedDifferentialZ, resultPositionZ);
				Profiler.EndSample();

				fixed (double* srcX = resultPositionX)
				fixed (double* srcY = resultPositionY)
				fixed (double* srcZ = resultPositionZ)
				fixed (float* dstX = &meshBuffers.vertexPositions[0].x)
				fixed (float* dstY = &meshBuffers.vertexPositions[0].y)
				fixed (float* dstZ = &meshBuffers.vertexPositions[0].z)
				{
					const int dstStride = 3;// sizeof(Vector3) / sizeof(float)
					for (int i = 0; i != vertexCount; i++)
						dstX[i * dstStride] = (float)srcX[i];
					for (int i = 0; i != vertexCount; i++)
						dstY[i * dstStride] = (float)srcY[i];
					for (int i = 0; i != vertexCount; i++)
						dstZ[i * dstStride] = (float)srcZ[i];
				}
			}
		}
	}

	public class MeshLaplacianTransformROI
	{
		public int internalCount;
		public int externalCount;

		public int[] internalFromExternal;// [0..mesh.vertexCount]
		public int[] externalFromInternal;// [0..internalCount]

		public int[] constraintIndices;
		public double constraintWeight;

		public SparseMatrix Ls;
		public SparseMatrix Lc;
		public SparseMatrix LcT;
		public SparseMatrix LcT_Lc;
		public SparseCholesky LcT_Lc_chol;

		private int InternalValence(MeshAdjacency meshAdjacency, int i)
		{
			int n = 0;
			foreach (var k in meshAdjacency.vertexVertices[externalFromInternal[i]])
			{
				if (internalFromExternal[k] != -1)
					n++;
			}
			return n;
		}

		public MeshLaplacianTransformROI(MeshAdjacency meshAdjacency, int[] roiIndices, int roiConstraintBoundary, int[] roiConstraintIndices = null)
		{
			BuildFrom(meshAdjacency, roiIndices, roiConstraintBoundary, roiConstraintIndices);
		}

		public void BuildFrom(MeshAdjacency meshAdjacency, int[] roiIndices, int roiBoundaryLevels, int[] roiConstraintIndices = null)
		{
			unsafe
			{
				using (var visited = new UnsafeArrayBool(meshAdjacency.vertexCount))
				using (var visitedBoundary = new UnsafeArrayBool(meshAdjacency.vertexCount))
				using (var visitor = new UnsafeBFS(meshAdjacency.vertexCount))
				{
					// find boundary
					visited.Clear(false);
					visitedBoundary.Clear(false);
					visitor.Clear();

					int visitedCount = 0;
					int visitedBoundaryCount = 0;

					foreach (int i in roiIndices)
					{
						visited.val[i] = true;
						visitedCount++;
						visitor.Ignore(i);
					}

					foreach (int i in roiIndices)
					{
						foreach (var j in meshAdjacency.vertexVertices[i])
						{
							visitor.Insert(j);
						}
					}

					// step boundary
					while (visitor.MoveNext())
					{
						int i = visitor.position;

						visited.val[i] = true;
						visitedCount++;
						visitedBoundary.val[i] = true;
						visitedBoundaryCount++;

						if (visitor.depth < roiBoundaryLevels)
						{
							foreach (var j in meshAdjacency.vertexVertices[i])
							{
								visitor.Insert(j);
							}
						}
					}

					// add constraints
					if (roiConstraintIndices != null)
					{
						foreach (int i in roiConstraintIndices)
						{
							if (visited.val[i])
							{
								if (visitedBoundary.val[i] == false)
								{
									visitedBoundary.val[i] = true;
									visitedBoundaryCount++;
								}
							}
							else
							{
								Debug.LogWarning("ignoring user constraint outside ROI: vertex " + i);
							}
						}
					}

					// build translations
					internalCount = 0;
					externalCount = meshAdjacency.vertexCount;

					internalFromExternal = new int[externalCount];
					externalFromInternal = new int[visitedCount];

					for (int i = 0; i != meshAdjacency.vertexCount; i++)
					{
						if (visited.val[i])
						{
							int internalIndex = internalCount++;
							externalFromInternal[internalIndex] = i;
							internalFromExternal[i] = internalIndex;
						}
						else
						{
							internalFromExternal[i] = -1;
						}
					}

					// find constraint indices
					constraintIndices = new int[visitedBoundaryCount];
					constraintWeight = 1.0;

					int constraintCount = 0;
					for (int i = 0; i != internalCount; i++)
					{
						if (visitedBoundary.val[externalFromInternal[i]])
						{
							constraintIndices[constraintCount++] = i;
						}
					}

					// count unconstrained laplacian non-zero fields
					int nzmax = internalCount;
					for (int i = 0; i != internalCount; i++)
					{
						nzmax += InternalValence(meshAdjacency, i);
					}

					// build Ls
					EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build Ls", 0.0f);
					var Ls_storage = new CoordinateStorage<double>(internalCount, internalCount, nzmax);
					for (int i = 0; i != internalCount; i++)// D
					{
						//TODO proper fix
						//Ls_storage.At(i, i, InternalValence(meshAdjacency, i));
						Ls_storage.At(i, i, Mathf.Max(1, InternalValence(meshAdjacency, i)));
					}
					for (int i = 0; i != internalCount; i++)// A
					{
						foreach (var k in meshAdjacency.vertexVertices[externalFromInternal[i]])
						{
							int j = internalFromExternal[k];
							if (j != -1)
							{
								Ls_storage.At(i, j, -1.0);
							}
						}
					}
					Ls = Converter.ToCompressedColumnStorage(Ls_storage) as SparseMatrix;

					// build Lc
					EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build Lc", 0.0f);
					var Lc_storage = new CoordinateStorage<double>(internalCount + constraintCount, internalCount, nzmax + constraintCount);
					for (int i = 0; i != internalCount; i++)
					{
						//TODO proper fix
						//Lc_storage.At(i, i, InternalValence(meshAdjacency, i));
						Lc_storage.At(i, i, Mathf.Max(1, InternalValence(meshAdjacency, i)));
					}
					for (int i = 0; i != internalCount; i++)
					{
						foreach (var k in meshAdjacency.vertexVertices[externalFromInternal[i]])
						{
							int j = internalFromExternal[k];
							if (j != -1)
							{
								Lc_storage.At(i, j, -1.0);
							}
						}
					}
					for (int i = 0; i != constraintIndices.Length; i++)
					{
						Lc_storage.At(internalCount + i, constraintIndices[i], constraintWeight);
					}
					Lc = Converter.ToCompressedColumnStorage(Lc_storage) as SparseMatrix;

					// build LcT
					EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build LcT", 0.0f);
					LcT = Lc.Transpose() as SparseMatrix;

					// build LcT_Lc
					EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build LcT_Lc", 0.0f);
					LcT_Lc = LcT.Multiply(Lc) as SparseMatrix;

					// build LcT_Lc_chol
					EditorUtilityProxy.DisplayProgressBar("MeshLaplacian", "build LcT_Lc_chol", 0.0f);
					LcT_Lc_chol = SparseCholesky.Create(LcT_Lc, ColumnOrdering.MinimumDegreeAtPlusA);

					// done
					EditorUtilityProxy.ClearProgressBar();
				}
			}
		}

		public void ComputeMeshLaplacian(MeshLaplacian meshLaplacian, MeshBuffers meshBuffers)
		{
			Debug.Assert(externalCount == meshBuffers.vertexCount);

			// Ls x = diffcoords
			unsafe
			{
				var vertexPositionX = new double[internalCount];
				var vertexPositionY = new double[internalCount];
				var vertexPositionZ = new double[internalCount];

				fixed (Vector3* src = meshBuffers.vertexPositions)
				fixed (double* dstX = vertexPositionX)
				fixed (double* dstY = vertexPositionY)
				fixed (double* dstZ = vertexPositionZ)
				{
					for (int i = 0; i != internalCount; i++)
					{
						int k = externalFromInternal[i];
						dstX[i] = src[k].x;
						dstY[i] = src[k].y;
						dstZ[i] = src[k].z;
					}
				}

				ArrayUtils.ResizeCheckedIfLessThan(ref meshLaplacian.vertexDifferentialX, internalCount);
				ArrayUtils.ResizeCheckedIfLessThan(ref meshLaplacian.vertexDifferentialY, internalCount);
				ArrayUtils.ResizeCheckedIfLessThan(ref meshLaplacian.vertexDifferentialZ, internalCount);

				Ls.Multiply(vertexPositionX, meshLaplacian.vertexDifferentialX);
				Ls.Multiply(vertexPositionY, meshLaplacian.vertexDifferentialY);
				Ls.Multiply(vertexPositionZ, meshLaplacian.vertexDifferentialZ);

				meshLaplacian.internalCount = internalCount;
			}
		}

		public void ResolveMeshBuffers(MeshBuffers meshBuffers, MeshLaplacian meshLaplacian)
		{
			Debug.Assert(externalCount == meshBuffers.vertexCount);
			Debug.Assert(internalCount == meshLaplacian.internalCount);

			int constraintCount = constraintIndices.Length;

			// c = 'm' spatial constraints [c0 c1 ... cm]
			// Lc = [Ls I|0] where dim(I) = m
			// Lc x = [diffcoords c]
			// x* = (Lc^T Lc)^-1 Lc^T [diffcoords c]
			unsafe
			{
				var constrainedDifferentialX = new double[internalCount + constraintCount];
				var constrainedDifferentialY = new double[internalCount + constraintCount];
				var constrainedDifferentialZ = new double[internalCount + constraintCount];

				fixed (double* srcX = meshLaplacian.vertexDifferentialX)
				fixed (double* srcY = meshLaplacian.vertexDifferentialY)
				fixed (double* srcZ = meshLaplacian.vertexDifferentialZ)
				fixed (double* dstX = constrainedDifferentialX)
				fixed (double* dstY = constrainedDifferentialY)
				fixed (double* dstZ = constrainedDifferentialZ)
				{
					UnsafeUtility.MemCpy(dstX, srcX, sizeof(double) * internalCount);
					UnsafeUtility.MemCpy(dstY, srcY, sizeof(double) * internalCount);
					UnsafeUtility.MemCpy(dstZ, srcZ, sizeof(double) * internalCount);

					for (int i = 0; i != constraintCount; i++)
					{
						int k = externalFromInternal[constraintIndices[i]];
						dstX[internalCount + i] = constraintWeight * meshBuffers.vertexPositions[k].x;
						dstY[internalCount + i] = constraintWeight * meshBuffers.vertexPositions[k].y;
						dstZ[internalCount + i] = constraintWeight * meshBuffers.vertexPositions[k].z;
					}
				}

				var LcT_constrainedDifferentialX = new double[internalCount + constraintCount];
				var LcT_constrainedDifferentialY = new double[internalCount + constraintCount];
				var LcT_constrainedDifferentialZ = new double[internalCount + constraintCount];

				LcT.Multiply(constrainedDifferentialX, LcT_constrainedDifferentialX);
				LcT.Multiply(constrainedDifferentialY, LcT_constrainedDifferentialY);
				LcT.Multiply(constrainedDifferentialZ, LcT_constrainedDifferentialZ);

				var resultPositionX = new double[internalCount + constraintCount];
				var resultPositionY = new double[internalCount + constraintCount];
				var resultPositionZ = new double[internalCount + constraintCount];

				Profiler.BeginSample("chol-solve");
				LcT_Lc_chol.Solve(LcT_constrainedDifferentialX, resultPositionX);
				LcT_Lc_chol.Solve(LcT_constrainedDifferentialY, resultPositionY);
				LcT_Lc_chol.Solve(LcT_constrainedDifferentialZ, resultPositionZ);
				Profiler.EndSample();

				fixed (double* srcX = resultPositionX)
				fixed (double* srcY = resultPositionY)
				fixed (double* srcZ = resultPositionZ)
				fixed (float* dstX = &meshBuffers.vertexPositions[0].x)
				fixed (float* dstY = &meshBuffers.vertexPositions[0].y)
				fixed (float* dstZ = &meshBuffers.vertexPositions[0].z)
				{
					const int dstStride = 3;// sizeof(Vector3) / sizeof(float)
					for (int i = 0; i != internalCount; i++)
					{
						int k = externalFromInternal[i];
						dstX[k * dstStride] = (float)srcX[i];
						dstY[k * dstStride] = (float)srcY[i];
						dstZ[k * dstStride] = (float)srcZ[i];
					}
				}
			}
		}
	}
}
