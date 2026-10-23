using DLS.Description;
using DLS.Game.Notes;
using Seb.Helpers;
using Seb.Types;
using UnityEngine;
using static DLS.Graphics.DrawSettings;

namespace DLS.Game
{
	// Text note on the canvas. Has no pins and is ignored by the simulation, so it may overlap chips and wires.
	public class NoteInstance : IMoveable
	{
		public string Text;
		public NoteColour Colour;

		string measuredText;
		Vector2 measuredSize;
		string[] measuredLines;

		public NoteInstance(NoteDescription description)
		{
			ID = description.ID;
			Position = description.Position;
			Text = description.Text ?? string.Empty;
			Colour = description.Colour;
		}

		// Size is calculated from the (normalized) text, so it always matches what is drawn
		public Vector2 Size
		{
			get
			{
				UpdateMeasurements();
				return measuredSize;
			}
		}

		// Lines of the normalized text (at most NoteText.MaxLines lines of NoteText.MaxLineLength characters)
		public string[] Lines
		{
			get
			{
				UpdateMeasurements();
				return measuredLines;
			}
		}

		public Vector2 Centre => Position + new Vector2(Size.x, -Size.y) / 2;

		// Position is the top-left corner
		public Vector2 Position { get; set; }
		public Vector2 MoveStartPosition { get; set; }
		public Vector2 StraightLineReferencePoint { get; set; }
		public int ID { get; }

		public bool IsSelected { get; set; }
		public bool HasReferencePointForStraightLineMovement { get; set; }
		public bool IsValidMovePos { get; set; }

		public Bounds2D BoundingBox => CreateBoundingBox(0);
		public Bounds2D SelectionBoundingBox => CreateBoundingBox(SelectionBoundsPadding);

		public Vector2 SnapPoint => Position;

		public bool ShouldBeIncludedInSelectionBox(Vector2 selectionCentre, Vector2 selectionSize)
		{
			Bounds2D selfBounds = SelectionBoundingBox;
			return Maths.BoxesOverlap(selectionCentre, selectionSize, selfBounds.Centre, selfBounds.Size);
		}

		// Cached since the bounds are queried often while elements are being moved
		void UpdateMeasurements()
		{
			if (measuredLines != null && ReferenceEquals(measuredText, Text)) return;

			(float width, float height) = NoteText.Measure(Text, NoteCharWidth, NoteLineHeight, NotePadding);
			measuredSize = new Vector2(width, height);
			measuredLines = NoteText.Lines(Text);
			measuredText = Text;
		}

		Bounds2D CreateBoundingBox(float pad) => Bounds2D.CreateFromCentreAndSize(Centre, Size + Vector2.one * pad);
	}
}
