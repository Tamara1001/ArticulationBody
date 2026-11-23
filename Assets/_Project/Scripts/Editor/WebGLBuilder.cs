using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Station.EditorTools
{
    /// <summary>Build WebGL lista para subir a itch.io (carpeta Builds/WebGL, ignorada por Git).</summary>
    public static class WebGLBuilder
    {
        public const string OutputPath = "Builds/WebGL";

        [MenuItem("Estación/Build WebGL", priority = 20)]
        public static void Build()
        {
            // Ajustes pensados para itch.io: Gzip con descompresión en el navegador como respaldo
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.defaultWebScreenWidth = 1600;
            PlayerSettings.defaultWebScreenHeight = 900;
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Station.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Build] WebGL {report.summary.result} · {report.summary.totalSize / (1024f * 1024f):0.0} MB · " +
                      $"{report.summary.totalTime.TotalSeconds:0} s · errores {report.summary.totalErrors}");
        }
    }
}
