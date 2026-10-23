using System;
using System.Collections.Generic;

namespace DLS.Game.Logic
{
	public sealed class Equation
	{
		public string Output;
		public BoolExpr Expr;
	}

	public sealed class ParseResult
	{
		public List<Equation> Equations = new();
		public List<string> Inputs = new(); // sorted alphabetically (ignoring case), first-written spelling
		public string Error;
		public int ErrorIndex;
		public bool Success => Error == null;
	}

	public static class ExprParser
	{
		enum T { Ident, Number, Not, Tick, And, Or, Xor, LParen, RParen, Eq, Sep, Invalid, End }

		struct Token
		{
			public T Type;
			public string Text;
			public int Index;
		}

		sealed class ParseError : Exception
		{
			public readonly int Index;
			public ParseError(string message, int index) : base(message) => Index = index;
		}

		sealed class Parser
		{
			readonly List<Token> tokens;
			int pos;
			int depth;
			const int MaxDepth = 64;
			public readonly Dictionary<string, string> Variables = new(StringComparer.OrdinalIgnoreCase);

			public Parser(List<Token> tokens) => this.tokens = tokens;

			Token Cur => tokens[pos];
			Token Next() => tokens[pos++];

			public bool AtEnd => Cur.Type == T.End;
			public void SkipSeparators()
			{
				while (Cur.Type == T.Sep) pos++;
			}

			public (string name, int nameIndex, BoolExpr expr) ParseEquation()
			{
				string name = null;
				int nameIndex = Cur.Index;
				if (Cur.Type == T.Ident && tokens[pos + 1].Type == T.Eq)
				{
					name = Cur.Text;
					pos += 2;
				}
				BoolExpr expr = ParseOr();
				Token t = Cur;
				switch (t.Type)
				{
					case T.Sep:
					case T.End:
						break;
					case T.Ident:
					case T.Number:
					case T.Not:
					case T.LParen:
						throw new ParseError($"Missing operator before '{t.Text}'. There is no implicit AND: use * for AND (e.g. A*B)", t.Index);
					case T.RParen:
						throw new ParseError("Unmatched ')'", t.Index);
					case T.Eq:
						throw new ParseError("Unexpected '='", t.Index);
					default:
						throw new ParseError($"Unexpected '{t.Text}'", t.Index);
				}
				return (name, nameIndex, expr);
			}

			void Enter()
			{
				if (++depth > MaxDepth) throw new ParseError($"Expression is nested too deeply (more than {MaxDepth} levels)", Cur.Index);
			}

			BoolExpr ParseOr() => ParseChain(ParseXor, T.Or, terms => new OrExpr(terms));
			BoolExpr ParseXor() => ParseChain(ParseAnd, T.Xor, terms => new XorExpr(terms));
			BoolExpr ParseAnd() => ParseChain(ParseUnary, T.And, terms => new AndExpr(terms));

			BoolExpr ParseChain(Func<BoolExpr> operand, T op, Func<List<BoolExpr>, BoolExpr> make)
			{
				BoolExpr first = operand();
				if (Cur.Type != op) return first;
				List<BoolExpr> terms = new() { first };
				while (Cur.Type == op)
				{
					pos++;
					terms.Add(operand());
				}
				return make(terms);
			}

			BoolExpr ParseUnary()
			{
				if (Cur.Type == T.Not)
				{
					Enter();
					pos++;
					BoolExpr operand = ParseUnary();
					depth--;
					return new NotExpr(operand);
				}
				BoolExpr e = ParseAtom();
				while (Cur.Type == T.Tick)
				{
					pos++;
					e = new NotExpr(e);
				}
				return e;
			}

			BoolExpr ParseAtom()
			{
				Token t = Cur;
				switch (t.Type)
				{
					case T.Ident:
						pos++;
						if (!Variables.TryGetValue(t.Text, out string spelling))
						{
							spelling = t.Text;
							Variables[spelling] = spelling;
						}
						return new VarExpr(spelling);
					case T.Number:
						if (t.Text != "0" && t.Text != "1") throw new ParseError($"Invalid constant '{t.Text}': only 0 and 1 are allowed", t.Index);
						pos++;
						return new ConstExpr(t.Text == "1");
					case T.LParen:
					{
						Enter();
						pos++;
						BoolExpr inner = ParseOr();
						if (Cur.Type != T.RParen) throw new ParseError("Missing ')'", Cur.Index);
						pos++;
						depth--;
						return inner;
					}
					case T.Sep:
					case T.End:
						throw new ParseError("Expected a variable, 0, 1 or '(' but the expression ended", t.Index);
					case T.Invalid:
						throw new ParseError($"Unknown character '{t.Text}'", t.Index);
					default:
						throw new ParseError($"Expected a variable, 0, 1 or '(' but found '{t.Text}'", t.Index);
				}
			}
		}

		public static ParseResult Parse(string text)
		{
			ParseResult result = new();
			text ??= "";
			try
			{
				Parser parser = new(Tokenize(text));
				List<(string name, int nameIndex)> names = new();
				while (true)
				{
					parser.SkipSeparators();
					if (parser.AtEnd) break;
					var (name, nameIndex, expr) = parser.ParseEquation();
					result.Equations.Add(new Equation { Output = name, Expr = expr });
					names.Add((name, nameIndex));
				}

				if (result.Equations.Count == 0) throw new ParseError("Enter at least one equation, e.g. Y = A*B", text.Length);

				HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < names.Count; i++)
				{
					if (names[i].name != null && !used.Add(names[i].name)) throw new ParseError($"Output {names[i].name} is defined twice", names[i].nameIndex);
				}
				for (int i = 0; i < names.Count; i++)
				{
					if (names[i].name != null && parser.Variables.ContainsKey(names[i].name)) throw new ParseError($"'{names[i].name}' is used as both an output and an input", names[i].nameIndex);
				}
				int counter = 1;
				foreach (Equation eq in result.Equations)
				{
					if (eq.Output != null) continue;
					string candidate;
					do candidate = counter++ == 1 ? "Y" : "Y" + (counter - 1);
					while (parser.Variables.ContainsKey(candidate) || !used.Add(candidate));
					eq.Output = candidate;
				}

				if (parser.Variables.Count == 0) throw new ParseError("The equations need at least one input variable", 0);
				result.Inputs = new List<string>(parser.Variables.Values);
				result.Inputs.Sort(StringComparer.OrdinalIgnoreCase);
			}
			catch (ParseError e)
			{
				result.Equations.Clear();
				result.Inputs.Clear();
				result.Error = $"{e.Message} (at position {e.Index + 1})";
				result.ErrorIndex = e.Index;
			}
			return result;
		}

		static List<Token> Tokenize(string text)
		{
			List<Token> tokens = new();
			int i = 0;
			while (i < text.Length)
			{
				char c = text[i];
				if (c == '\n' || c == ';') { Add(tokens, T.Sep, c == ';' ? ";" : "newline", i); i++; continue; }
				if (char.IsWhiteSpace(c)) { i++; continue; }
				if (IsLetter(c) || c == '_' || IsDigit(c))
				{
					int start = i;
					while (i < text.Length && (IsAlnum(text[i]) || text[i] == '_')) i++;
					T type = IsDigit(c) ? T.Number : T.Ident;
					Add(tokens, type, text.Substring(start, i - start), start);
					continue;
				}
				T t = c switch
				{
					'!' or '~' => T.Not,
					'\'' => T.Tick,
					'*' or '&' or '.' or '·' => T.And,
					'+' or '|' => T.Or,
					'^' => T.Xor,
					'(' => T.LParen,
					')' => T.RParen,
					'=' => T.Eq,
					_ => T.Invalid
				};
				Add(tokens, t, c.ToString(), i);
				i++;
			}
			Add(tokens, T.End, "end of input", text.Length);
			return tokens;
		}

		static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
		static bool IsDigit(char c) => c >= '0' && c <= '9';
		static bool IsAlnum(char c) => IsLetter(c) || IsDigit(c);

		static void Add(List<Token> tokens, T type, string text, int index) => tokens.Add(new Token { Type = type, Text = text, Index = index });
	}
}
