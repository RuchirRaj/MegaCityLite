using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

// Editor class to let us have an additional HDRenderPipelineAsset with settings specific to the consoles,
// mainly to allow improved performance at the cost of some quality compared to the desktop settings used in the main
// asset. RenderPipeline asset is switched at build time if we are building for console and then switched back after the
// build has finished.
[InitializeOnLoad]
public class RenderPipeAssetSwitcher : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;
    static RenderPipelineAsset s_PreBuildAsset;

    static RenderPipeAssetSwitcher()
    {
        BuildPlayerWindow.RegisterBuildPlayerHandler(OnPreprocessBuild);
    }

    public static void OnPreprocessBuild(BuildPlayerOptions options)
    {
        // Store the asset being used prior to the build
        s_PreBuildAsset = GraphicsSettings.renderPipelineAsset;
        var assetPath = "";

        // Add each platform you want to override here
        switch (options.target)
        {
            case BuildTarget.PS4:
            case BuildTarget.XboxOne:
                assetPath = "Assets/Settings/HDRenderPipelineAsset_Console.asset";
                break;
        }

        if (assetPath != string.Empty) // No need to override if we haven't got anything
        {
            if (s_PreBuildAsset != null)
                Debug.LogFormat("Overriding Render Pipeline Asset '{0}' for build target {1} with '{2}'", s_PreBuildAsset.name, options.target, assetPath);
            else
                Debug.LogFormat("Setting Render Pipeline Asset for build target {0} to '{1}'", options.target, assetPath);

            GraphicsSettings.renderPipelineAsset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(assetPath);
        }

        BuildPipeline.BuildPlayer(options);
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        // Revert the Graphics Setting asset to what it was pre-build (or null, if nothing has been stored)
        GraphicsSettings.renderPipelineAsset = s_PreBuildAsset ? s_PreBuildAsset : null;

        if(GraphicsSettings.renderPipelineAsset == null)
            Debug.LogWarningFormat("GraphicsSettings.renderPipelineAsset has been set to null! Please ensure this wasn't done in error.");
    }
}
