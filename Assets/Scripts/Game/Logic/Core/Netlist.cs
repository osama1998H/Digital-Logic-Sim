using System;
using System.Collections.Generic;

namespace DLS.Game.Logic
{
	public enum GateKind { Not, And, Or, Xor, Nand }

	[Flags]
	public enum GateSet { None = 0, Not = 1, And = 2, Or = 4, Xor = 8 } // NAND is always available

	public sealed class Gate
	{
		public GateKind Kind;
		public int[] InputNets;
		public int OutputNet;
	}

	// Nets 0..n-1 are the inputs; gate i drives net n+i. Gates are in topological order.
	// On failure (parse error, too many inputs/outputs/gates, no inputs) Error is set and the lists are empty.
	public sealed class Netlist
	{
		public string[] InputNames = Array.Empty<string>();
		public string[] OutputNames = Array.Empty<string>();
		public int[] OutputNets = Array.Empty<int>();
		public List<Gate> Gates = new();
		public string Error;
		public bool Success => Error == null;

		public bool[] Evaluate(bool[] inputs)
		{
			bool[] nets = new bool[InputNames.Length + Gates.Count];
			Array.Copy(inputs, nets, InputNames.Length);
			foreach (Gate g in Gates)
			{
				bool a = nets[g.InputNets[0]];
				bool b = g.InputNets.Length > 1 && nets[g.InputNets[1]];
				nets[g.OutputNet] = g.Kind switch
				{
					GateKind.Not => !a,
					GateKind.And => a && b,
					GateKind.Or => a || b,
					GateKind.Xor => a ^ b,
					_ => !(a && b)
				};
			}
			bool[] outs = new bool[OutputNets.Length];
			for (int i = 0; i < outs.Length; i++) outs[i] = nets[OutputNets[i]];
			return outs;
		}
	}

	public static class NetlistBuilder
	{
		public const int MaxInputs = 16;
		public const int MaxOutputs = 8;
		public const int MaxGates = 300;

		sealed class LimitException : Exception
		{
			public LimitException(string message) : base(message) { }
		}

		public static Netlist Build(ParseResult parsed, GateSet available)
		{
			if (!parsed.Success) return Fail(parsed.Error);
			if (parsed.Inputs.Count == 0) return Fail("needs at least one input");
			if (parsed.Inputs.Count > MaxInputs) return Fail($"too many inputs ({parsed.Inputs.Count}, max {MaxInputs})");
			if (parsed.Equations.Count > MaxOutputs) return Fail($"too many outputs ({parsed.Equations.Count}, max {MaxOutputs})");

			Builder b = new(parsed, available);
			try
			{
				return b.Run();
			}
			catch (LimitException e)
			{
				return Fail(e.Message);
			}
		}

		static Netlist Fail(string error) => new() { Error = error };

		sealed class Builder
		{
			readonly ParseResult parsed;
			readonly GateSet available;
			readonly Netlist result = new();
			readonly Dictionary<string, int> inputIndex = new(StringComparer.OrdinalIgnoreCase);
			readonly Dictionary<(GateKind, int, int), int> shared = new();
			readonly Dictionary<int, int> inverseOf = new(); // net -> net it is the NOT of (both directions)
			int constOne = -1;

			public Builder(ParseResult parsed, GateSet available)
			{
				this.parsed = parsed;
				this.available = available;
			}

			public Netlist Run()
			{
				result.InputNames = parsed.Inputs.ToArray();
				for (int i = 0; i < parsed.Inputs.Count; i++) inputIndex[parsed.Inputs[i]] = i;
				result.OutputNames = new string[parsed.Equations.Count];
				result.OutputNets = new int[parsed.Equations.Count];
				for (int i = 0; i < parsed.Equations.Count; i++)
				{
					result.OutputNames[i] = parsed.Equations[i].Output;
					result.OutputNets[i] = Emit(parsed.Equations[i].Expr);
				}
				return result;
			}

			int Emit(BoolExpr e)
			{
				switch (e)
				{
					case VarExpr v: return inputIndex[v.Name];
					case ConstExpr c:
						if (constOne < 0) constOne = Nand(0, Not(0));
						return c.Value ? constOne : Not(constOne);
					case NotExpr n:
						if (n.Operand is NotExpr inner) return Emit(inner.Operand); // NOT(NOT x) = x, no gates
						return Not(Emit(n.Operand));
					case NaryExpr nary:
						int acc = Emit(nary.Terms[0]);
						for (int i = 1; i < nary.Terms.Count; i++)
						{
							int next = Emit(nary.Terms[i]);
							acc = nary switch
							{
								AndExpr => And(acc, next),
								OrExpr => Or(acc, next),
								_ => Xor(acc, next)
							};
						}
						return acc;
					default: throw new ArgumentException("Unknown expression type");
				}
			}

			int Not(int a)
			{
				if (inverseOf.TryGetValue(a, out int inv)) return inv;
				int o = (available & GateSet.Not) != 0 ? Gate(GateKind.Not, a, -1) : Gate(GateKind.Nand, a, a);
				inverseOf[o] = a;
				inverseOf[a] = o;
				return o;
			}

			int And(int a, int b) => (available & GateSet.And) != 0 ? Gate(GateKind.And, a, b) : Not(Nand(a, b));
			int Or(int a, int b) => (available & GateSet.Or) != 0 ? Gate(GateKind.Or, a, b) : Nand(Not(a), Not(b));

			int Xor(int a, int b)
			{
				if ((available & GateSet.Xor) != 0) return Gate(GateKind.Xor, a, b);
				int t = Nand(a, b);
				return Nand(Nand(a, t), Nand(b, t));
			}

			int Nand(int a, int b) => Gate(GateKind.Nand, a, b);

			int Gate(GateKind kind, int a, int b)
			{
				if (b >= 0 && b < a) (a, b) = (b, a);
				(GateKind, int, int) key = (kind, a, b);
				if (shared.TryGetValue(key, out int existing)) return existing;
				if (result.Gates.Count >= MaxGates) throw new LimitException($"too many gates (max {MaxGates})");
				int net = result.InputNames.Length + result.Gates.Count;
				result.Gates.Add(new Gate { Kind = kind, InputNets = b < 0 ? new[] { a } : new[] { a, b }, OutputNet = net });
				shared[key] = net;
				return net;
			}
		}
	}
}
