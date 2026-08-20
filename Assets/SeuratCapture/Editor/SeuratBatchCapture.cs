using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

// Headless entry point to run a Seurat capture from the command line:
//
//   Unity.exe -batchmode -projectPath <project>
//             -executeMethod SeuratBatchCapture.Run
//             [-captureScene Assets/Scenes/MyScene.unity]
//             [-captureHeadbox <GameObject name>]
//             [-captureOutput <folder>]
//             [-captureSamples <n>] [-captureResolution <n>]
//             [-captureCenterResolution <n>]
//             -quit
//
// Every argument is optional: without -captureScene the scene already open in
// the project is captured, and without -captureHeadbox the first CaptureHeadbox
// found in it is used. The capture settings default to the ones stored on the
// component, so a scene set up in the Editor bakes identically from a script.
//
// Do NOT pass -nographics: the capture renders with the actual graphics device.
public static class SeuratBatchCapture {
  public static void Run() {
    string[] args = System.Environment.GetCommandLineArgs();
    string scene_path = null;
    string headbox_name = null;
    string output_dir = null;
    int samples = 0;
    int resolution = 0;
    int center_resolution = 0;
    for (int i = 0; i < args.Length - 1; ++i) {
      switch (args[i]) {
        case "-captureScene": scene_path = args[i + 1]; break;
        case "-captureHeadbox": headbox_name = args[i + 1]; break;
        case "-captureOutput": output_dir = args[i + 1]; break;
        case "-captureSamples": samples = int.Parse(args[i + 1]); break;
        case "-captureResolution": resolution = int.Parse(args[i + 1]); break;
        case "-captureCenterResolution":
          center_resolution = int.Parse(args[i + 1]);
          break;
      }
    }
    if (string.IsNullOrEmpty(output_dir)) {
      output_dir = Path.Combine(Path.GetTempPath(), "seurat-batch-capture");
    }

    if (!string.IsNullOrEmpty(scene_path)) {
      if (!File.Exists(scene_path)) {
        Fail("scene not found: " + scene_path);
        return;
      }
      EditorSceneManager.OpenScene(scene_path);
    }

    CaptureHeadbox headbox = FindHeadbox(headbox_name);
    if (headbox == null) {
      Fail(string.IsNullOrEmpty(headbox_name)
             ? "no CaptureHeadbox in the open scene."
             : "no CaptureHeadbox named '" + headbox_name + "' in the open scene.");
      return;
    }
    if (!headbox.gameObject.activeInHierarchy) {
      headbox.gameObject.SetActive(true);
    }
    Debug.Log("SeuratBatchCapture: using headbox on '" + headbox.gameObject.name +
              "' at " + headbox.transform.position + ", camera near=" +
              headbox.ColorCamera.nearClipPlane + " far=" +
              headbox.ColorCamera.farClipPlane);

    // Only override what the command line actually asked for; the rest stays
    // as authored on the component.
    if (samples > 0) {
      headbox.samples_per_face_ = (PositionSampleCount)samples;
    }
    if (resolution > 0) {
      headbox.resolution_ = (CubeFaceResolution)resolution;
      // The center view is the one Seurat needs at the highest resolution, so
      // it follows the requested resolution unless it is given separately.
      headbox.center_resolution_ = (CubeFaceResolution)resolution;
    }
    if (center_resolution > 0) {
      headbox.center_resolution_ = (CubeFaceResolution)center_resolution;
    }

    Directory.CreateDirectory(output_dir);
    CaptureBuilder capture = new CaptureBuilder();
    capture.BeginCapture(headbox, output_dir, 1, new CaptureStatus());
    // One RunCapture() renders one cube face, so a full capture takes six per
    // headbox sample. The guard only exists to turn a state machine that stops
    // advancing into a failed exit code instead of an infinite batch job.
    int faces = 6 * (int)headbox.samples_per_face_;
    int guard = faces + 8;
    while (!capture.IsCaptureComplete() && guard > 0) {
      capture.RunCapture();
      --guard;
    }
    bool complete = capture.IsCaptureComplete();
    capture.EndCapture();
    if (!complete) {
      Fail("capture did not finish after " + (faces + 8) + " faces.");
      return;
    }
    Debug.Log("SeuratBatchCapture: done, output in " + output_dir);
  }

  private static CaptureHeadbox FindHeadbox(string headbox_name) {
    CaptureHeadbox[] headboxes = Object.FindObjectsByType<CaptureHeadbox>(
      FindObjectsInactive.Include, FindObjectsSortMode.None);
    foreach (CaptureHeadbox candidate in headboxes) {
      Debug.Log("SeuratBatchCapture: found headbox '" +
                candidate.gameObject.name + "' active=" +
                candidate.gameObject.activeInHierarchy);
    }
    if (string.IsNullOrEmpty(headbox_name)) {
      return headboxes.Length > 0 ? headboxes[0] : null;
    }
    foreach (CaptureHeadbox candidate in headboxes) {
      if (candidate.gameObject.name == headbox_name) {
        return candidate;
      }
    }
    return null;
  }

  private static void Fail(string message) {
    Debug.LogError("SeuratBatchCapture: " + message);
    EditorApplication.Exit(1);
  }
}
