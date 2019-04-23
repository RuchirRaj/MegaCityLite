#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

class PreventX86BuildProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if(report.summary.platform == BuildTarget.StandaloneWindows)
            Debug.LogError("MegaCity should be built in x86_64 (Build Settings / Architecture)");
    }
}
#endif
