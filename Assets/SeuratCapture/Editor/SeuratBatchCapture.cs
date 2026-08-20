using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

// Headless entry point to run a Seurat capture from the command line:
//   Unity.exe -batchmode -projectPath <proj> -executeMethod SeuratBatchCapture.Run
//     [-captureOutput <dir>] [-captureSamples <n>] [-captureResolution <n>] -quit
public static class SeuratBatchCapture {
  public static void Run() {
    string[] args = System.Environment.GetCommandLineArgs();
    string output_dir = null;
    int samples = 2;
    int resolution = 512;
    for (int i = 0; i < args.Length - 1; ++i) {
      switch (args[i]) {
        case "-captureOutput": output_dir = args[i + 1]; break;
        case "-captureSamples": samples = int.Parse(args[i + 1]); break;
        case "-captureResolution": resolution = int.Parse(args[i + 1]); break;
      }
    }
    if (string.IsNullOrEmpty(output_dir)) {
      output_dir = Path.Combine(Path.GetTempPath(), "seurat-batch-capture");
    }

    EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

    CaptureHeadbox[] headboxes =
        Object.FindObjectsByType<CaptureHeadbox>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    if (headboxes.Length == 0) {
      Debug.LogError("SeuratBatchCapture: no CaptureHeadbox found in scene.");
      EditorApplication.Exit(1);
      return;
    }
    foreach (CaptureHeadbox h in headboxes) {
      Debug.Log("SeuratBatchCapture: found headbox '" + h.gameObject.name +
                "' active=" + h.gameObject.activeInHierarchy);
    }
    CaptureHeadbox headbox = headboxes[0];
    if (!headbox.gameObject.activeInHierarchy) {
      headbox.gameObject.SetActive(true);
    }
    Debug.Log("SeuratBatchCapture: using headbox on '" + headbox.gameObject.name +
              "' at " + headbox.transform.position + ", camera near=" +
              headbox.ColorCamera.nearClipPlane + " far=" + headbox.ColorCamera.farClipPlane);

    headbox.samples_per_face_ = (PositionSampleCount)samples;
    headbox.center_resolution_ = (CubeFaceResolution)resolution;
    headbox.resolution_ = (CubeFaceResolution)resolution;

    Directory.CreateDirectory(output_dir);
    CaptureBuilder capture = new CaptureBuilder();
    capture.BeginCapture(headbox, output_dir, 1, new CaptureStatus());
    int guard = 0;
    while (!capture.IsCaptureComplete() && guard < samples * 6 + 8) {
      capture.RunCapture();
      ++guard;
    }
    capture.EndCapture();
    Debug.Log("SeuratBatchCapture: done, output in " + output_dir);
  }
}
