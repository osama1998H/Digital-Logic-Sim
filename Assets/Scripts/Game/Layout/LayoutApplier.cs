using System.Collections.Generic;
using DLS.Description;
using UnityEngine;

namespace DLS.Game.Layout
{
	// Converts a LayoutResult back into wire descriptions that DevChipInstance.ReplaceAllWires can load
	public static class LayoutApplier
	{
		// originalWires and originalDescriptions are indexed by connection key (the wire index the problem was built from).
		// The returned descriptions are in result order, so every wire-attached wire comes after the wire it attaches to.
		// invariantError is non-null if the descriptions would not load exactly as built.
		public static WireDescription[] BuildWireDescriptions(LayoutResult result, IList<WireInstance> originalWires, WireDescription[] originalDescriptions, out string invariantError)
		{
			int count = result.Connections.Count;
			Dictionary<int, int> newIndexByKey = new(count);
			int[] keyAtNewIndex = new int[count];

			for (int i = 0; i < count; i++)
			{
				newIndexByKey[result.Connections[i].Key] = i;
				keyAtNewIndex[i] = result.Connections[i].Key;
			}

			WireDescription[] descriptions = new WireDescription[count];

			for (int i = 0; i < count; i++)
			{
				RoutedConnection connection = result.Connections[i];
				WireDescription original = originalDescriptions[connection.Key];
				descriptions[i] = connection.Routed ? FromRouted(connection, original, newIndexByKey) : FromUnrouted(original, i, result, newIndexByKey, descriptions);
			}

			invariantError = FindInvariantViolation(descriptions, keyAtNewIndex, originalWires, originalDescriptions.Length);
			return descriptions;
		}

		static WireDescription FromRouted(RoutedConnection connection, WireDescription original, Dictionary<int, int> newIndexByKey)
		{
			WireConnectionType connectionType = WireConnectionType.ToPins;
			if (connection.SourceAttach == AttachKind.Wire) connectionType = WireConnectionType.ToWireSource;
			else if (connection.TargetAttach == AttachKind.Wire) connectionType = WireConnectionType.ToWireTarget;

			bool attached = connectionType != WireConnectionType.ToPins;
			int parentIndex = attached && newIndexByKey.TryGetValue(connection.AttachedToKey, out int index) ? index : -1;

			Vector2[] points = new Vector2[connection.Points.Count];
			for (int i = 0; i < points.Length; i++)
			{
				points[i] = LayoutProblemBuilder.ToVector(connection.Points[i]);
			}

			return new WireDescription
			{
				SourcePinAddress = original.SourcePinAddress,
				TargetPinAddress = original.TargetPinAddress,
				ConnectionType = connectionType,
				ConnectedWireIndex = parentIndex,
				ConnectedWireSegmentIndex = attached ? connection.AttachedSegmentIndex : -1,
				Points = points // the wire-attached end is the router's exact attach coordinate
			};
		}

		static WireDescription FromUnrouted(WireDescription original, int newIndex, LayoutResult result, Dictionary<int, int> newIndexByKey, WireDescription[] descriptions)
		{
			WireDescription desc = original;
			desc.Points = (Vector2[])original.Points.Clone();
			if (desc.ConnectionType == WireConnectionType.ToPins) return desc;

			bool attachedAtSource = desc.ConnectionType == WireConnectionType.ToWireSource;
			bool parentFound = newIndexByKey.TryGetValue(original.ConnectedWireIndex, out int parentIndex) && parentIndex < newIndex;
			bool parentRouted = parentFound && result.Connections[parentIndex].Routed;

			// Source-attached wires only keep their attachment to a rerouted parent; bus writers also keep it to an unrouted trunk
			if (!parentFound || (attachedAtSource && !parentRouted))
			{
				desc.ConnectionType = WireConnectionType.ToPins;
				desc.ConnectedWireIndex = -1;
				desc.ConnectedWireSegmentIndex = -1;
				return desc;
			}

			desc.ConnectedWireIndex = parentIndex;

			// Parent has a new shape, so move the attach point onto it (WireInstance projects onto the stored segment)
			if (parentRouted)
			{
				int endIndex = attachedAtSource ? 0 : desc.Points.Length - 1;
				(Vector2 point, int segmentIndex) = ClosestPointOnPath(descriptions[parentIndex].Points, desc.Points[endIndex]);
				desc.Points[endIndex] = point;
				desc.ConnectedWireSegmentIndex = segmentIndex;
			}

			return desc;
		}

		static (Vector2 point, int segmentIndex) ClosestPointOnPath(Vector2[] path, Vector2 p)
		{
			Vector2 bestPoint = path[0];
			int bestSegment = 0;
			float bestSqrDst = float.MaxValue;

			for (int i = 0; i < path.Length - 1; i++)
			{
				Vector2 q = WireInstance.ClosestPointOnLineSegment(p, path[i], path[i + 1]);
				float sqrDst = (q - p).sqrMagnitude;
				if (sqrDst < bestSqrDst)
				{
					bestSqrDst = sqrDst;
					bestPoint = q;
					bestSegment = i;
				}
			}

			return (bestPoint, bestSegment);
		}

		// Mirrors the checks in DevChipInstance.TryLoadWireFromDescription (plus index order), so a bad result is reported before loading
		static string FindInvariantViolation(WireDescription[] descriptions, int[] keyAtNewIndex, IList<WireInstance> originalWires, int originalCount)
		{
			if (descriptions.Length != originalCount) return $"Result has {descriptions.Length} connections, expected {originalCount}";

			HashSet<int> seenKeys = new();
			for (int i = 0; i < descriptions.Length; i++)
			{
				WireDescription desc = descriptions[i];
				if (!seenKeys.Add(keyAtNewIndex[i])) return $"Duplicate connection key {keyAtNewIndex[i]}";
				if (desc.Points == null || desc.Points.Length < 2) return $"Wire {i} has fewer than two points";
				if (desc.ConnectionType == WireConnectionType.ToPins)
				{
					if (desc.ConnectedWireIndex != -1) return $"Pin-to-pin wire {i} references wire {desc.ConnectedWireIndex}";
					continue;
				}

				int parentIndex = desc.ConnectedWireIndex;
				if (parentIndex < 0 || parentIndex >= i) return $"Wire {i} attaches to wire {parentIndex}, which is not before it";

				WireDescription parent = descriptions[parentIndex];
				if (desc.ConnectedWireSegmentIndex < 0 || desc.ConnectedWireSegmentIndex >= parent.Points.Length - 1) return $"Wire {i} attaches to invalid segment {desc.ConnectedWireSegmentIndex} of wire {parentIndex}";

				WireInstance parentWire = originalWires[keyAtNewIndex[parentIndex]];
				if (desc.ConnectionType == WireConnectionType.ToWireSource)
				{
					if (!PinAddress.Equals(parent.SourcePinAddress, desc.SourcePinAddress)) return $"Wire {i} attaches at source to wire {parentIndex} with a different source pin";
				}
				else if (!parentWire.IsBusWire || !PinAddress.Equals(parentWire.TargetPin_BusCorrected.Address, desc.TargetPinAddress))
				{
					return $"Wire {i} attaches at target to wire {parentIndex}, which is not its bus";
				}
			}

			return null;
		}
	}
}
