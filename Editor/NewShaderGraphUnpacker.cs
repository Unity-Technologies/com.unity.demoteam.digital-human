using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

#if UNITY_2022_2_OR_NEWER
[InitializeOnLoad]
public class NewShaderGraphUnpacker
{
    static NewShaderGraphUnpacker()
    {
        const string newSGSrcPath = "Packages/com.unity.demoteam.digital-human/ShaderLibrary/.NewShaderGraphs2022/";
        const string newSGDstPath = "Packages/com.unity.demoteam.digital-human/ShaderLibrary/ShaderGraphs2022/";

        DirectoryInfo dirSrc = new DirectoryInfo(Path.GetFullPath(newSGSrcPath));
        DirectoryInfo dirDst = new DirectoryInfo(Path.GetFullPath(newSGDstPath));

        if (dirSrc.Exists && dirDst.Exists && dirDst.GetFiles().Length == 0)
        {
            Debug.LogFormat("Copying New Digital Human shader graphs to {0}", dirDst.FullName);
            Directory.CreateDirectory(newSGDstPath);
            FileInfo[] info = dirSrc.GetFiles("*.*");
            foreach (FileInfo f in info)
            {
                Debug.Log("Copying " + f.FullName);
                f.CopyTo(dirDst.FullName + f.Name, false);
            }
        }
        
    }
}
#endif