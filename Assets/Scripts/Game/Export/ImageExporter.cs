using System;
using System.Collections;
using System.IO;
using DLS.Graphics;
using DLS.SaveSystem;
using Seb.Types;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DLS.Game.Export
{
	public struct ExportOptions
	{
		public int Scale;
		public bool AllPinNames;
		public bool ShowGrid;
	}

	// Saves the viewed chip as a PNG: the camera is framed on the circuit and the UI is hidden for one frame, which is captured at the end of that frame
	public static class ImageExporter
	{
		const float PaddingFraction = 0.06f;
		const float SavedBannerSeconds = 4;
		const float ErrorBannerSeconds = 6;

		public static bool IsCapturing { get; private set; }
		public static ExportOptions Options { get; private set; }

		public static void Export(ExportOptions options)
		{
			if (IsCapturing) return;

			Project project = Project.ActiveProject;
			DevChipInstance chip = project.ViewedChip;
			if (chip.Elements.Count == 0) return;

			Options = options;
			UnityMain.instance.StartCoroutine(Capture(project, chip, options));
		}

		static IEnumerator Capture(Project project, DevChipInstance chip, ExportOptions options)
		{
			project.controller.CancelEverything();
			CameraController.ViewState savedView = CameraController.CaptureView();

			string folder = SavePaths.GetExportsPath(project.description.ProjectName);
			string fileName = ExportNaming.FileName(chip.ChipName, DateTime.Now);
			string error = null;
			int startFrame = Time.frameCount;

			IsCapturing = true;
			// Hover highlights on wires and pins are skipped while the mouse is over UI
			InteractionState.MouseIsOverUI = true;

			try
			{
				// No bottom-bar offset (unlike the normal chip view) since the UI is hidden
				Bounds2D bounds = CameraController.GetChipBounds(chip);
				float aspect = Screen.width / (float)Screen.height;
				(float centreX, float centreY, float orthoSize) = ExportFraming.Fit(bounds.Min.x, bounds.Min.y, bounds.Max.x, bounds.Max.y, aspect, PaddingFraction);
				CameraController.SetView(new Vector2(centreX, centreY), orthoSize);

				// Wait until a frame has been drawn with the capture state (Main.Update skips the UI), then grab it once it has rendered
				while (Time.frameCount <= startFrame) yield return null;
				yield return new WaitForEndOfFrame();

				error = CaptureAndSave(options.Scale, folder, fileName);
			}
			finally
			{
				CameraController.RestoreView(savedView);
				IsCapturing = false;
				// The options popup stays open during the capture so the controller ignores input; close it now
				if (UIDrawer.ActiveMenu == UIDrawer.MenuType.ExportImage) UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}

			if (error == null)
			{
				StatusBanner.Show("Image saved: " + fileName, SavedBannerSeconds);
				Main.OpenFolderInFileBrowser(folder);
			}
			else
			{
				StatusBanner.Show("Export failed: " + error, ErrorBannerSeconds);
			}
		}

		// Returns null on success, otherwise the error message
		static string CaptureAndSave(int scale, string folder, string fileName)
		{
			Texture2D texture = null;
			try
			{
				texture = ScreenCapture.CaptureScreenshotAsTexture(scale);
				byte[] png = texture.EncodeToPNG();
				SavePaths.EnsureDirectoryExists(folder);
				File.WriteAllBytes(Path.Combine(folder, fileName), png);
				return null;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return e.Message;
			}
			finally
			{
				if (texture != null) Object.Destroy(texture);
			}
		}
	}
}
