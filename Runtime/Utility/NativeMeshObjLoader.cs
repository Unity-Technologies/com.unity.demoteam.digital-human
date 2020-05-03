//#define VERBOSE

using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	public static class NativeMeshObjLoader
	{
		[Flags]
		public enum VertexAttribs
		{
			Position = 1 << 0,
			TexCoord = 1 << 1,
			Normal = 1 << 2,
		}

		public enum VertexOrder
		{
			ByDefinition = 0,
			ByReference = 1,
		}

		struct InputVertex
		{
			public uint idxPosition;
			public uint idxTexCoord;
			public uint idxNormal;
		}

		struct InputFace
		{
			public InputVertex v0;
			public InputVertex v1;
			public InputVertex v2;
		}

		public unsafe static NativeMeshSOA Parse(string path, VertexAttribs vertexAttribs = VertexAttribs.Position, VertexOrder vertexOrder = VertexOrder.ByDefinition)
		{
#if VERBOSE
			Debug.LogFormat("trying {0}", path);
#endif

			var text = File.ReadAllText(path);//TODO replace with native variant
			var textSize = text.Length;

			// measure the data
			int numPositions = 0;
			int numTexCoords = 0;
			int numNormals = 0;
			int numFaces = 0;

			for (int i = 0; i < text.Length; i++)
			{
				if (ReadChar(text, ref i, 'v'))
				{
					if (ReadBlank(text, ref i))
					{
						numPositions++;
					}
					else if (ReadChar(text, ref i, 't') && ReadBlank(text, ref i))
					{
						numTexCoords++;
					}
					else if (ReadChar(text, ref i, 'n') && ReadBlank(text, ref i))
					{
						numNormals++;
					}
				}
				else if (ReadChar(text, ref i, 'f') && ReadBlankGreedy(text, ref i))
				{
					int readVerts = 0;
					while (ReadDigit(text, ref i))
					{
						ReadUntilNewlineOrBlank(text, ref i);
						ReadBlankGreedy(text, ref i);
						readVerts++;
					}
					if (readVerts > 2)
					{
						numFaces += readVerts - 2;
					}
				}

				ReadUntilNewline(text, ref i);
			}

#if VERBOSE
			Debug.LogFormat("-- numPositions = {0}", numPositions);
			Debug.LogFormat("-- numTexCoords = {0}", numTexCoords);
			Debug.LogFormat("-- numNormals = {0}", numNormals);
			Debug.LogFormat("-- numFaces = {0}", numFaces);
#endif

			// allocate buffers
			var inputPositions = new NativeArray<Vector3>(numPositions, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			var inputTexCoords = new NativeArray<Vector2>(numTexCoords, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			var inputNormals = new NativeArray<Vector3>(numNormals, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			var inputFaces = new NativeArray<InputFace>(numFaces, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

			var outputIndicesMax = numFaces * 3;
			var outputIndicesLUT = new NativeHashMap<Hash128, int>(outputIndicesMax, Allocator.Temp);
			var outputPositions = new NativeArray<Vector3>(outputIndicesMax, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			var outputTexCoords = new NativeArray<Vector2>(outputIndicesMax, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			var outputNormals = new NativeArray<Vector3>(outputIndicesMax, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			var outputIndices = new NativeArray<int>(outputIndicesMax, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

			// read the data
			numPositions = 0;
			numTexCoords = 0;
			numNormals = 0;
			numFaces = 0;

			for (int i = 0; i < text.Length; i++)
			{
				if (ReadChar(text, ref i, 'v'))
				{
					if (ReadBlank(text, ref i))
					{
						Vector3 position;
						ReadFloat(text, ref i, out position.x);
						position.x *= -1.0f;//TODO remove this hack
						ReadBlankGreedy(text, ref i);
						ReadFloat(text, ref i, out position.y);
						ReadBlankGreedy(text, ref i);
						ReadFloat(text, ref i, out position.z);
						inputPositions[numPositions++] = position;
					}
					else if (ReadChar(text, ref i, 't') && ReadBlank(text, ref i))
					{
						Vector2 texCoord;
						ReadFloat(text, ref i, out texCoord.x);
						ReadBlankGreedy(text, ref i);
						ReadFloat(text, ref i, out texCoord.y);
						inputTexCoords[numTexCoords++] = texCoord;
					}
					else if (ReadChar(text, ref i, 'n') && ReadBlank(text, ref i))
					{
						Vector3 normal;
						ReadFloat(text, ref i, out normal.x);
						normal.x *= -1.0f;//TODO remove this hack
						ReadBlankGreedy(text, ref i);
						ReadFloat(text, ref i, out normal.y);
						ReadBlankGreedy(text, ref i);
						ReadFloat(text, ref i, out normal.z);
						inputNormals[numNormals++] = normal;
					}
				}
				else if (ReadChar(text, ref i, 'f') && ReadBlankGreedy(text, ref i))
				{
					InputFace face = new InputFace();
					if (ReadUInt(text, ref i, out face.v0.idxPosition))
					{
						ReadChar(text, ref i, '/');
						ReadUInt(text, ref i, out face.v0.idxTexCoord);
						ReadChar(text, ref i, '/');
						ReadUInt(text, ref i, out face.v0.idxNormal);

						int readVerts = 1;
						while (ReadBlankGreedy(text, ref i))
						{
							face.v1 = face.v2;
							if (ReadUInt(text, ref i, out face.v2.idxPosition))
							{
								ReadChar(text, ref i, '/');
								ReadUInt(text, ref i, out face.v2.idxTexCoord);
								ReadChar(text, ref i, '/');
								ReadUInt(text, ref i, out face.v2.idxNormal);
								if (++readVerts > 2)
								{
									inputFaces[numFaces++] = face;
								}
							}
						}
					}
				}

				ReadUntilNewline(text, ref i);
			}

			// process the data
			int numOutputVertices = 0;
			int numOutputIndices = 0;

			if (vertexOrder == VertexOrder.ByReference)
			{
				for (int i = 0; i != numFaces; i++)
				{
					InputFace face = inputFaces[i];

					var key0 = Hash(in face.v0);
					var key1 = Hash(in face.v1);
					var key2 = Hash(in face.v2);
					int idx0, idx1, idx2;

					if (outputIndicesLUT.TryGetValue(key0, out idx0) == false)
						outputIndicesLUT[key0] = idx0 = numOutputVertices++;
					if (outputIndicesLUT.TryGetValue(key1, out idx1) == false)
						outputIndicesLUT[key1] = idx1 = numOutputVertices++;
					if (outputIndicesLUT.TryGetValue(key2, out idx2) == false)
						outputIndicesLUT[key2] = idx2 = numOutputVertices++;

					outputPositions[idx0] = inputPositions[(int)face.v0.idxPosition - 1];
					outputPositions[idx1] = inputPositions[(int)face.v1.idxPosition - 1];
					outputPositions[idx2] = inputPositions[(int)face.v2.idxPosition - 1];

					outputTexCoords[idx0] = inputTexCoords[(int)face.v0.idxTexCoord - 1];
					outputTexCoords[idx1] = inputTexCoords[(int)face.v1.idxTexCoord - 1];
					outputTexCoords[idx2] = inputTexCoords[(int)face.v2.idxTexCoord - 1];

					outputNormals[idx0] = inputNormals[(int)face.v0.idxNormal - 1];
					outputNormals[idx1] = inputNormals[(int)face.v1.idxNormal - 1];
					outputNormals[idx2] = inputNormals[(int)face.v2.idxNormal - 1];

					outputIndices[numOutputIndices++] = idx0;
					outputIndices[numOutputIndices++] = idx1;
					outputIndices[numOutputIndices++] = idx2;
				}
			}
			else if (vertexOrder == VertexOrder.ByDefinition)
			{
				numOutputVertices = numPositions;

				var indexVisited = new NativeArray<bool>(numPositions, Allocator.Temp, NativeArrayOptions.ClearMemory);

				for (int i = 0; i != numFaces; i++)
				{
					InputFace face = inputFaces[i];

					var key0 = Hash(in face.v0);
					var key1 = Hash(in face.v1);
					var key2 = Hash(in face.v2);
					int idx0, idx1, idx2;

					if (outputIndicesLUT.TryGetValue(key0, out idx0) == false)
					{
						if (indexVisited[idx0 = (int)face.v0.idxPosition - 1])
							outputIndicesLUT[key0] = idx0 = numOutputVertices++;
						else
							outputIndicesLUT[key0] = idx0;
					}

					if (outputIndicesLUT.TryGetValue(key1, out idx1) == false)
					{
						if (indexVisited[idx1 = (int)face.v1.idxPosition - 1])
							outputIndicesLUT[key1] = idx1 = numOutputVertices++;
						else
							outputIndicesLUT[key1] = idx1;
					}

					if (outputIndicesLUT.TryGetValue(key2, out idx2) == false)
					{
						if (indexVisited[idx2 = (int)face.v2.idxPosition - 1])
							outputIndicesLUT[key2] = idx2 = numOutputVertices++;
						else
							outputIndicesLUT[key2] = idx2;
					}

					indexVisited[(int)face.v0.idxPosition - 1] = true;
					indexVisited[(int)face.v1.idxPosition - 1] = true;
					indexVisited[(int)face.v2.idxPosition - 1] = true;

					outputPositions[idx0] = inputPositions[(int)face.v0.idxPosition - 1];
					outputPositions[idx1] = inputPositions[(int)face.v1.idxPosition - 1];
					outputPositions[idx2] = inputPositions[(int)face.v2.idxPosition - 1];

					outputTexCoords[idx0] = inputTexCoords[(int)face.v0.idxTexCoord - 1];
					outputTexCoords[idx1] = inputTexCoords[(int)face.v1.idxTexCoord - 1];
					outputTexCoords[idx2] = inputTexCoords[(int)face.v2.idxTexCoord - 1];

					outputNormals[idx0] = inputNormals[(int)face.v0.idxNormal - 1];
					outputNormals[idx1] = inputNormals[(int)face.v1.idxNormal - 1];
					outputNormals[idx2] = inputNormals[(int)face.v2.idxNormal - 1];

					outputIndices[numOutputIndices++] = idx0;
					outputIndices[numOutputIndices++] = idx1;
					outputIndices[numOutputIndices++] = idx2;
				}

				indexVisited.Dispose();
			}

#if VERBOSE
			Debug.LogFormat("output vertex count = {0}", numOutputVertices);
			Debug.LogFormat("output index count = {0}", numOutputIndices);
#endif

			// copy to container
			NativeMeshSOA mesh = new NativeMeshSOA()
			{
				vertexPositions = new NativeArray<Vector3>(numOutputVertices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
				vertexTexCoords = new NativeArray<Vector2>(numOutputVertices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
				vertexNormals = new NativeArray<Vector3>(numOutputVertices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
				vertexCount = numOutputVertices,

				faceIndices = new NativeArray<int>(numOutputIndices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
				faceIndicesCount = numOutputIndices,
			};

			NativeArray<Vector3>.Copy(outputPositions, mesh.vertexPositions, numOutputVertices);
			NativeArray<Vector2>.Copy(outputTexCoords, mesh.vertexTexCoords, numOutputVertices);
			NativeArray<Vector3>.Copy(outputNormals, mesh.vertexNormals, numOutputVertices);
			NativeArray<int>.Copy(outputIndices, mesh.faceIndices, numOutputIndices);

			// free buffers
			inputPositions.Dispose();
			inputTexCoords.Dispose();
			inputNormals.Dispose();
			inputFaces.Dispose();

			outputIndicesLUT.Dispose();
			outputPositions.Dispose();
			outputTexCoords.Dispose();
			outputNormals.Dispose();
			outputIndices.Dispose();

			// done
			return mesh;
		}

		static Hash128 Hash(in InputVertex v)
		{
			return new Hash128(v.idxPosition, v.idxTexCoord, v.idxNormal, 0);
		}

		static bool ReadChar(string text, ref int index, char value)
		{
			if (text[index] == value)
			{
				index++;
				return true;
			}
			else
			{
				return false;
			}
		}

		static bool ReadDigit(string text, ref int index)
		{
			if (text[index] >= '0' && text[index] <= '9')
			{
				index++;
				return true;
			}
			else
			{
				return false;
			}
		}

		static bool ReadUInt(string text, ref int index, out uint value)
		{
			unsafe
			{
				const uint READ_BASE = '0';
				const uint READ_FAIL = uint.MaxValue;
				const int READ_MAX = 32;

				char* readBuf = stackalloc char[READ_MAX];
				uint readPos = 0;

				if (text[index] >= '0' && text[index] <= '9')
				{
					readBuf[readPos++] = text[index++];
					while (text[index] >= '0' && text[index] <= '9')
					{
						if (readPos == READ_MAX)
						{
							value = READ_FAIL;
							return false;
						}
						readBuf[readPos++] = text[index++];
					}

					value = readBuf[0] - READ_BASE;
					for (uint i = 1; i != readPos; i++)
					{
						value = (value * 10) + (readBuf[i] - READ_BASE);
					}
					return true;
				}
				else
				{
					value = READ_FAIL;
					return false;
				}
			}
		}

		//static int readfloat = 0;
		static bool ReadFloat(string text, ref int index, out float value)
		{
			uint valueInt = 0u;
			uint valueFrac = 0u;

			bool readSign = ReadChar(text, ref index, '-');
			bool readInt = ReadUInt(text, ref index, out valueInt);

			int indexFrac = index;
			bool readFrac = ReadChar(text, ref index, '.') && ReadUInt(text, ref index, out valueFrac);
			int countFrac = index - indexFrac;

			if (readInt || readFrac)
			{
				if (readFrac && valueFrac > 0)
				{
					value = (float)valueFrac;
					value *= Mathf.Pow(10.0f, -(countFrac - 1));
				}
				else
				{
					value = 0.0f;
				}
				if (readInt && valueInt > 0)
				{
					value = ((float)valueInt + value);
				}
				if (readSign)
				{
					value = -value;
				}
				//readfloat++;
				//if (readfloat == 1)
				//{
				//	Debug.Log("read float (" + readfloat + ")");
				//	Debug.Log("-- readSign " + readSign);
				//	Debug.Log("-- readInt " + readInt);
				//	Debug.Log("-- readFrac " + readFrac);
				//	Debug.Log("-- valueInt " + valueInt);
				//	Debug.Log("-- valueFrac " + valueFrac);
				//	Debug.Log("read float VALUE " + value);
				//}
				return true;
			}
			else
			{
				value = float.NaN;
				return false;
			}
		}

		static bool ReadBlank(string text, ref int index)
		{
			if (text[index] == ' ' || text[index] == '\t')
			{
				index++;
				return true;
			}
			else
			{
				return false;
			}
		}

		static bool ReadBlankGreedy(string text, ref int index)
		{
			if (text[index] == ' ' || text[index] == '\t')
			{
				index++;
				while (text[index] == ' ' || text[index] == '\t')
				{
					index++;
				}
				return true;
			}
			else
			{
				return false;
			}
		}

		static bool ReadUntilNewline(string text, ref int index)
		{
			if (text[index] != '\n')
			{
				index++;
				while (text[index] != '\n')
				{
					index++;
				}
				return true;
			}
			else
			{
				return false;
			}
		}

		static bool ReadUntilNewlineOrBlank(string text, ref int index)
		{
			if (text[index] != '\n' && text[index] != ' ' && text[index] != '\t')
			{
				index++;
				while (text[index] != '\n' && text[index] != ' ' && text[index] != '\t')
				{
					index++;
				}
				return true;
			}
			else
			{
				return false;
			}
		}
	}
}
