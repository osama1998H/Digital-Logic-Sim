using System;
using System.Collections;
using System.IO;
using DLS.Graphics;
using DLS.SaveSystem;
using Seb.Helpers;
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

	// Saves the viewed chip as a PNG: the camera is framed on the circuit and the UI is hidden for one frame.
	// At the end of that frame the camera is rendered again into a (larger) render texture, which is read back and saved.
	public static class ImageExporter
	{
		const float PaddingFraction = 0.06f;
		const int MaxImageSize = 8192;
		const float SavedBannerSeconds = 4;
		const float ErrorBannerSeconds = 6;
		const float SavingBannerSeconds = 30; // replaced by the result banner once saved

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
			// Screen size is in physical pixels (so on a Retina display 1x is already high resolution)
			(int width, int height, int _) = ExportSize.Fit(Screen.width, Screen.height, options.Scale, Mathf.Min(SystemInfo.maxTextureSize, MaxImageSize));
			string error;
			Texture2D texture = null;
			int startFrame = Time.frameCount;

			IsCapturing = true;
			// Hover highlights on wires and pins are skipped while the mouse is over UI
			InteractionState.MouseIsOverUI = true;

			try
			{
				error = TryFrameCamera(chip, width / (float)height);

				if (error == null)
				{
					// Wait until a frame has been drawn with the capture state (Main.Update skips the UI), then render it again at the export size.
					// At the end of the frame the draw manager's command buffer still holds that frame's draw commands.
					while (Time.frameCount <= startFrame) yield return null;
					yield return new WaitForEndOfFrame();

					error = TryRender(width, height, out texture);
				}
			}
			finally
			{
				CameraController.RestoreView(savedView);
				IsCapturing = false;
				// The options popup stays open during the capture so the controller ignores input; close it now
				if (UIDrawer.ActiveMenu == UIDrawer.MenuType.ExportImage) UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}

			if (error != null)
			{
				if (texture != null) Object.Destroy(texture);
				StatusBanner.Show("Export failed: " + error, ErrorBannerSeconds);
				yield break;
			}

			try
			{
				// Encoding a large image can take a moment: let a frame with the banner reach the screen first
				StatusBanner.Show("Saving image...", SavingBannerSeconds);
				int bannerFrame = Time.frameCount;
				while (Time.frameCount <= bannerFrame + 1) yield return null;

				error = TrySave(texture, folder, fileName);
			}
			finally
			{
				Object.Destroy(texture);
			}

			if (error == null)
			{
				StatusBanner.Show($"Image saved ({width}x{height}): {fileName}", SavedBannerSeconds);
				Main.OpenFolderInFileBrowser(folder);
			}
			else
			{
				StatusBanner.Show("Export failed: " + error, ErrorBannerSeconds);
			}
		}

		// Each step returns null on success, otherwise the error message

		static string TryFrameCamera(DevChipInstance chip, float aspect)
		{
			try
			{
				// No bottom-bar offset (unlike the normal chip view) since the UI is hidden
				Bounds2D bounds = CameraController.GetChipBounds(chip);
				(float centreX, float centreY, float orthoSize) = ExportFraming.Fit(bounds.Min.x, bounds.Min.y, bounds.Max.x, bounds.Max.y, aspect, PaddingFraction);
				CameraController.SetView(new Vector2(centreX, centreY), orthoSize);
				return null;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return e.Message;
			}
		}

		// Renders the world camera into a render texture rather than reading the back buffer:
		// any size is possible, and reading into an RGB texture gives an opaque image (the shaders blend alpha into the target)
		static string TryRender(int width, int height, out Texture2D texture)
		{
			texture = null;
			Camera cam = InputHelper.WorldCam;
			if (cam == null) return "camera not found";

			RenderTexture prevTarget = cam.targetTexture;
			RenderTexture prevActive = RenderTexture.active;
			RenderTexture rt = null;
			try
			{
				rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
				cam.targetTexture = rt;
				cam.Render();

				RenderTexture.active = rt;
				texture = new Texture2D(width, height, TextureFormat.RGB24, false);
				texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				texture.Apply(false);
				return null;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				if (texture != null) Object.Destroy(texture);
				texture = null;
				return e.Message;
			}
			finally
			{
				cam.targetTexture = prevTarget;
				RenderTexture.active = prevActive;
				if (rt != null) RenderTexture.ReleaseTemporary(rt);
			}
		}

		static string TrySave(Texture2D texture, string folder, string fileName)
		{
			try
			{
				byte[] png = texture.EncodeToPNG();
				if (png == null || png.Length == 0) return "could not encode the image";
				SavePaths.EnsureDirectoryExists(folder);
				File.WriteAllBytes(Path.Combine(folder, fileName), png);
				return null;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return e.Message;
			}
		}

		public static void Reset()
		{
			IsCapturing = false;
		}
	}
}
