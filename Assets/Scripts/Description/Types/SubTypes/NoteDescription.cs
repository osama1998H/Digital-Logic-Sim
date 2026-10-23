using UnityEngine;

namespace DLS.Description
{
	public struct NoteDescription
	{
		public int ID; // Unique within parent chip (shared with subchip and pin IDs)
		public Vector2 Position; // Top-left corner
		public string Text;
		public NoteColour Colour;

		public NoteDescription(int id, Vector2 position, string text, NoteColour colour)
		{
			ID = id;
			Position = position;
			Text = text;
			Colour = colour;
		}
	}

	public enum NoteColour
	{
		Yellow,
		Blue,
		Green,
		Pink,
		Grey
	}
}
