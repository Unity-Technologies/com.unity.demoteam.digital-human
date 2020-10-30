using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DemoTeam.DigitalHuman
{
	[CreateAssetMenu(menuName = "Digital Human/Snappers Head Importer", fileName = "MyCharacter_SnappersHeadImporter", order = 50)]
	public class SnappersHeadImporter : ScriptableObject
	{
		const int SB_SIZE = 65536;

		[Header("Source stacks")]
		public List<TextAsset> scriptsResolveControllers;
		public List<TextAsset> scriptsResolveBlendShapes;
		public List<TextAsset> scriptsResolveShaderParam;

		[Header("Source literals")]
		public string namedControllers = "Controllers";
		public string namedBlendShapes = "BlendShapes";
		public string namedShaderParam = "ShaderParam";

		[Header("Output settings")]
		public string csClassPrefix = "MyCharacter";
		public string csNamespace = "MyNamespace";
		public bool csCompacted = true;

		const string compactedControllers = "a";
		const string compactedBlendShapes = "b";
		const string compactedShaderParam = "c";

#if UNITY_EDITOR
		[ContextMenu("Generate")]
		public void Generate()
		{
			var includedControllers = new Dictionary<string, SnappersControllerCaps>();
			var includedBlendShapes = new HashSet<string>();
			var includedShaderParam = new HashSet<string>();

			var indicesControllers = new Dictionary<string, int>();
			var indicesBlendShapes = new Dictionary<string, int>();
			var indicesShaderParam = new Dictionary<string, int>();

			// discover data members
			{
				DiscoverDataMembers(scriptsResolveControllers, includedControllers, includedBlendShapes, includedShaderParam, skipCaps: true);
				DiscoverDataMembers(scriptsResolveBlendShapes, includedControllers, includedBlendShapes, includedShaderParam, skipCaps: false);
				DiscoverDataMembers(scriptsResolveShaderParam, includedControllers, includedBlendShapes, includedShaderParam, skipCaps: false);
			}

			// generate data structures
			{
				GenerateDataStructure("SnappersControllers", indicesControllers, includedControllers.Keys);
				GenerateDataStructure("SnappersBlendShapes", indicesBlendShapes, includedBlendShapes);
				// ................... SnappersShaderParam.. already defined

				GenerateDataStructureIndices<SnappersShaderParam>(indicesShaderParam);
			}

			// generate implementation
			{
				GenerateImplementation(scriptsResolveControllers, scriptsResolveBlendShapes, scriptsResolveShaderParam, includedControllers, indicesControllers, indicesBlendShapes, indicesShaderParam);
			}

			AssetDatabase.Refresh();

			Debug.LogFormat("trying to create: {0}.{1}_SnappersHead", csNamespace, csClassPrefix);
			var scriptInstance = CreateInstance(string.Format("{0}.{1}_SnappersHead", csNamespace, csClassPrefix));
			if (scriptInstance != null)
			{
				var outputDir = GetWritePath();
				var outputPath = string.Format("{0}/{1}_SnappersHead.asset", outputDir, csClassPrefix);

				AssetDatabase.CreateAsset(scriptInstance, outputPath);
				AssetDatabase.SaveAssets();
			}
		}

		public void DiscoverDataMembers(List<TextAsset> scripts, Dictionary<string, SnappersControllerCaps> includedControllers, HashSet<string> includedBlendShapes, HashSet<string> includedShaderParams, bool skipCaps)
		{
			foreach (var script in scripts)
			{
				if (script == null)
					continue;

				DiscoverDataMembers(script, includedControllers, includedBlendShapes, includedShaderParams, skipCaps);
			}
		}

		public void DiscoverDataMembers(TextAsset script, Dictionary<string, SnappersControllerCaps> includedControllers, HashSet<string> includedBlendShapes, HashSet<string> includedShaderParams, bool skipCaps)
		{
			var regexMember = new Regex("([a-zA-Z_]+[\\w]*)\\.([a-zA-Z_]+[\\w]*)");

			var inputPath = AssetDatabase.GetAssetPath(script);
			var inputStream = new StreamReader(inputPath);

			while (inputStream.EndOfStream == false)
			{
				var line = inputStream.ReadLine();
				if (line.Length == 0)
					continue;

				// gather struct.member
				var matches = regexMember.Matches(line);
				{
					// note: this needs to be kept in sync with GenerateEvaluationFunctionSegment
					foreach (Match match in matches)
					{
						var nameStruct = match.Groups[1].Value;
						var nameMember = match.Groups[2].Value;
						if (nameStruct == namedBlendShapes)
						{
							includedBlendShapes.Add(nameMember);
						}
						else if (nameStruct == namedShaderParam)
						{
							includedShaderParams.Add(nameMember);
						}
						else
						{
							SnappersControllerCaps caps;
							includedControllers.TryGetValue(nameStruct, out caps);
							includedControllers[nameStruct] = caps | (skipCaps ? SnappersControllerCaps.none : TranslateControllerField(nameMember));
						}
					}
				}
			}

			inputStream.Close();
		}

		public void GenerateDataStructure(string name, Dictionary<string, int> indices, IEnumerable<string> fields)
		{
			var sb = new StringBuilder(SB_SIZE);

			List<string> sortedFields;
			sortedFields = new List<string>(fields);
			sortedFields.Sort();

			sb.AppendLine("using Unity.DemoTeam.DigitalHuman;");
			sb.AppendLine();
			sb.AppendFormat("namespace {0}\n", csNamespace);
			sb.AppendLine("{");
			sb.AppendFormat("	public struct {0}_{1}<T> where T : struct\n", csClassPrefix, name);
			sb.AppendLine("	{");

			for (int i = 0; i != sortedFields.Count; i++)
			{
				string field = sortedFields[i];

				sb.Append("		public T ");
				sb.Append(field);
				sb.AppendLine(";");

				indices.Add(field, i);
			}

			sb.AppendLine("	}");
			sb.AppendLine("}");

			WriteScriptAsset(string.Format("{0}_{1}", csClassPrefix, name), sb.ToString());
		}

		public void GenerateDataStructureIndices<T>(Dictionary<string, int> indices) where T : struct
		{
			var names = typeof(T).GetFields();

			for (int i = 0; i != names.Length; i++)
			{
				indices.Add(names[i].Name, i);
			}
		}

		public void GenerateImplementation(List<TextAsset> resolveControllers, List<TextAsset> resolveBlendShapes, List<TextAsset> resolveShaderParam, Dictionary<string, SnappersControllerCaps> includedControllers, Dictionary<string, int> indicesControllers, Dictionary<string, int> indicesBlendShapes, Dictionary<string, int> indicesShaderParam)
		{
			var sb = new StringBuilder(SB_SIZE);

			sb.AppendLine("using UnityEngine;");
			sb.AppendLine("using Unity.DemoTeam.DigitalHuman;");
			sb.AppendLine("using Unity.Collections.LowLevel.Unsafe;// for UnsafeUtility.AsRef<T>");
			sb.AppendLine();
			sb.AppendFormat("namespace {0}\n", csNamespace);
			sb.AppendLine("{");
			sb.AppendFormat("	using SnappersControllers = {0}_SnappersControllers<SnappersController>;\n", csClassPrefix);
			sb.AppendFormat("	using SnappersBlendShapes = {0}_SnappersBlendShapes<float>;\n", csClassPrefix);
			sb.AppendLine();
			sb.AppendFormat("	public class {0}_{1}\n", csClassPrefix, "SnappersHead : SnappersHeadDefinition");
			sb.AppendLine("	{");
			sb.AppendLine("		public override InstanceData CreateInstanceData(Mesh sourceMesh, Transform sourceRig, Warnings warnings)");
			sb.AppendLine("		{");
			sb.AppendLine("			return CreateInstanceData<SnappersControllers, SnappersBlendShapes>(sourceMesh, sourceRig, warnings);");
			sb.AppendLine("		}");
			sb.AppendLine();
			sb.AppendLine("		bool CheckSizes()");
			sb.AppendLine("		{");
			unsafe
			{
				sb.AppendFormat("			const int INDEXED_SIZE_CONTROLLERS = {0};\n", indicesControllers.Count * sizeof(SnappersController));
				sb.AppendFormat("			const int INDEXED_SIZE_BLENDSHAPES = {0};\n", indicesBlendShapes.Count * sizeof(float));
				sb.AppendFormat("			const int INDEXED_SIZE_SHADERPARAM = {0};\n", indicesShaderParam.Count * sizeof(float));
			}
			sb.AppendLine();
			sb.AppendLine("			return");
			sb.AppendLine("				INDEXED_SIZE_CONTROLLERS <= UnsafeUtility.SizeOf<SnappersControllers>() &&");
			sb.AppendLine("				INDEXED_SIZE_BLENDSHAPES <= UnsafeUtility.SizeOf<SnappersBlendShapes>() &&");
			sb.AppendLine("				INDEXED_SIZE_SHADERPARAM <= UnsafeUtility.SizeOf<SnappersShaderParam>();");
			sb.AppendLine("		}");
			sb.AppendLine();
			GenerateEvaluationFunctionCallsite(sb, "		", "ResolveControllers");
			sb.AppendLine();
			GenerateEvaluationFunctionCallsite(sb, "		", "ResolveBlendShapes");
			sb.AppendLine();
			GenerateEvaluationFunctionCallsite(sb, "		", "ResolveShaderParam");
			sb.AppendLine();
			GenerateInitializeControllerCapsCallsite(sb, "		", "InitializeControllerCaps");
			sb.AppendLine("	}");
			sb.AppendLine("}");

			WriteScriptAsset(string.Format("{0}_{1}", csClassPrefix, "SnappersHead"), sb.ToString());

			sb.Clear();

			sb.AppendLine("#pragma warning disable 0219");

			if (csCompacted == false)
			{
				sb.AppendLine();
				sb.AppendLine("using Unity.DemoTeam.DigitalHuman;");
			}

			sb.AppendLine();
			sb.AppendFormat("namespace {0}\n", csNamespace);
			sb.AppendLine("{");

			if (csCompacted == false)
			{
				sb.AppendFormat("	using SnappersControllers = {0}_SnappersControllers<SnappersController>;\n", csClassPrefix);
				sb.AppendFormat("	using SnappersBlendShapes = {0}_SnappersBlendShapes<float>;\n", csClassPrefix);
				sb.AppendLine();
			}

			sb.AppendFormat("	public static class {0}_{1}\n", csClassPrefix, "SnappersHeadImpl");
			sb.AppendLine("	{");
			GenerateEvaluationFunction(sb, "		", "ResolveControllers", resolveControllers, includedControllers, indicesControllers, indicesBlendShapes, indicesShaderParam);
			sb.AppendLine();
			GenerateEvaluationFunction(sb, "		", "ResolveBlendShapes", resolveBlendShapes, includedControllers, indicesControllers, indicesBlendShapes, indicesShaderParam);
			sb.AppendLine();
			GenerateEvaluationFunction(sb, "		", "ResolveShaderParam", resolveShaderParam, includedControllers, indicesControllers, indicesBlendShapes, indicesShaderParam);
			sb.AppendLine();
			GenerateInitializeControllerCaps(sb, "		", "InitializeControllerCaps", includedControllers, indicesControllers);
			sb.AppendLine();
			sb.AppendLine(@"		static float clamp(float value, float min, float max)
		{
			if (value < min)
				return min;
			else if (value > max)
				return max;
			else
				return value;
		}

		static float min(float a, float b)
		{
			if (a < b)
				return a;
			else
				return b;
		}

		static float max(float a, float b)
		{
			if (a > b)
				return a;
			else
				return b;
		}

		static float hermite(float p0, float p1, float r0, float r1, float t)
		{
			float t2 = t * t;
			float t3 = t2 * t;
			float _3t2 = 3.0f * t2;
			float _2t3 = 2.0f * t3;
			return (p0 * (_2t3 - _3t2 + 1.0f) + p1 * (-_2t3 + _3t2) + r0 * (t3 - 2.0f * t2 + t) + r1 * (t3 - t2));
		}

		static float linstep(float a, float b, float value)
		{
			if (a != b)
				return clamp((value - a) / (b - a), 0.0f, 1.0f);
			else
				return 0.0f;
		}
");
			sb.AppendLine("	}");
			sb.AppendLine("}");

			WriteScriptAsset(string.Format("{0}_{1}", csClassPrefix, "SnappersHeadImpl"), sb.ToString());
		}

		public void GenerateEvaluationFunctionCallsite(StringBuilder sb, string tabs, string name)
		{
			sb.AppendFormat("		public override unsafe void {0}(void* ptrSnappersControllers, void* ptrSnappersBlendShapes, void* ptrSnappersShaderParam)\n", name);
			sb.AppendLine("		{");
			sb.AppendLine("			if (!CheckSizes())");
			sb.AppendLine("				return;");
			sb.AppendLine();
			sb.AppendFormat("			{0}_SnappersHeadImpl.{1}(\n", csClassPrefix, name);

			if (csCompacted)
			{
				sb.AppendLine("				(float*)ptrSnappersControllers,");
				sb.AppendLine("				(float*)ptrSnappersBlendShapes,");
				sb.AppendLine("				(float*)ptrSnappersShaderParam");
			}
			else
			{
				sb.AppendLine("#if UNITY_2020_1_OR_NEWER");
				sb.AppendLine("				ref UnsafeUtility.AsRef<SnappersControllers>(ptrSnappersControllers),");
				sb.AppendLine("				ref UnsafeUtility.AsRef<SnappersBlendShapes>(ptrSnappersBlendShapes),");
				sb.AppendLine("				ref UnsafeUtility.AsRef<SnappersShaderParam>(ptrSnappersShaderParam)");
				sb.AppendLine("#else");
				sb.AppendLine("				ref UnsafeUtilityEx.AsRef<SnappersControllers>(ptrSnappersControllers),");
				sb.AppendLine("				ref UnsafeUtilityEx.AsRef<SnappersBlendShapes>(ptrSnappersBlendShapes),");
				sb.AppendLine("				ref UnsafeUtilityEx.AsRef<SnappersShaderParam>(ptrSnappersShaderParam)");
				sb.AppendLine("#endif");
			}

			sb.AppendLine("			);");
			sb.AppendLine("		}");
		}

		public void GenerateEvaluationFunction(StringBuilder sb, string tabs, string name, List<TextAsset> scripts, Dictionary<string, SnappersControllerCaps> includedControllers, Dictionary<string, int> indicesControllers, Dictionary<string, int> indicesBlendShapes, Dictionary<string, int> indicesShaderParam)
		{
			if (csCompacted)
			{
				sb.AppendFormat("		public static unsafe void {0}(float* {1}, float* {2}, float* {3})\n", name, compactedControllers, compactedBlendShapes, compactedShaderParam);
			}
			else
			{
				sb.AppendFormat("		public static void {0}(ref SnappersControllers {1}, ref SnappersBlendShapes {2}, ref SnappersShaderParam {3})\n", name, namedControllers, namedBlendShapes, namedShaderParam);
			}

			sb.AppendLine("		{");

			foreach (var script in scripts)
			{
				if (script == null)
					continue;

				if (csCompacted == false)
				{
					sb.AppendFormat("			// this segment generated from '{0}'\n", script.name);
				}

				sb.AppendLine("			{");
				GenerateEvaluationFunctionSegment(sb, "				", script, includedControllers, indicesControllers, indicesBlendShapes, indicesShaderParam);
				sb.AppendLine("			}");
			}

			sb.AppendLine("		}");
		}

		public void GenerateEvaluationFunctionSegment(StringBuilder sb, string tabs, TextAsset script, Dictionary<string, SnappersControllerCaps> includedControllers, Dictionary<string, int> indicesControllers, Dictionary<string, int> indicesBlendShapes, Dictionary<string, int> indicesShaderParam)
		{
			var regexDouble = new Regex("(\\d+\\.\\d+)([^f])");
			var regexSymbol = new Regex("\\$([_a-zA-Z][_a-zA-Z0-9]*)");
			var regexMember = new Regex("([a-zA-Z_]+[\\w]*)\\.([a-zA-Z_]+[\\w]*)");

			var symbolCount = 0;
			var symbolTable = new Dictionary<string, string>();

			var inputPath = AssetDatabase.GetAssetPath(script);
			var inputStream = new StreamReader(inputPath);

			while (inputStream.EndOfStream == false)
			{
				var line = inputStream.ReadLine();
				if (line.Length == 0)
					continue;

				// remove comments
				if (csCompacted)
				{
					var commentIndex = line.IndexOf("//");
					if (commentIndex != -1)
						line = line.Substring(0, commentIndex);
				}

				// remove blanks
				line = line.Trim();
				if (line.Length == 0)
					continue;

				// insert tabs
				sb.Append(tabs);

				// replace $ with _
				if (csCompacted)
				{
					Match match;
					while ((match = regexSymbol.Match(line)).Success)
					{
						var symbol = match.Groups[0].Value;
						var symbolCompact = null as string;
						if (!symbolTable.TryGetValue(symbol, out symbolCompact))
						{
							symbolCompact = "_s" + (++symbolCount);
							symbolTable.Add(symbol, symbolCompact);
						}
						line = line.Replace(symbol, symbolCompact);
					}
				}
				else
				{
					line = line.Replace('$', '_');
				}

				// replace 0.0 with 0.0f
				line = regexDouble.Replace(line, "$1f$2");

				// gather and replace struct.member
				var matches = regexMember.Matches(line);
				{
					int lineCursor = 0;

					// note: this needs to be kept in sync with DiscoverDataMembers
					foreach (Match match in matches)
					{
						// add everything until match
						sb.Append(line, lineCursor, match.Index - lineCursor);

						var nameStruct = match.Groups[1].Value;
						var nameMember = match.Groups[2].Value;
						if (nameStruct == namedBlendShapes)
						{
							// e.g. Head_blendShape.nameMember
							if (csCompacted)
							{
								sb.Append(compactedBlendShapes);
								sb.Append('[');
								sb.Append(indicesBlendShapes[nameMember]);
								sb.Append(']');
							}
						}
						else if (nameStruct == namedShaderParam)
						{
							// e.g. SkinShader.nameMember
							if (csCompacted)
							{
								sb.Append(compactedShaderParam);
								sb.Append('[');
								sb.Append(indicesShaderParam[nameMember]);
								sb.Append(']');
							}
						}
						else
						{
							// e.g. nameStruct.nameMember
							if (csCompacted)
							{
								unsafe
								{
									sb.Append(compactedControllers);
									sb.Append('[');
									sb.Append(indicesControllers[nameStruct] * (sizeof(SnappersController) / sizeof(float)) + IndexControllerField(nameMember));
									sb.Append(']');
								}
							}
							else
							{
								sb.Append(namedControllers);
								sb.Append('.');
							}
						}

						if (csCompacted)
							lineCursor = match.Index + match.Length;
						else
							lineCursor = match.Index;
					}

					sb.Append(line, lineCursor, line.Length - lineCursor);
				}

				// add newline
				sb.Append('\n');
			}

			inputStream.Close();
		}

		public void GenerateInitializeControllerCapsCallsite(StringBuilder sb, string tabs, string name)
		{
			sb.AppendFormat("		public override unsafe void {0}(void* ptrSnappersControllers)\n", name);
			sb.AppendLine("		{");
			sb.AppendFormat("			{0}_SnappersHeadImpl.{1}(\n", csClassPrefix, name);

			if (csCompacted)
			{
				sb.AppendLine("				(uint*)ptrSnappersControllers");
			}
			else
			{
				sb.AppendLine("#if UNITY_2020_1_OR_NEWER");
				sb.AppendLine("				ref UnsafeUtility.AsRef<SnappersControllers>(ptrSnappersControllers)");
				sb.AppendLine("#else");
				sb.AppendLine("				ref UnsafeUtilityEx.AsRef<SnappersControllers>(ptrSnappersControllers)");
				sb.AppendLine("#endif");
			}

			sb.AppendLine("			);");
			sb.AppendLine("		}");
		}

		public void GenerateInitializeControllerCaps(StringBuilder sb, string tabs, string name, Dictionary<string, SnappersControllerCaps> includedControllers, Dictionary<string, int> indicesControllers)
		{
			Func<StringBuilder, int, SnappersControllerCaps, SnappersControllerCaps, int> concatCap = (_sb, _capCount, _caps, _cap) =>
			{
				if ((_caps & _cap) != 0)
				{
					if (_capCount++ > 0)
					{
						sb.Append(" | ");
					}
					_sb.Append("SnappersControllerCaps.");
					_sb.Append(_cap);
				}
				return _capCount;
			};

			if (csCompacted)
			{
				sb.AppendFormat("		public static unsafe void {0}(uint* {1})\n", name, compactedControllers);
			}
			else
			{
				sb.AppendFormat("		public static void {0}(ref SnappersControllers {1})\n", name, namedControllers);
			}

			sb.AppendLine("		{");

			List<string> sortedFields;
			sortedFields = new List<string>(includedControllers.Keys);
			sortedFields.Sort();

			foreach (var field in sortedFields)
			{
				if (csCompacted)

				{
					unsafe
					{
						sb.AppendFormat("			{0}[{1}] = {2};\n", compactedControllers, indicesControllers[field] * (sizeof(SnappersController) / sizeof(float)) + 9, (int)includedControllers[field]);
					}
				}
				else
				{
					sb.AppendFormat("			{0}.{1}.caps = ", namedControllers, field);

					var caps = includedControllers[field];
					var capCount = 0;

					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.translateX);
					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.translateY);
					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.translateZ);
					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.rotateX);
					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.rotateY);
					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.rotateZ);
					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.scaleX);
					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.scaleY);
					capCount = concatCap(sb, capCount, caps, SnappersControllerCaps.scaleZ);

					if (capCount == 0)
						sb.AppendLine("SnappersControllerCaps.none;");
					else
						sb.AppendLine(";");
				}
			}

			sb.AppendLine("		}");
		}

		public string GetWritePath()
		{
			return Path.GetDirectoryName(AssetDatabase.GetAssetPath(this)).Replace('\\', '/');
		}

		public string WriteScriptAsset(string identifier, string text)
		{
			var outputDir = GetWritePath();
			var outputPath = string.Format("{0}/{1}.cs", outputDir, identifier);
			var outputStream = new StreamWriter(outputPath);

			outputStream.NewLine = "\n";
			outputStream.Write(text.Replace("\r\n", "\n"));
			outputStream.Flush();
			outputStream.Close();

			Debug.LogFormat("Wrote {0}", outputPath);

			return outputPath;
		}

		static SnappersControllerCaps TranslateControllerField(string field)
		{
			switch (field)
			{
				case "translateX": return SnappersControllerCaps.translateX;
				case "translateY": return SnappersControllerCaps.translateY;
				case "translateZ": return SnappersControllerCaps.translateZ;

				case "rotateX": return SnappersControllerCaps.rotateX;
				case "rotateY": return SnappersControllerCaps.rotateY;
				case "rotateZ": return SnappersControllerCaps.rotateZ;

				case "scaleX": return SnappersControllerCaps.scaleX;
				case "scaleY": return SnappersControllerCaps.scaleY;
				case "scaleZ": return SnappersControllerCaps.scaleZ;

				default: return SnappersControllerCaps.none;
			}
		}

		static int IndexControllerField(string field)
		{
			switch (field)
			{
				case "translateX": return 0;
				case "translateY": return 1;
				case "translateZ": return 2;

				case "rotateX": return 3;
				case "rotateY": return 4;
				case "rotateZ": return 5;

				case "scaleX": return 6;
				case "scaleY": return 7;
				case "scaleZ": return 8;

				default: return -1;
			}
		}
#endif
	}
}
