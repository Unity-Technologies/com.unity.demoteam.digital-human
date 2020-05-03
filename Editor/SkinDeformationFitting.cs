using System;
using Unity.Jobs;
using Accord.Math;
using Accord.Statistics.Models.Regression.Fitting;

namespace Unity.DemoTeam.DigitalHuman
{
	using Param = SkinDeformationFittingOptions.Param;
	using Method = SkinDeformationFittingOptions.Method;

	public static class SkinDeformationFitting
	{
		private static T[][] MakeJagged<T>(T[,] m)
		{
			var numRows = m.GetLength(0);
			var numCols = m.GetLength(1);
			var jaggedM = new T[numRows][];

			for (int i = 0; i != numRows; i++)
			{
				jaggedM[i] = new T[numCols];
				for (int j = 0; j != numCols; j++)
				{
					jaggedM[i][j] = m[i, j];
				}
			}

			return jaggedM;
		}

		public static int[] GetBlendShapeIndices(UnityEngine.Mesh mesh)
		{
			var numShapes = mesh.blendShapeCount;
			var outShapes = new int[numShapes];

			for (int k = 0; k != numShapes; k++)
			{
				outShapes[k] = k;
			}

			return outShapes;
		}

		public static int[] ComputeLinearlyIndependentBlendShapeIndices(UnityEngine.Mesh mesh)
		{
			return ComputeLinearlyIndependentBlendShapeIndices(mesh, GetBlendShapeIndices(mesh));
		}

		public static int[] ComputeLinearlyIndependentBlendShapeIndices(UnityEngine.Mesh mesh, int[] blendShapeIndices)
		{
			var numShapes = blendShapeIndices.Length;
			var outShapes = new int[numShapes];

			var numVertices = mesh.vertexCount;
			var numEquations = 3 * numVertices;
			var numVariables = 0;

			var tmpPositions = new UnityEngine.Vector3[numVertices];
			var tmpTangents = new UnityEngine.Vector3[numVertices];
			var tmpNormals = new UnityEngine.Vector3[numVertices];

			var A = new double[numEquations, 1];// start with an empty column
			var _ = new double[numEquations];
			{
				for (int j = 0; j != numShapes; j++)
				{
					int k = blendShapeIndices[j];

					if (EditorUtilityProxy.DisplayCancelableProgressBar("Filter linearly independent blend shapes", "Processing shape " + k + " (" + mesh.GetBlendShapeName(k) + ")", j / (float)numShapes))
					{
						outShapes = null;
						break;// user cancelled
					}
					mesh.GetBlendShapeFrameVertices(k, 0, tmpPositions, tmpNormals, tmpTangents);

					for (int i = 0; i != numVertices; i++)
					{
						_[i * 3 + 0] = tmpPositions[i].x;
						_[i * 3 + 1] = tmpPositions[i].y;
						_[i * 3 + 2] = tmpPositions[i].z;
					}

					A.SetColumn(numVariables, _);// write to empty column

					var rank = Matrix.Rank(A.TransposeAndDot(A));
					if (rank == numVariables + 1)
					{
						outShapes[numVariables++] = k;
						A = A.Concatenate(_);// grow by one column
					}
					else
					{
						UnityEngine.Debug.LogWarning("shape " + k + " (" + mesh.GetBlendShapeName(k) + ") did NOT increase rank => skip");
					}
				}

				EditorUtilityProxy.ClearProgressBar();
			}

			if (outShapes != null)
			{
				Array.Resize(ref outShapes, numVariables);
			}

			return outShapes;
		}

		public static void FitFramesToBlendShapes(SkinDeformation[] frames, UnityEngine.Mesh mesh, int[] blendShapeIndices, Method fittingMethod, Param fittingParam)
		{
			// Ax = b
			// x* = (A^T A)^-1 A^T b
			int numShapes = mesh.blendShapeCount;
			int numVertices = mesh.vertexCount;
			int numEquations = 0;
			int numVariables = blendShapeIndices.Length;

			var meshBuffers = null as MeshBuffers;
			var meshEdges = null as MeshEdges;

			switch (fittingParam)
			{
				case Param.DeltaPosition:
					numEquations = 3 * numVertices;
					break;

				case Param.OutputEdgeLength:
				case Param.OutputEdgeCurvature:
					meshBuffers = new MeshBuffers(mesh);
					meshEdges = new MeshEdges(mesh.triangles);
					numEquations = meshEdges.edges.Length;
					break;
			}

			var tmpPositions = new UnityEngine.Vector3[numVertices];
			var tmpTangents = new UnityEngine.Vector3[numVertices];
			var tmpNormals = new UnityEngine.Vector3[numVertices];

			var edgeLengths = null as float[];
			var edgeCurvatures = null as float[];

			// prepare A
			var A = new double[numEquations, numVariables];
			var _ = new double[numEquations];
			{
				for (int j = 0; j != numVariables; j++)
				{
					int k = blendShapeIndices[j];

					EditorUtilityProxy.DisplayProgressBar("Building 'A'", "Processing shape " + k + " (" + mesh.GetBlendShapeName(k) + ")", j / (float)numVariables);
					mesh.GetBlendShapeFrameVertices(k, 0, tmpPositions, tmpNormals, tmpTangents);

					switch (fittingParam)
					{
						case Param.DeltaPosition:
							for (int i = 0; i != numVertices; i++)
							{
								_[i * 3 + 0] = tmpPositions[i].x;
								_[i * 3 + 1] = tmpPositions[i].y;
								_[i * 3 + 2] = tmpPositions[i].z;
							}
							break;

						case Param.OutputEdgeLength:
							for (int i = 0; i != numVertices; i++)
							{
								tmpPositions[i] += meshBuffers.vertexPositions[i];
							}
							meshEdges.ComputeLengths(ref edgeLengths, tmpPositions);
							for (int i = 0; i != numEquations; i++)
							{
								_[i] = edgeLengths[i];
							}
							break;

						case Param.OutputEdgeCurvature:
							for (int i = 0; i != numVertices; i++)
							{
								tmpPositions[i] += meshBuffers.vertexPositions[i];
								tmpNormals[i] += meshBuffers.vertexNormals[i];
								tmpNormals[i].Normalize();
							}
							meshEdges.ComputeCurvatures(ref edgeCurvatures, tmpPositions, tmpNormals);
							for (int i = 0; i != numEquations; i++)
							{
								_[i] = edgeCurvatures[i];
							}
							break;
					}

					A.SetColumn(j, _);
				}
			}

			// prepare (A^T A)^-1 A^T for LLS
			var At_A_inv_At = null as double[,];
			if (fittingMethod == Method.LinearLeastSquares)
			{
				EditorUtilityProxy.DisplayProgressBar("Computing A^T", "Processing ...", 0.0f);
				var At = A.Transpose();
				EditorUtilityProxy.DisplayProgressBar("Computing (A^T A)", "Processing ...", 0.25f);
				var At_A = At.Dot(A);
				EditorUtilityProxy.DisplayProgressBar("Computing (A^T A)^-1", "Processing ...", 0.5f);
				var At_A_inv = At_A.Inverse();
				EditorUtilityProxy.DisplayProgressBar("Computing (A^T A)^-1 A^T", "Processing ...", 0.75f);
				At_A_inv_At = At_A_inv.Dot(At);
			}

			// prepare A[][] for NNLS
			var A_jagged = null as double[][];
			if (fittingMethod == Method.NonNegativeLeastSquares)
			{
				A_jagged = MakeJagged(A);
			}

			// prepare shared job data
			sharedJobData.frames = frames;
			sharedJobData.blendShapeIndices = blendShapeIndices;

			sharedJobData.meshBuffers = meshBuffers;
			sharedJobData.meshEdges = meshEdges;

			sharedJobData.numEquations = numEquations;
			sharedJobData.numVariables = numVariables;
			sharedJobData.numVertices = numVertices;

			sharedJobData.fittingMethod = fittingMethod;
			sharedJobData.fittingParam = fittingParam;

			sharedJobData.At_A_inv_At = At_A_inv_At;
			sharedJobData.A_jagged = A_jagged;

			// prepare jobs
			var jobs = new FrameFittingJob[frames.Length];
			var jobHandles = new JobHandle[frames.Length];

			for (int k = 0; k != jobs.Length; k++)
				jobs[k].frameIndex = k;

			// execute jobs
			for (int k = 0; k != jobs.Length; k++)
				jobHandles[k] = jobs[k].Schedule();

			// wait until done
			var progressTime0 = DateTime.Now;
			var progressNumCompleted = -1;
			while (true)
			{
				int numCompleted = 0;
				for (int i = 0; i != jobs.Length; i++)
					numCompleted += (jobHandles[i].IsCompleted ? 1 : 0);

				if (numCompleted == jobs.Length)
					break;

				if (numCompleted > progressNumCompleted)
				{
					var progressVal = numCompleted / (float)frames.Length;
					var progressMsg = "Processing frames ... Completed " + numCompleted + " / " + frames.Length;
					if (progressVal > 0.0f)
					{
						var timeElapsed = DateTime.Now - progressTime0;
						var timeArrival = TimeSpan.FromMilliseconds(timeElapsed.TotalMilliseconds / progressVal);
						var timeRemains = timeArrival - timeElapsed;

						progressMsg += " ... Est. time " + string.Format("{0}m{1:D2}s", (int)UnityEngine.Mathf.Floor((float)timeRemains.TotalMinutes), timeRemains.Seconds);
					}

					switch (sharedJobData.fittingMethod)
					{
						case Method.LinearLeastSquares:
							EditorUtilityProxy.DisplayProgressBar("Computing x* = (A^T A)^-1 A^T b", progressMsg, progressVal);
							break;

						case Method.NonNegativeLeastSquares:
							EditorUtilityProxy.DisplayProgressBar("Computing x* = NonNegativeLeastSquares(A, b)", progressMsg, progressVal);
							break;
					}

					progressNumCompleted = numCompleted;
				}
				else
				{
					System.Threading.Thread.Sleep(1);
				}
			}
		}

		static SharedJobData sharedJobData;
		struct SharedJobData
		{
			public SkinDeformation[] frames;
			public int[] blendShapeIndices;

			public MeshBuffers meshBuffers;
			public MeshEdges meshEdges;

			public int numVariables;
			public int numEquations;
			public int numVertices;

			public Method fittingMethod;
			public Param fittingParam;

			public double[,] At_A_inv_At;
			public double[][] A_jagged;
		}

		struct FrameFittingJob : IJob
		{
			public int frameIndex;

			public void Execute()
			{
				int k = frameIndex;

				var tmpPositions = new UnityEngine.Vector3[sharedJobData.numVertices];
				var tmpNormals = new UnityEngine.Vector3[sharedJobData.numVertices];

				var edgeLengths = null as float[];
				var edgeCurvatures = null as float[];

				// prepare b
				var b = new double[sharedJobData.numEquations];
				{
					Array.Copy(sharedJobData.frames[k].deltaPositions, tmpPositions, tmpPositions.Length);
					Array.Copy(sharedJobData.frames[k].deltaNormals, tmpNormals, tmpNormals.Length);

					switch (sharedJobData.fittingParam)
					{
						case Param.DeltaPosition:
							for (int i = 0; i != sharedJobData.numVertices; i++)
							{
								b[i * 3 + 0] = tmpPositions[i].x;
								b[i * 3 + 1] = tmpPositions[i].y;
								b[i * 3 + 2] = tmpPositions[i].z;
							}
							break;

						case Param.OutputEdgeLength:
							for (int i = 0; i != sharedJobData.numVertices; i++)
							{
								tmpPositions[i] += sharedJobData.meshBuffers.vertexPositions[i];
							}
							sharedJobData.meshEdges.ComputeLengths(ref edgeLengths, tmpPositions);
							for (int i = 0; i != sharedJobData.numEquations; i++)
							{
								b[i] = edgeLengths[i];
							}
							break;

						case Param.OutputEdgeCurvature:
							for (int i = 0; i != sharedJobData.numVertices; i++)
							{
								tmpPositions[i] += sharedJobData.meshBuffers.vertexPositions[i];
								tmpNormals[i] += sharedJobData.meshBuffers.vertexNormals[i];
								tmpNormals[i].Normalize();
							}
							sharedJobData.meshEdges.ComputeCurvatures(ref edgeCurvatures, tmpPositions, tmpNormals);
							for (int i = 0; i != sharedJobData.numEquations; i++)
							{
								b[i] = edgeCurvatures[i];
							}
							break;
					}
				}

				// compute x*
				var x = new double[sharedJobData.numVariables];
				{
					switch (sharedJobData.fittingMethod)
					{
						case Method.LinearLeastSquares:
							sharedJobData.At_A_inv_At.Dot(b, x);// stores result in x
							break;

						case Method.NonNegativeLeastSquares:
							{
								var nnlsSolver = new NonNegativeLeastSquares() { MaxIterations = 200, Tolerance = 0.000000001 };
								var nnlsOutput = nnlsSolver.Learn(sharedJobData.A_jagged, b);
								Array.Copy(nnlsOutput.Weights, x, x.Length);
							}
							break;
					}
					//UnityEngine.Debug.Log("frame " + k + ": x* = " + x);
				}

				// remap weights to shape indices
				for (int j = 0; j != sharedJobData.numVariables; j++)
				{
					sharedJobData.frames[k].fittedWeights[sharedJobData.blendShapeIndices[j]] = (float)x[j];
				}
			}
		}
	}
}
