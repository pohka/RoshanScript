using System.Collections.Generic;
using System.Text;

namespace DotaTranspiler.IR
{
    // -------------------------------------------------------------------------
    // Base node
    // -------------------------------------------------------------------------

    public abstract class LuaNode
    {
        public abstract void WriteTo(LuaWriter writer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            using var writer = new LuaWriter(sb);
            WriteTo(writer);
            return sb.ToString();
        }
    }

    // -------------------------------------------------------------------------
    // Literals & identifiers
    // -------------------------------------------------------------------------

    public sealed class LuaNil : LuaNode
    {
        public static readonly LuaNil Instance = new();
        public override void WriteTo(LuaWriter w) => w.Write("nil");
    }

    public sealed class LuaTrue : LuaNode
    {
        public static readonly LuaTrue Instance = new();
        public override void WriteTo(LuaWriter w) => w.Write("true");
    }

    public sealed class LuaFalse : LuaNode
    {
        public static readonly LuaFalse Instance = new();
        public override void WriteTo(LuaWriter w) => w.Write("false");
    }

    public sealed class LuaNumber : LuaNode
    {
        public double Value { get; }
        public LuaNumber(double value) => Value = value;

        public override void WriteTo(LuaWriter w)
        {
            // Emit integers without decimal point when the value is whole
            if (Value == System.Math.Floor(Value) && !double.IsInfinity(Value))
                w.Write(((long)Value).ToString());
            else
                w.Write(Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public sealed class LuaString : LuaNode
    {
        public string Value { get; }
        public LuaString(string value) => Value = value;

        public override void WriteTo(LuaWriter w)
        {
            // Escape backslashes and double-quotes, wrap in double-quotes
            w.Write("\"");
            w.Write(Value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                        .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t"));
            w.Write("\"");
        }
    }

    public sealed class LuaIdentifier : LuaNode
    {
        public string Name { get; }
        public LuaIdentifier(string name) => Name = name;
        public override void WriteTo(LuaWriter w) => w.Write(Name);
    }

    public sealed class LuaVarArg : LuaNode
    {
        public static readonly LuaVarArg Instance = new();
        public override void WriteTo(LuaWriter w) => w.Write("...");
    }

    // -------------------------------------------------------------------------
    // Expressions
    // -------------------------------------------------------------------------

    public sealed class LuaUnary : LuaNode
    {
        public string Op { get; }          // "not", "-", "#"
        public LuaNode Operand { get; }
        public LuaUnary(string op, LuaNode operand) { Op = op; Operand = operand; }

        public override void WriteTo(LuaWriter w)
        {
            w.Write(Op);
            if (Op == "not") w.Write(" ");
            Operand.WriteTo(w);
        }
    }

    public sealed class LuaBinary : LuaNode
    {
        public LuaNode Left { get; }
        public string Op { get; }
        public LuaNode Right { get; }
        public LuaBinary(LuaNode left, string op, LuaNode right)
        {
            Left = left; Op = op; Right = right;
        }

        public override void WriteTo(LuaWriter w)
        {
            Left.WriteTo(w);
            w.Write(" "); w.Write(Op); w.Write(" ");
            Right.WriteTo(w);
        }
    }

    public sealed class LuaParenthesized : LuaNode
    {
        public LuaNode Inner { get; }
        public LuaParenthesized(LuaNode inner) => Inner = inner;

        public override void WriteTo(LuaWriter w)
        {
            w.Write("(");
            Inner.WriteTo(w);
            w.Write(")");
        }
    }

    /// <summary>Table field access: table.field</summary>
    public sealed class LuaMemberAccess : LuaNode
    {
        public LuaNode Table { get; }
        public string Field { get; }
        public LuaMemberAccess(LuaNode table, string field) { Table = table; Field = field; }

        public override void WriteTo(LuaWriter w)
        {
            Table.WriteTo(w);
            w.Write(".");
            w.Write(Field);
        }
    }

    /// <summary>Table index access: table[key]</summary>
    public sealed class LuaIndexAccess : LuaNode
    {
        public LuaNode Table { get; }
        public LuaNode Key { get; }
        public LuaIndexAccess(LuaNode table, LuaNode key) { Table = table; Key = key; }

        public override void WriteTo(LuaWriter w)
        {
            Table.WriteTo(w);
            w.Write("[");
            Key.WriteTo(w);
            w.Write("]");
        }
    }

    /// <summary>Regular function call: f(args) or table.f(args)</summary>
    public sealed class LuaCall : LuaNode
    {
        public LuaNode Target { get; }
        public IReadOnlyList<LuaNode> Args { get; }
        public LuaCall(LuaNode target, IReadOnlyList<LuaNode> args)
        {
            Target = target; Args = args;
        }

        public override void WriteTo(LuaWriter w)
        {
            Target.WriteTo(w);
            w.Write("(");
            for (int i = 0; i < Args.Count; i++)
            {
                if (i > 0) w.Write(", ");
                Args[i].WriteTo(w);
            }
            w.Write(")");
        }
    }

    /// <summary>Method call using colon syntax: obj:Method(args)</summary>
    public sealed class LuaMethodCall : LuaNode
    {
        public LuaNode Target { get; }
        public string MethodName { get; }
        public IReadOnlyList<LuaNode> Args { get; }
        public LuaMethodCall(LuaNode target, string methodName, IReadOnlyList<LuaNode> args)
        {
            Target = target; MethodName = methodName; Args = args;
        }

        public override void WriteTo(LuaWriter w)
        {
            Target.WriteTo(w);
            w.Write(":");
            w.Write(MethodName);
            w.Write("(");
            for (int i = 0; i < Args.Count; i++)
            {
                if (i > 0) w.Write(", ");
                Args[i].WriteTo(w);
            }
            w.Write(")");
        }
    }

    /// <summary>Anonymous function expression: function(params) body end</summary>
    public sealed class LuaFunctionExpr : LuaNode
    {
        public IReadOnlyList<string> Parameters { get; }
        public LuaBlock Body { get; }
        public LuaFunctionExpr(IReadOnlyList<string> parameters, LuaBlock body)
        {
            Parameters = parameters; Body = body;
        }

        public override void WriteTo(LuaWriter w)
        {
            w.Write("function(");
            w.Write(string.Join(", ", Parameters));
            w.Write(")");
            w.Indent();
            Body.WriteTo(w);
            w.Dedent();
            w.WriteLine("end");
        }
    }

    /// <summary>Table constructor: { [key] = val, ... } or { val, val, ... }</summary>
    public sealed class LuaTableConstructor : LuaNode
    {
        public IReadOnlyList<LuaTableField> Fields { get; }
        public LuaTableConstructor(IReadOnlyList<LuaTableField> fields) => Fields = fields;

        public override void WriteTo(LuaWriter w)
        {
            if (Fields.Count == 0) { w.Write("{}"); return; }

            w.WriteLine("{");
            w.Indent();
            foreach (var f in Fields)
            {
                f.WriteTo(w);
                w.WriteLine(",");
            }
            w.Dedent();
            w.Write("}");
        }
    }

    public sealed class LuaTableField : LuaNode
    {
        public LuaNode? Key { get; }    // null = array-style, string key = name-style
        public bool IsStringKey { get; }
        public LuaNode Value { get; }

        public LuaTableField(LuaNode value) { Value = value; }
        public LuaTableField(string key, LuaNode value) { Key = new LuaIdentifier(key); IsStringKey = true; Value = value; }
        public LuaTableField(LuaNode key, LuaNode value, bool indexKey) { Key = key; Value = value; }

        public override void WriteTo(LuaWriter w)
        {
            if (Key == null)
            {
                Value.WriteTo(w);
            }
            else if (IsStringKey)
            {
                Key.WriteTo(w);
                w.Write(" = ");
                Value.WriteTo(w);
            }
            else
            {
                w.Write("[");
                Key.WriteTo(w);
                w.Write("] = ");
                Value.WriteTo(w);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Statements
    // -------------------------------------------------------------------------

    public sealed class LuaBlock : LuaNode
    {
        public IReadOnlyList<LuaNode> Statements { get; }
        public LuaBlock(IReadOnlyList<LuaNode> statements) => Statements = statements;

        public override void WriteTo(LuaWriter w)
        {
            foreach (var s in Statements)
            {
                s.WriteTo(w);
                if (s is not LuaComment) w.NewLine();
            }
        }
    }

    public sealed class LuaLocalDecl : LuaNode
    {
        public IReadOnlyList<string> Names { get; }
        public IReadOnlyList<LuaNode>? Values { get; }
        public LuaLocalDecl(IReadOnlyList<string> names, IReadOnlyList<LuaNode>? values = null)
        {
            Names = names; Values = values;
        }

        public override void WriteTo(LuaWriter w)
        {
            w.Write("local ");
            w.Write(string.Join(", ", Names));
            if (Values != null && Values.Count > 0)
            {
                w.Write(" = ");
                for (int i = 0; i < Values.Count; i++)
                {
                    if (i > 0) w.Write(", ");
                    Values[i].WriteTo(w);
                }
            }
        }
    }

    public sealed class LuaAssignment : LuaNode
    {
        public IReadOnlyList<LuaNode> Targets { get; }
        public IReadOnlyList<LuaNode> Values { get; }
        public LuaAssignment(IReadOnlyList<LuaNode> targets, IReadOnlyList<LuaNode> values)
        {
            Targets = targets; Values = values;
        }

        public override void WriteTo(LuaWriter w)
        {
            for (int i = 0; i < Targets.Count; i++)
            {
                if (i > 0) w.Write(", ");
                Targets[i].WriteTo(w);
            }
            w.Write(" = ");
            for (int i = 0; i < Values.Count; i++)
            {
                if (i > 0) w.Write(", ");
                Values[i].WriteTo(w);
            }
        }
    }

    public sealed class LuaExprStatement : LuaNode
    {
        public LuaNode Expr { get; }
        public LuaExprStatement(LuaNode expr) => Expr = expr;
        public override void WriteTo(LuaWriter w) => Expr.WriteTo(w);
    }

    public sealed class LuaReturn : LuaNode
    {
        public IReadOnlyList<LuaNode> Values { get; }
        public LuaReturn(IReadOnlyList<LuaNode> values) => Values = values;

        public override void WriteTo(LuaWriter w)
        {
            w.Write("return");
            if (Values.Count > 0)
            {
                w.Write(" ");
                for (int i = 0; i < Values.Count; i++)
                {
                    if (i > 0) w.Write(", ");
                    Values[i].WriteTo(w);
                }
            }
        }
    }

    public sealed class LuaBreak : LuaNode
    {
        public static readonly LuaBreak Instance = new();
        public override void WriteTo(LuaWriter w) => w.Write("break");
    }

    public sealed class LuaIfStatement : LuaNode
    {
        public IReadOnlyList<(LuaNode Condition, LuaBlock Body)> Branches { get; }
        public LuaBlock? ElseBranch { get; }

        public LuaIfStatement(
            IReadOnlyList<(LuaNode, LuaBlock)> branches,
            LuaBlock? elseBranch = null)
        {
            Branches = branches; ElseBranch = elseBranch;
        }

        public override void WriteTo(LuaWriter w)
        {
            for (int i = 0; i < Branches.Count; i++)
            {
                var (cond, body) = Branches[i];
                w.Write(i == 0 ? "if " : "elseif ");
                cond.WriteTo(w);
                w.WriteLine(" then");
                w.Indent();
                body.WriteTo(w);
                w.Dedent();
            }
            if (ElseBranch != null)
            {
                w.WriteLine("else");
                w.Indent();
                ElseBranch.WriteTo(w);
                w.Dedent();
            }
            w.Write("end");
        }
    }

    public sealed class LuaWhileLoop : LuaNode
    {
        public LuaNode Condition { get; }
        public LuaBlock Body { get; }
        public LuaWhileLoop(LuaNode condition, LuaBlock body)
        {
            Condition = condition; Body = body;
        }

        public override void WriteTo(LuaWriter w)
        {
            w.Write("while ");
            Condition.WriteTo(w);
            w.WriteLine(" do");
            w.Indent();
            Body.WriteTo(w);
            w.Dedent();
            w.Write("end");
        }
    }

    public sealed class LuaRepeatLoop : LuaNode
    {
        public LuaBlock Body { get; }
        public LuaNode Condition { get; }
        public LuaRepeatLoop(LuaBlock body, LuaNode condition)
        {
            Body = body; Condition = condition;
        }

        public override void WriteTo(LuaWriter w)
        {
            w.WriteLine("repeat");
            w.Indent();
            Body.WriteTo(w);
            w.Dedent();
            w.Write("until ");
            Condition.WriteTo(w);
        }
    }

    public sealed class LuaNumericFor : LuaNode
    {
        public string VarName { get; }
        public LuaNode Start { get; }
        public LuaNode Limit { get; }
        public LuaNode? Step { get; }
        public LuaBlock Body { get; }

        public LuaNumericFor(string varName, LuaNode start, LuaNode limit,
            LuaNode? step, LuaBlock body)
        {
            VarName = varName; Start = start; Limit = limit; Step = step; Body = body;
        }

        public override void WriteTo(LuaWriter w)
        {
            w.Write("for ");
            w.Write(VarName);
            w.Write(" = ");
            Start.WriteTo(w);
            w.Write(", ");
            Limit.WriteTo(w);
            if (Step != null) { w.Write(", "); Step.WriteTo(w); }
            w.WriteLine(" do");
            w.Indent();
            Body.WriteTo(w);
            w.Dedent();
            w.Write("end");
        }
    }

    public sealed class LuaGenericFor : LuaNode
    {
        public IReadOnlyList<string> VarNames { get; }
        public IReadOnlyList<LuaNode> Iterators { get; }
        public LuaBlock Body { get; }

        public LuaGenericFor(IReadOnlyList<string> varNames,
            IReadOnlyList<LuaNode> iterators, LuaBlock body)
        {
            VarNames = varNames; Iterators = iterators; Body = body;
        }

        public override void WriteTo(LuaWriter w)
        {
            w.Write("for ");
            w.Write(string.Join(", ", VarNames));
            w.Write(" in ");
            for (int i = 0; i < Iterators.Count; i++)
            {
                if (i > 0) w.Write(", ");
                Iterators[i].WriteTo(w);
            }
            w.WriteLine(" do");
            w.Indent();
            Body.WriteTo(w);
            w.Dedent();
            w.Write("end");
        }
    }

    public sealed class LuaComment : LuaNode
    {
        public string Text { get; }
        public LuaComment(string text) => Text = text;
        public override void WriteTo(LuaWriter w) => w.WriteLine("-- " + Text);
    }

    // -------------------------------------------------------------------------
    // Top-level declarations
    // -------------------------------------------------------------------------

    /// <summary>
    /// Named function declaration: function ClassName:MethodName(params) body end
    /// or function ClassName.MethodName(params) body end (static)
    /// </summary>
    public sealed class LuaFunctionDecl : LuaNode
    {
        public string ClassName { get; }
        public string MethodName { get; }
        public bool IsInstanceMethod { get; }   // true = colon, false = dot
        public IReadOnlyList<string> Parameters { get; }
        public LuaBlock Body { get; }
        public string? SourceComment { get; }   // optional C# source location

        public LuaFunctionDecl(string className, string methodName, bool isInstanceMethod,
            IReadOnlyList<string> parameters, LuaBlock body, string? sourceComment = null)
        {
            ClassName = className; MethodName = methodName;
            IsInstanceMethod = isInstanceMethod;
            Parameters = parameters; Body = body;
            SourceComment = sourceComment;
        }

        public override void WriteTo(LuaWriter w)
        {
            if (SourceComment != null)
                w.WriteLine("-- " + SourceComment);

            w.Write("function ");
            w.Write(ClassName);
            w.Write(IsInstanceMethod ? ":" : ".");
            w.Write(MethodName);
            w.Write("(");
            w.Write(string.Join(", ", Parameters));
            w.WriteLine(")");
            w.Indent();
            Body.WriteTo(w);
            w.Dedent();
            w.Write("end");
        }
    }

    /// <summary>
    /// Class declaration guard + class({}) assignment:
    ///   if ClassName == nil then ClassName = class({field = value, ...}) end
    /// </summary>
    public sealed class LuaClassDecl : LuaNode
    {
        public string ClassName { get; }
        public IReadOnlyList<LuaTableField> DefaultFields { get; }

        public LuaClassDecl(string className, IReadOnlyList<LuaTableField> defaultFields)
        {
            ClassName = className; DefaultFields = defaultFields;
        }

        public override void WriteTo(LuaWriter w)
        {
            w.Write("if ");
            w.Write(ClassName);
            w.WriteLine(" == nil then");
            w.Indent();
            w.Write(ClassName);
            w.Write(" = class(");
            new LuaTableConstructor(DefaultFields).WriteTo(w);
            w.WriteLine(")");
            w.Dedent();
            w.Write("end");
        }
    }

    /// <summary>
    /// Namespace table guard + member assignment:
    ///   Addon = Addon or {}
    ///   Addon.ClassName = ClassName
    /// </summary>
    public sealed class LuaNamespaceAlias : LuaNode
    {
        public string Namespace { get; }
        public string ClassName { get; }

        public LuaNamespaceAlias(string ns, string className)
        {
            Namespace = ns; ClassName = className;
        }

        public override void WriteTo(LuaWriter w)
        {
            w.Write(Namespace);
            w.Write(" = ");
            w.Write(Namespace);
            w.WriteLine(" or {}");
            w.Write(Namespace);
            w.Write(".");
            w.Write(ClassName);
            w.Write(" = ");
            w.Write(ClassName);
        }
    }

    /// <summary>
    /// Entire compiled Lua file: header comment, requires, class decl, method decls,
    /// and bottom registration alias.
    /// </summary>
    public sealed class LuaFile : LuaNode
    {
        public string FilePath { get; }
        public IReadOnlyList<LuaNode> Nodes { get; }

        public LuaFile(string filePath, IReadOnlyList<LuaNode> nodes)
        {
            FilePath = filePath; Nodes = nodes;
        }

        public override void WriteTo(LuaWriter w)
        {
            foreach (var node in Nodes)
            {
                node.WriteTo(w);
                w.NewLine();
                // Blank line after class decl and after each function
                if (node is LuaClassDecl || node is LuaFunctionDecl)
                    w.NewLine();
            }
        }
    }
}
