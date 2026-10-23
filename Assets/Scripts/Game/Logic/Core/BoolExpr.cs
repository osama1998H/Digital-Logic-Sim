using System;
using System.Collections.Generic;

namespace DLS.Game.Logic
{
	public abstract class BoolExpr
	{
		// Variable names are matched case-insensitively
		public bool Evaluate(IReadOnlyDictionary<string, bool> values)
		{
			Dictionary<string, bool> lookup = new(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, bool> pair in values) lookup[pair.Key] = pair.Value;
			return Eval(lookup);
		}

		public abstract void CollectVariables(ISet<string> into);
		internal abstract bool Eval(Dictionary<string, bool> values);
	}

	public sealed class VarExpr : BoolExpr
	{
		public readonly string Name;
		public VarExpr(string name) => Name = name;
		public override void CollectVariables(ISet<string> into) => into.Add(Name);

		internal override bool Eval(Dictionary<string, bool> values)
		{
			if (!values.TryGetValue(Name, out bool v)) throw new KeyNotFoundException("No value for variable " + Name);
			return v;
		}
	}

	public sealed class ConstExpr : BoolExpr
	{
		public readonly bool Value;
		public ConstExpr(bool value) => Value = value;
		public override void CollectVariables(ISet<string> into) { }
		internal override bool Eval(Dictionary<string, bool> values) => Value;
	}

	public sealed class NotExpr : BoolExpr
	{
		public readonly BoolExpr Operand;
		public NotExpr(BoolExpr operand) => Operand = operand;
		public override void CollectVariables(ISet<string> into) => Operand.CollectVariables(into);
		internal override bool Eval(Dictionary<string, bool> values) => !Operand.Eval(values);
	}

	public abstract class NaryExpr : BoolExpr
	{
		public readonly List<BoolExpr> Terms;
		protected NaryExpr(List<BoolExpr> terms) => Terms = terms;

		public override void CollectVariables(ISet<string> into)
		{
			foreach (BoolExpr t in Terms) t.CollectVariables(into);
		}
	}

	public sealed class AndExpr : NaryExpr
	{
		public AndExpr(List<BoolExpr> terms) : base(terms) { }

		internal override bool Eval(Dictionary<string, bool> values)
		{
			foreach (BoolExpr t in Terms)
			{
				if (!t.Eval(values)) return false;
			}
			return true;
		}
	}

	public sealed class OrExpr : NaryExpr
	{
		public OrExpr(List<BoolExpr> terms) : base(terms) { }

		internal override bool Eval(Dictionary<string, bool> values)
		{
			foreach (BoolExpr t in Terms)
			{
				if (t.Eval(values)) return true;
			}
			return false;
		}
	}

	public sealed class XorExpr : NaryExpr
	{
		public XorExpr(List<BoolExpr> terms) : base(terms) { }

		internal override bool Eval(Dictionary<string, bool> values)
		{
			bool result = false;
			foreach (BoolExpr t in Terms) result ^= t.Eval(values);
			return result;
		}
	}
}
