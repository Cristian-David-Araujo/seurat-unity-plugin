/*
Copyright 2017 Google Inc. All Rights Reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;

// Reflects status updates back to CaptureWindow, and allows CaptureWindow to
// notify capture/baking tasks to cancel.
class EditorBakeStatus : CaptureStatus
{
  bool task_cancelled_ = false;
  CaptureWindow bake_gui_;

  public override void SendProgress(string message, float fraction_complete)
  {
    bake_gui_.SetProgressBar(message, fraction_complete);
  }

  public override bool TaskContinuing()
  {
    return !task_cancelled_;
  }

  public void SetGUI(CaptureWindow bake_gui) { bake_gui_ = bake_gui; }

  public void CancelTask()
  {
    Debug.Log("User canceled capture processing.");
    task_cancelled_ = true;
  }
}

// Provides an interactive modeless GUI during the capture and bake process.
class CaptureWindow : EditorWindow
{
  // Defines a state machine flow for the capture and bake process.
  enum BakeStage {
    kInitialization,
    kCapture,
    // This stage indicates the GUI is waiting for user to dismiss the window
    // by pressing a "Done" button.
    kWaitForDoneButton,
    kComplete,
  }

  const float kTimerInterval = 0.016f;
  const int kTimerExpirationsPerCapture = 4;

  float last_time_;
  float ui_timer_ = 0.25f;
  int capture_timer_;

  string progress_message_;
  float progress_complete_;
  // The headbox component receives notification that capture is complete to
  // update the Inspector GUI, e.g. unlock the Capture button.
  CaptureHeadbox capture_notification_component_;
  CaptureBuilder monitored_capture_;
  EditorBakeStatus capture_status_;

  BakeStage bake_stage_ = BakeStage.kInitialization;

  public void SetupStatus(EditorBakeStatus capture_status)
  {
    capture_status_ = capture_status;
    capture_status_.SetGUI(this);
  }

  public void SetupCaptureProcess(CaptureHeadbox capture_notification_component,
    CaptureBuilder capture)
  {
    capture_timer_ = kTimerExpirationsPerCapture;
    bake_stage_ = BakeStage.kCapture;
    last_time_ = Time.realtimeSinceStartup;
    capture_notification_component_ = capture_notification_component;
    monitored_capture_ = capture;
  }

  public void SetProgressBar(string message, float fraction_complete)
  {
    progress_message_ = message;
    progress_complete_ = fraction_complete;
  }

  public void OnGUI()
  {
    // Reserve layout space for the progress bar, equal to the space for a
    // textfield:
    Rect progress_rect = GUILayoutUtility.GetRect(18, 18, "TextField");
    EditorGUI.ProgressBar(progress_rect, progress_complete_, progress_message_);
    EditorGUILayout.Space();

    if (bake_stage_ != BakeStage.kWaitForDoneButton) {
      if (GUILayout.Button("Cancel")) {
        if (capture_status_ != null) {
          capture_status_.CancelTask();
        }
      }
    }

    if (bake_stage_ == BakeStage.kWaitForDoneButton) {
      if (GUILayout.Button("Done")) {
        bake_stage_ = BakeStage.kComplete;
        // Close immediately instead of waiting for the headbox inspector to
        // poll IsComplete(); OnInspectorGUI only runs while that inspector is
        // visible and repainting, so the window would otherwise hang around.
        Close();
      }
    }
  }

  private bool UpdateAndCheckUiTimerReady()
  {
    bool ui_timer_ready = false;
    float delta_time = Time.realtimeSinceStartup - last_time_;
    last_time_ = Time.realtimeSinceStartup;
    ui_timer_ -= delta_time;
    if (ui_timer_ <= 0.0f) {
      ui_timer_ready = true;
      // Prevent the timer from going infinitely negative due to large real time
      // intervals, e.g. from slow frame capture rendering.
      if (ui_timer_ <= -kTimerInterval) {
        ui_timer_ = 0.0f;
      }
      ui_timer_ += kTimerInterval;
    }
    return ui_timer_ready;
  }

  public void Update()
  {
    // Non-serialized references do not survive a domain reload (script
    // recompilation); without them this window can never make progress or be
    // dismissed, so shut it down.
    if (bake_stage_ == BakeStage.kCapture &&
        (capture_notification_component_ == null || monitored_capture_ == null)) {
      bake_stage_ = BakeStage.kComplete;
      Close();
      return;
    }

    if (capture_status_ != null && capture_status_.TaskContinuing() && !UpdateAndCheckUiTimerReady()) {
      return;
    }

    if (bake_stage_ == BakeStage.kCapture)
    {
      --capture_timer_;
      if (capture_timer_ == 0) {
        capture_timer_ = kTimerExpirationsPerCapture;

        monitored_capture_.RunCapture();

        if (monitored_capture_.IsCaptureComplete() &&
          capture_status_.TaskContinuing())
        {
          monitored_capture_.EndCapture();
          monitored_capture_ = null;

          // Persist last_output_dir_ on the headbox now that the capture is
          // done. EditorUtility.SetDirty is not reliable for scene objects
          // since Unity 2019+, and marking dirty every tick stalls the editor.
          EditorSceneManager.MarkSceneDirty(capture_notification_component_.gameObject.scene);

          bake_stage_ = BakeStage.kWaitForDoneButton;
        }
      }

      if (capture_status_ != null && !capture_status_.TaskContinuing())
      {
        bake_stage_ = BakeStage.kComplete;
        if (monitored_capture_ != null) {
          monitored_capture_.EndCapture();
          monitored_capture_ = null;
        }
        Close();
      }
    }

    // Repaint with updated progress the GUI on each wall-clock time tick.
    Repaint();
  }

  public bool IsComplete()
  {
    return bake_stage_ == BakeStage.kComplete;
  }
};


// Implements the Capture Headbox component Editor panel.
[CustomEditor(typeof(CaptureHeadbox))]
public class CaptureHeadboxEditor : Editor {
  public static readonly string kSeuratCaptureDir = "SeuratCapture";

  SerializedProperty output_folder_;
  SerializedProperty size_;
  SerializedProperty samples_;
  SerializedProperty center_resolution_;
  SerializedProperty resolution_;
  SerializedProperty dynamic_range_;
  SerializedProperty suppress_camera_antialiasing_;
  SerializedProperty suppress_camera_post_processing_;
  SerializedProperty last_output_dir_;

  bool show_bake_command_ = false;

  EditorBakeStatus capture_status_;
  CaptureWindow bake_progress_window_;
  CaptureBuilder capture_builder_;

  void OnEnable() {
    output_folder_ = serializedObject.FindProperty("output_folder_");
    size_ = serializedObject.FindProperty("size_");
    samples_ = serializedObject.FindProperty("samples_per_face_");
    center_resolution_ = serializedObject.FindProperty("center_resolution_");
    resolution_ = serializedObject.FindProperty("resolution_");
    dynamic_range_ = serializedObject.FindProperty("dynamic_range_");
    suppress_camera_antialiasing_ =
      serializedObject.FindProperty("suppress_camera_antialiasing_");
    suppress_camera_post_processing_ =
      serializedObject.FindProperty("suppress_camera_post_processing_");
    last_output_dir_ = serializedObject.FindProperty("last_output_dir_");
  }

  public override void OnInspectorGUI() {
    serializedObject.Update();

    EditorGUILayout.PropertyField(output_folder_, new GUIContent(
      "Output Folder"));
    if (GUILayout.Button("Choose Output Folder")) {
      string path = EditorUtility.SaveFolderPanel(
        "Choose Capture Output Folder", output_folder_.stringValue, "");
      if (path.Length != 0) {
        output_folder_.stringValue = path;
      }
    }

    EditorGUILayout.PropertyField(size_, new GUIContent("Headbox Size"));
    EditorGUILayout.PropertyField(samples_, new GUIContent("Sample Count"));
    EditorGUILayout.PropertyField(center_resolution_, new GUIContent(
      "Center Capture Resolution"));
    EditorGUILayout.PropertyField(resolution_, new GUIContent(
      "Default Resolution"));
    EditorGUILayout.PropertyField(dynamic_range_, new GUIContent(
      "Dynamic Range"));
    EditorGUILayout.PropertyField(suppress_camera_antialiasing_, new GUIContent(
      "Suppress Antialiasing"));
    if (!suppress_camera_antialiasing_.boolValue) {
      EditorGUILayout.HelpBox(
        "Antialiasing blends the colour of two surfaces into one pixel while " +
        "that pixel keeps a single surface's depth. Seurat bakes it as a " +
        "point sample, so the near object's colour lands on the surface " +
        "behind it and shows up as an outline around the object.",
        MessageType.Warning);
    }
    EditorGUILayout.PropertyField(suppress_camera_post_processing_,
      new GUIContent("Suppress Post Processing"));

    EditorGUILayout.PropertyField(last_output_dir_, new GUIContent(
      "Last Output Folder"));

    if (capture_status_ != null)
    {
      GUI.enabled = false;
    }
    if (GUILayout.Button("Capture")) {
      Capture();
    }
    GUI.enabled = true;

    serializedObject.ApplyModifiedProperties();

    DrawBakeCommand((CaptureHeadbox)target);

    // Poll the bake status.
    if (bake_progress_window_ != null && bake_progress_window_.IsComplete()) {
      bake_progress_window_.Close();
      bake_progress_window_ = null;
      capture_builder_ = null;
      capture_status_ = null;
    } else if (bake_progress_window_ == null && capture_status_ != null) {
      // The window is already gone (Done button, manual close, or domain
      // reload). Release the capture state so the Capture button unlocks.
      capture_builder_ = null;
      capture_status_ = null;
    }
  }

  // Shows the seurat command line that bakes the capture these settings
  // produce, with the flag values measured in the blink-cuda fork. Nothing here
  // runs the pipeline; it exists so the two halves of the workflow cannot
  // drift, in particular the texture density, which has to be derived from the
  // capture resolution and is silently clamped otherwise.
  private void DrawBakeCommand(CaptureHeadbox headbox) {
    show_bake_command_ = EditorGUILayout.Foldout(
      show_bake_command_, "Seurat Bake Command", true);
    if (!show_bake_command_) {
      return;
    }
    string command = BuildBakeCommand(headbox);
    EditorGUILayout.SelectableLabel(command, EditorStyles.textArea,
      GUILayout.Height(EditorGUIUtility.singleLineHeight * 6f));
    if (GUILayout.Button("Copy Bake Command")) {
      EditorGUIUtility.systemCopyBuffer = command;
    }
    EditorGUILayout.HelpBox(
      "A capture supplies " + (int)headbox.resolution_ + "/90 = " +
      PixelsPerDegree(headbox) + " pixels per degree, and Seurat clamps " +
      "-pixels_per_degree to what the atlas and the capture can actually " +
      "hold: asking for more only turns the surplus atlas into inpainted " +
      "filler. -premultiply_alpha=false is what a Linear-space Unity " +
      "project needs (see README-import.md); -specular_filter_size widens " +
      "the baker's view-dependent filter, which is the single biggest " +
      "measured reduction of silhouette outlines.",
      MessageType.Info);
  }

  private static int PixelsPerDegree(CaptureHeadbox headbox) {
    // A cube face spans 90 degrees, so this is all the angular resolution the
    // capture has. seurat/baker/texture_sizer.cc sizes the atlas in pixels per
    // radian and clamps to what fits.
    return Mathf.Max(1, (int)((int)headbox.resolution_ / 90));
  }

  private static string BuildBakeCommand(CaptureHeadbox headbox) {
    string capture_dir = headbox.last_output_dir_;
    if (string.IsNullOrEmpty(capture_dir)) {
      capture_dir = headbox.output_folder_;
    }
    if (string.IsNullOrEmpty(capture_dir)) {
      capture_dir = "<capture folder>";
    }
    return
      "seurat" +
      " -input_path=" + capture_dir + "/manifest.json" +
      " -output_path=" + capture_dir + "/scene" +
      " -cache_path=" + capture_dir + "/cache" +
      " -premultiply_alpha=false" +
      " -specular_filter_size=2.0" +
      " -overdraw_factor=2" +
      " -pixels_per_degree=" + PixelsPerDegree(headbox) +
      " -texture_width=8192 -texture_height=8192" +
      " -triangle_count=144000";
  }

  public void Capture() {
    CaptureHeadbox headbox = (CaptureHeadbox)target;

    string capture_output_folder = headbox.output_folder_;
    if (capture_output_folder.Length <= 0) {
      capture_output_folder = FileUtil.GetUniqueTempPathInProject();
    }
    headbox.last_output_dir_ = capture_output_folder;
    EditorSceneManager.MarkSceneDirty(headbox.gameObject.scene);
    Directory.CreateDirectory(capture_output_folder);

    capture_status_ = new EditorBakeStatus();
    capture_builder_ = new CaptureBuilder();

    // Kick off the interactive Editor bake window.
    bake_progress_window_ = (CaptureWindow)EditorWindow.GetWindow(typeof(CaptureWindow));
    bake_progress_window_.SetupStatus(capture_status_);

    capture_builder_.BeginCapture(headbox, capture_output_folder, 1, capture_status_);
    bake_progress_window_.SetupCaptureProcess(headbox, capture_builder_);
  }
}
