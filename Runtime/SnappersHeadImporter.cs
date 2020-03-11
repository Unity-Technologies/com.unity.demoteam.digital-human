using System;
using System.Collections.Generic;
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

#if UNITY_EDITOR
		[ContextMenu("Generate")]
		public void Generate()
		{
			var includedControllers = new Dictionary<string, SnappersControllerCaps>();
			var includedBlendShapes = new HashSet<string>();
			var includedShaderParam = new HashSet<string>();

			// generate the evaluation functions
			{
				GenerateEvaluationFunctions(scriptsResolveControllers, scriptsResolveBlendShapes, scriptsResolveShaderParam, includedControllers, includedBlendShapes, includedShaderParam);
			}

			// generate the data structures
			{
				GenerateDataStructure("SnappersControllers", includedControllers.Keys);
				GenerateDataStructure("SnappersBlendShapes", includedBlendShapes);
				// ................... SnappersShaderParam.. already defined
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

		public void GenerateEvaluationFunctions(List<TextAsset> resolveControllers, List<TextAsset> resolveBlendShapes, List<TextAsset> resolveShaderParam, Dictionary<string, SnappersControllerCaps> includedControllers, HashSet<string> includedBlendShapes, HashSet<string> includedShaderParam)
		{
			var sb = new StringBuilder(SB_SIZE);

			sb.AppendLine("using UnityEngine;");
			sb.AppendLine("using Unity.DemoTeam.DigitalHuman;");
			sb.AppendLine("using Unity.Collections.LowLevel.Unsafe;// for UnsafeUtilityEx.AsRef<T>");
			sb.AppendLine();
			sb.AppendLine("using static Unity.DemoTeam.DigitalHuman.SnappersHeadDefinitionMath;");
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

			sb.AppendLine("#pragma warning disable 0219");

			GenerateEvaluationFunction(sb, "		", "ResolveControllers", resolveControllers, includedControllers, includedBlendShapes, includedShaderParam, skipCaps: true);
			sb.AppendLine();
			GenerateEvaluationFunction(sb, "		", "ResolveBlendShapes", resolveBlendShapes, includedControllers, includedBlendShapes, includedShaderParam, skipCaps: false);
			sb.AppendLine();
			GenerateEvaluationFunction(sb, "		", "ResolveShaderParam", resolveShaderParam, includedControllers, includedBlendShapes, includedShaderParam, skipCaps: false);
			sb.AppendLine();
			GenerateInitializeControllerCaps(sb, "		", "InitializeControllerCaps", includedControllers);

			sb.AppendLine("#pragma warning restore 0219");

			sb.AppendLine("	}");
			sb.AppendLine("}");

			WriteScriptAsset(string.Format("{0}_{1}", csClassPrefix, "SnappersHead"), sb.ToString());
		}

		public void GenerateEvaluationFunction(StringBuilder sb, string tabs, string name, List<TextAsset> scripts, Dictionary<string, SnappersControllerCaps> includedControllers, HashSet<string> includedBlendShapes, HashSet<string> includedShaderParam, bool skipCaps)
		{
			sb.AppendFormat("		// --- {0}\n", name);
			sb.AppendFormat("		public override unsafe void {0}(void* ptrSnappersControllers, void* ptrSnappersBlendShapes, void* ptrSnappersShaderParam)\n", name);
			sb.AppendLine("		{");
			sb.AppendFormat("			{0}(\n", name);
			sb.AppendLine("				ref UnsafeUtilityEx.AsRef<SnappersControllers>(ptrSnappersControllers),");
			sb.AppendLine("				ref UnsafeUtilityEx.AsRef<SnappersBlendShapes>(ptrSnappersBlendShapes),");
			sb.AppendLine("				ref UnsafeUtilityEx.AsRef<SnappersShaderParam>(ptrSnappersShaderParam)");
			sb.AppendLine("			);");
			sb.AppendLine("		}");
			sb.AppendFormat("		public void {0}(ref SnappersControllers {1}, ref SnappersBlendShapes {2}, ref SnappersShaderParam {3})\n", name, namedControllers, namedBlendShapes, namedShaderParam);
			sb.AppendLine("		{");

			foreach (var script in scripts)
			{
				if (script == null)
					continue;

				sb.AppendFormat("			// this segment generated from '{0}'\n", script.name);
				sb.AppendLine("			{");
				GenerateEvaluationFunctionSegment(sb, "				", script, includedControllers, includedBlendShapes, includedShaderParam, skipCaps);
				sb.AppendLine("			}");
			}

			sb.AppendLine("		}");
		}

		public void GenerateEvaluationFunctionSegment(StringBuilder sb, string tabs, TextAsset script, Dictionary<string, SnappersControllerCaps> includedControllers, HashSet<string> includedBlendShapes, HashSet<string> includedShaderParams, bool skipCaps)
		{
			var regexDouble = new Regex("(\\d+\\.\\d+)([^f])");
			var regexMember = new Regex("([a-zA-Z_]+[\\w]*)\\.([a-zA-Z_]+[\\w]*)");

			var inputPath = AssetDatabase.GetAssetPath(script);
			var inputStream = new StreamReader(inputPath);

			while (inputStream.EndOfStream == false)
			{
				var line = inputStream.ReadLine();
				if (line.Length == 0)
					continue;

				// add tabs
				sb.Append(tabs);

				// replace $ with _
				line = line.Replace('$', '_');

				// replace eeg. 0.0 with 0.0f
				line = regexDouble.Replace(line, "$1f$2");

				// replace and gather struct.member
				var matches = regexMember.Matches(line);
				{
					int lineCursor = 0;

					foreach (Match match in matches)
					{
						sb.Append(line, lineCursor, match.Index - lineCursor);

						var nameStruct = match.Groups[1].Value;
						var nameMember = match.Groups[2].Value;
						if (nameStruct == namedBlendShapes)
						{
							includedBlendShapes.Add(nameMember);
						}
						//else if (nameStruct == namedControllers)
						//{
						//	includedControllers.Add(nameMember);
						//}
						else if (nameStruct == namedShaderParam)
						{
							includedShaderParams.Add(nameMember);
						}
						else// controller
						{
							SnappersControllerCaps caps;
							includedControllers.TryGetValue(nameStruct, out caps);
							includedControllers[nameStruct] = caps | (skipCaps ? SnappersControllerCaps.none : TranslateControllerField(nameMember));
							sb.Append(namedControllers);
							sb.Append('.');
						}

						lineCursor = match.Index;
					}

					sb.Append(line, lineCursor, line.Length - lineCursor);
				}

				// add newline
				sb.Append('\n');
			}

			inputStream.Close();
		}

		public void GenerateInitializeControllerCaps(StringBuilder sb, string tabs, string name, Dictionary<string, SnappersControllerCaps> includedControllers)
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

			sb.AppendFormat("		// --- {0}\n", name);
			sb.AppendFormat("		public override unsafe void {0}(void* ptrSnappersControllers)\n", name);
			sb.AppendLine("		{");
			sb.AppendFormat("			{0}(\n", name);
			sb.AppendLine("				ref UnsafeUtilityEx.AsRef<SnappersControllers>(ptrSnappersControllers)");
			sb.AppendLine("			);");
			sb.AppendLine("		}");
			sb.AppendFormat("		public void {0}(ref SnappersControllers {1})\n", name, namedControllers);
			sb.AppendLine("		{");

			List<string> sortedFields;
			sortedFields = new List<string>(includedControllers.Keys);
			sortedFields.Sort();

			foreach (var field in sortedFields)
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

			sb.AppendLine("		}");
		}

		public void GenerateDataStructure(string name, IEnumerable<string> fields)
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

			foreach (var field in sortedFields)
			{
				sb.Append("		public T ");
				sb.Append(field);
				sb.AppendLine(";");
			}

			sb.AppendLine("	}");
			sb.AppendLine("}");

			WriteScriptAsset(string.Format("{0}_{1}", csClassPrefix, name), sb.ToString());
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
#endif
	}
}
