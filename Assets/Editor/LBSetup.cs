using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LB.EditorTools
{
    /// <summary>
    /// Configuracion automatica del proyecto la primera vez que se abre:
    /// crea Assets/Scenes/Main.unity, la anade al build y ajusta el Player para Windows.
    /// Menu: LilBombards/...
    /// </summary>
    [InitializeOnLoad]
    public static class LBSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string BuildPath = "Builds/Windows/LilBombards.exe";

        static LBSetup()
        {
            EditorApplication.delayCall += AutoSetup;
        }

        static void AutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) Setup();
        }

        [MenuItem("LilBombards/Configurar proyecto")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("LilBombards").AddComponent<GameManager>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "Lil Bombards";
            PlayerSettings.companyName = "LilBombards";
            PlayerSettings.bundleVersion = Net.GameVersion;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LilBombards] Proyecto configurado. Abre " + ScenePath + " y pulsa Play.");
        }

        [MenuItem("LilBombards/Compilar para Windows (x64)")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) Setup();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[LilBombards] Build: " + report.summary.result + " -> " + Path.GetFullPath(BuildPath));
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorUtility.RevealInFinder(BuildPath);
        }

        [MenuItem("LilBombards/Abrir escena principal")]
        public static void OpenMain()
        {
            if (!File.Exists(ScenePath)) Setup();
            EditorSceneManager.OpenScene(ScenePath);
        }
    }

    /// <summary>
    /// Ajustes de importacion para los FBX exportados desde Blender a Assets/Resources/Models:
    /// mallas legibles (el juego las normaliza en tiempo de ejecucion) y conversion de ejes horneada.
    /// </summary>
    public class LBModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("Resources/Models")) return;
            var mi = (ModelImporter)assetImporter;
            mi.isReadable = true;
            mi.bakeAxisConversion = true;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.globalScale = 1f;
            mi.useFileScale = true;
        }
    }
}
