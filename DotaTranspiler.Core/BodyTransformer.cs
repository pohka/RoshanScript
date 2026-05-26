using System;
using System.Collections.Generic;
using System.Linq;
using DotaTranspiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotaTranspiler
{
    /// <summary>
    /// Transforms C# method bodies (statements and expressions) into LuaNode IR.
    /// Called per-method by the ClassEmitter.
    /// </summary>
    public sealed class BodyTransformer
    {
        private readonly SemanticModel _model;
        private readonly bool _emitDebugComments;

        // Names of C# identifiers that map to Dota globals and need no prefix
        private static readonly HashSet<string> GlobalFunctionNames = new(StringComparer.Ordinal)
        {
            "ListenToGameEvent", "StopListeningToGameEvent", "LinkLuaModifier",
            "FindUnitsInRadius", "CreateUnitByName", "CreateUnitByNameAsync",
            "CreateItemByName", "EmitGlobalSound", "EmitSoundOn",
            "EmitSoundOnLocationWithCaster", "DebugPrint", "DebugDrawCircle",
            "DebugDrawLine", "RandomFloat", "RandomInt", "Time", "FrameTime",
            "GetGroundPosition", "IsLocationVisible", "MinimapEvent",
            "Localize", "UnitFilter", "ApplyDamage",
        };

        public BodyTransformer(SemanticModel model, bool emitDebugComments = false)
        {
            _model = model;
            _emitDebugComments = emitDebugComments;
        }

        // -------------------------------------------------------------------------
        // Statements
        // -------------------------------------------------------------------------

        public IReadOnlyList<LuaNode> TransformBlock(BlockSyntax block)
        {
            var nodes = new List<LuaNode>();
            foreach (var stmt in block.Statements)
                nodes.AddRange(TransformStatement(stmt));
            return nodes;
        }

        private IEnumerable<LuaNode> TransformStatement(StatementSyntax stmt)
        {
            if (_emitDebugComments)
            {
                var line = stmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var file = stmt.GetLocation().GetLineSpan().Path;
                yield return new LuaComment($"[{System.IO.Path.GetFileName(file)}:{line}]");
            }

            switch (stmt)
            {
                case LocalDeclarationStatementSyntax local:
                    foreach (var n in TransformLocalDecl(local)) yield return n;
                    break;

                case ExpressionStatementSyntax expr:
                    if (expr.Expression is AwaitExpressionSyntax)
                        throw new TranspilerException(
                            "async/await is not supported. Dota vscripts are single-threaded " +
                            "and synchronous. Use Timers:CreateTimer() for deferred execution.",
                            expr.GetLocation());
                    yield return new LuaExprStatement(TransformExpression(expr.Expression));
                    break;

                case ReturnStatementSyntax ret:
                    if (ret.Expression != null)
                        yield return new LuaReturn(new[] { TransformExpression(ret.Expression) });
                    else
                        yield return new LuaReturn(Array.Empty<LuaNode>());
                    break;

                case IfStatementSyntax ifStmt:
                    yield return TransformIf(ifStmt);
                    break;

                case WhileStatementSyntax whileStmt:
                    yield return TransformWhile(whileStmt);
                    break;

                case DoStatementSyntax doStmt:
                    yield return TransformDo(doStmt);
                    break;

                case ForStatementSyntax forStmt:
                    foreach (var n in TransformFor(forStmt)) yield return n;
                    break;

                case ForEachStatementSyntax forEach:
                    yield return TransformForEach(forEach);
                    break;

                case BlockSyntax innerBlock:
                    // Bare block — Lua has no block scoping outside control flow, just flatten
                    foreach (var inner in innerBlock.Statements)
                        foreach (var n in TransformStatement(inner))
                            yield return n;
                    break;

                case BreakStatementSyntax:
                    yield return LuaBreak.Instance;
                    break;

                case ContinueStatementSyntax:
                    throw new TranspilerException(
                        "continue is not supported in Lua 5.1. " +
                        "Refactor using a boolean flag or restructure the loop condition.",
                        stmt.GetLocation());

                case EmptyStatementSyntax:
                    break;

                default:
                    throw new TranspilerException(
                        $"Unsupported statement type: {stmt.GetType().Name}. " +
                        $"Only a subset of C# is supported by the Dota transpiler.",
                        stmt.GetLocation());
            }
        }

        private IEnumerable<LuaNode> TransformLocalDecl(LocalDeclarationStatementSyntax local)
        {
            foreach (var v in local.Declaration.Variables)
            {
                var value = v.Initializer != null
                    ? TransformExpression(v.Initializer.Value)
                    : LuaNil.Instance;
                yield return new LuaLocalDecl(
                    new[] { v.Identifier.Text },
                    new[] { value });
            }
        }

        private LuaIfStatement TransformIf(IfStatementSyntax ifStmt)
        {
            var branches = new List<(LuaNode, LuaBlock)>();
            LuaBlock? elseBranch = null;

            // Collect if + elseif chain
            StatementSyntax? current = ifStmt;
            while (current is IfStatementSyntax branch)
            {
                var cond = TransformExpression(branch.Condition);
                var body = WrapInBlock(branch.Statement);
                branches.Add((cond, body));
                current = branch.Else?.Statement;
            }

            if (current != null && current is not IfStatementSyntax)
                elseBranch = WrapInBlock(current);

            return new LuaIfStatement(branches, elseBranch);
        }

        private LuaWhileLoop TransformWhile(WhileStatementSyntax w) =>
            new(TransformExpression(w.Condition), WrapInBlock(w.Statement));

        private LuaRepeatLoop TransformDo(DoStatementSyntax d)
        {
            // C# do-while: execute first, then check condition
            // Lua repeat-until: execute first, stop when condition is TRUE
            // Invert condition: do { body } while(cond) -> repeat body until not(cond)
            var cond = new LuaUnary("not",
                new LuaParenthesized(TransformExpression(d.Condition)));
            return new LuaRepeatLoop(WrapInBlock(d.Statement), cond);
        }

        private IEnumerable<LuaNode> TransformFor(ForStatementSyntax f)
        {
            // Try to detect simple numeric for: for (int i = start; i < limit; i++) or i++
            // Otherwise fall back to while loop equivalent
            if (f.Declaration?.Variables.Count == 1
                && f.Initializers.Count == 0
                && f.Incrementors.Count == 1
                && f.Condition != null)
            {
                var variable = f.Declaration.Variables[0];
                var varName = variable.Identifier.Text;

                if (TryGetNumericForParts(f, varName, out var start, out var limit, out var step))
                {
                    var body = WrapInBlock(f.Statement);
                    yield return new LuaNumericFor(varName, start!, limit!, step, body);
                    yield break;
                }
            }

            // Fallback: emit initializer locals + while loop
            if (f.Declaration != null)
                foreach (var v in f.Declaration.Variables)
                {
                    var val = v.Initializer != null
                        ? TransformExpression(v.Initializer.Value)
                        : LuaNil.Instance;
                    yield return new LuaLocalDecl(new[] { v.Identifier.Text }, new[] { val });
                }

            foreach (var init in f.Initializers)
                yield return new LuaExprStatement(TransformExpression(init));

            var whileBody = new List<LuaNode>(
                WrapInBlock(f.Statement).Statements);
            foreach (var inc in f.Incrementors)
                whileBody.Add(new LuaExprStatement(TransformExpression(inc)));

            var condition = f.Condition != null
                ? TransformExpression(f.Condition)
                : LuaTrue.Instance;

            yield return new LuaWhileLoop(condition, new LuaBlock(whileBody));
        }

        private bool TryGetNumericForParts(ForStatementSyntax f, string varName,
            out LuaNode? start, out LuaNode? limit, out LuaNode? step)
        {
            start = limit = step = null;

            if (f.Declaration?.Variables[0].Initializer == null) return false;
            start = TransformExpression(f.Declaration.Variables[0].Initializer!.Value);

            // Condition must be i < expr or i <= expr
            if (f.Condition is not BinaryExpressionSyntax condBin) return false;
            if (condBin.Left is not IdentifierNameSyntax condLeft
                || condLeft.Identifier.Text != varName) return false;

            limit = TransformExpression(condBin.Right);
            // Adjust limit for <= : already correct; for < we subtract 1 at emit time by wrapping
            bool isStrictLess = condBin.IsKind(SyntaxKind.LessThanExpression);
            bool isLessOrEqual = condBin.IsKind(SyntaxKind.LessThanOrEqualExpression);
            if (!isStrictLess && !isLessOrEqual) return false;

            if (isStrictLess)
                limit = new LuaBinary(limit, "-", new LuaNumber(1));

            // Incrementor must be i++ or i += constant
            var inc = f.Incrementors[0];
            if (inc is PostfixUnaryExpressionSyntax post
                && post.IsKind(SyntaxKind.PostIncrementExpression)
                && post.Operand is IdentifierNameSyntax postId
                && postId.Identifier.Text == varName)
            {
                step = null; // default step of 1
                return true;
            }

            if (inc is AssignmentExpressionSyntax assign
                && assign.IsKind(SyntaxKind.AddAssignmentExpression)
                && assign.Left is IdentifierNameSyntax assignId
                && assignId.Identifier.Text == varName)
            {
                step = TransformExpression(assign.Right);
                return true;
            }

            return false;
        }

        private LuaGenericFor TransformForEach(ForEachStatementSyntax forEach)
        {
            var iterExpr = TransformExpression(forEach.Expression);
            var body = WrapInBlock(forEach.Statement);

            // Determine iterator function: ipairs for known list types, pairs otherwise
            var typeInfo = _model.GetTypeInfo(forEach.Expression);
            var iteratorFn = IsArrayLikeType(typeInfo.Type)
                ? "ipairs"
                : "pairs";

            return new LuaGenericFor(
                new[] { "_", forEach.Identifier.Text },
                new[] { new LuaCall(new LuaIdentifier(iteratorFn), new[] { iterExpr }) },
                body);
        }

        private static bool IsArrayLikeType(ITypeSymbol? type)
        {
            if (type == null) return false;
            if (type is IArrayTypeSymbol) return true;
            var name = type.Name;
            return name is "List" or "IList" or "IEnumerable" or "IReadOnlyList" or "Array";
        }

        private LuaBlock WrapInBlock(StatementSyntax stmt)
        {
            var nodes = stmt is BlockSyntax block
                ? block.Statements.SelectMany(TransformStatement).ToList()
                : TransformStatement(stmt).ToList();
            return new LuaBlock(nodes);
        }

        // -------------------------------------------------------------------------
        // Expressions
        // -------------------------------------------------------------------------

        public LuaNode TransformExpression(ExpressionSyntax expr)
        {
            return expr switch
            {
                LiteralExpressionSyntax lit => TransformLiteral(lit),
                IdentifierNameSyntax id => TransformIdentifier(id),
                ThisExpressionSyntax => new LuaIdentifier("self"),
                PrefixUnaryExpressionSyntax pre => TransformPrefixUnary(pre),
                PostfixUnaryExpressionSyntax post => TransformPostfixUnary(post),
                // Coalesce must come before BinaryExpression (more specific first)
                BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.CoalesceExpression) =>
                    TransformCoalesce(bin),
                BinaryExpressionSyntax bin => TransformBinary(bin),
                AssignmentExpressionSyntax assign => TransformAssignment(assign),
                MemberAccessExpressionSyntax member => TransformMemberAccess(member),
                InvocationExpressionSyntax invoke => TransformInvocation(invoke),
                ElementAccessExpressionSyntax elem => TransformElementAccess(elem),
                ParenthesizedExpressionSyntax paren =>
                    new LuaParenthesized(TransformExpression(paren.Expression)),
                ConditionalExpressionSyntax ternary => TransformTernary(ternary),
                CastExpressionSyntax cast => TransformCast(cast),
                ObjectCreationExpressionSyntax ctor => TransformObjectCreation(ctor),
                DefaultExpressionSyntax => LuaNil.Instance,
                ConditionalAccessExpressionSyntax condAccess =>
                    TransformConditionalAccess(condAccess),
                // new T[] { ... } or new T[0] → Lua table constructor
                ArrayCreationExpressionSyntax arrayCreate =>
                    TransformArrayCreation(arrayCreate),
                // new T[] { } shorthand
                ImplicitArrayCreationExpressionSyntax implicitArray =>
                    TransformImplicitArrayCreation(implicitArray),

                // await expression — hard error
                AwaitExpressionSyntax awaitExpr => throw new TranspilerException(
                    "async/await is not supported. Dota vscripts are single-threaded and synchronous. " +
                    "Use Timers:CreateTimer() for deferred execution.",
                    awaitExpr.GetLocation()),

                // typeof() — no runtime type system in Lua 5.1
                TypeOfExpressionSyntax typeofExpr => throw new TranspilerException(
                    "typeof() is not supported. Lua 5.1 has no runtime type system.",
                    typeofExpr.GetLocation()),

                _ => throw new TranspilerException(
                    $"Unsupported expression type: {expr.GetType().Name}",
                    expr.GetLocation())
            };
        }

        private static LuaNode TransformLiteral(LiteralExpressionSyntax lit) =>
            lit.Kind() switch
            {
                SyntaxKind.NumericLiteralToken or
                SyntaxKind.NumericLiteralExpression =>
                    new LuaNumber(Convert.ToDouble(lit.Token.Value)),

                SyntaxKind.StringLiteralExpression =>
                    new LuaString(lit.Token.ValueText),

                SyntaxKind.TrueLiteralExpression => LuaTrue.Instance,
                SyntaxKind.FalseLiteralExpression => LuaFalse.Instance,
                SyntaxKind.NullLiteralExpression => LuaNil.Instance,
                SyntaxKind.CharacterLiteralExpression =>
                    new LuaString(lit.Token.ValueText),

                _ => throw new TranspilerException(
                    $"Unsupported literal kind: {lit.Kind()}",
                    lit.GetLocation())
            };

        private LuaNode TransformIdentifier(IdentifierNameSyntax id)
        {
            var name = id.Identifier.Text;

            // Use the semantic model to resolve what this identifier refers to.
            // If it resolves to a field or property of the enclosing class,
            // rewrite it as self.fieldName — this fixes the Phase 2 gap where
            // bare field references like `tickInterval` were not being prefixed.
            var symbol = _model.GetSymbolInfo(id).Symbol;
            if (symbol != null)
            {
                switch (symbol.Kind)
                {
                    case SymbolKind.Field:
                    case SymbolKind.Property:
                        // Only rewrite instance members (not static, not from DotaApi types)
                        if (!symbol.IsStatic
                            && symbol.ContainingType != null
                            && !IsDotaApiType(symbol.ContainingType))
                        {
                            return new LuaMemberAccess(
                                new LuaIdentifier("self"), name);
                        }
                        break;

                    case SymbolKind.Method:
                        // Bare method name used as a value (e.g. passed as callback).
                        // Rewrite to self.MethodName (dot, not colon — value reference).
                        if (!symbol.IsStatic
                            && symbol.ContainingType != null
                            && !IsDotaApiType(symbol.ContainingType))
                        {
                            return new LuaMemberAccess(
                                new LuaIdentifier("self"), name);
                        }
                        break;
                }
            }

            return new LuaIdentifier(name);
        }

        /// <summary>
        /// Returns true if the type is one of the DotaApi stub types (ICDOTA_*, etc.)
        /// whose members should never be rewritten to self.X — they're engine handles.
        /// </summary>
        private static bool IsDotaApiType(INamedTypeSymbol type)
        {
            var ns = type.ContainingNamespace?.ToString() ?? "";
            return ns == "DotaVScripts" || type.Name.StartsWith("ICDOTA", StringComparison.Ordinal)
                || type.Name.StartsWith("CDOTA", StringComparison.Ordinal)
                || type.Name.StartsWith("ICDota", StringComparison.Ordinal);
        }

        private LuaNode TransformPrefixUnary(PrefixUnaryExpressionSyntax pre)
        {
            var operand = TransformExpression(pre.Operand);
            return pre.Kind() switch
            {
                SyntaxKind.UnaryMinusExpression => new LuaUnary("-", operand),
                SyntaxKind.LogicalNotExpression => new LuaUnary("not", operand),
                // ++i → i = i + 1; value is i+1. Emit as binary for expression context.
                SyntaxKind.PreIncrementExpression =>
                    new LuaBinary(operand, "+", new LuaNumber(1)),
                SyntaxKind.PreDecrementExpression =>
                    new LuaBinary(operand, "-", new LuaNumber(1)),
                _ => throw new TranspilerException(
                    $"Unsupported prefix operator: {pre.Kind()}",
                    pre.GetLocation())
            };
        }

        private LuaNode TransformPostfixUnary(PostfixUnaryExpressionSyntax post)
        {
            // i++ and i-- are only valid as statements in generated code.
            // Transform to assignment: i = i + 1
            var operand = TransformExpression(post.Operand);
            var op = post.Kind() == SyntaxKind.PostIncrementExpression ? "+" : "-";
            return new LuaAssignment(
                new[] { operand },
                new[] { (LuaNode)new LuaBinary(operand, op, new LuaNumber(1)) });
        }

        private LuaNode TransformBinary(BinaryExpressionSyntax bin)
        {
            var left = TransformExpression(bin.Left);
            var right = TransformExpression(bin.Right);

            // String concatenation: + on strings → ..
            if (bin.IsKind(SyntaxKind.AddExpression) && IsStringType(bin.Left))
                return new LuaBinary(left, "..", right);

            var op = bin.Kind() switch
            {
                SyntaxKind.AddExpression => "+",
                SyntaxKind.SubtractExpression => "-",
                SyntaxKind.MultiplyExpression => "*",
                SyntaxKind.DivideExpression => "/",
                SyntaxKind.ModuloExpression => "%",
                SyntaxKind.EqualsExpression => "==",
                SyntaxKind.NotEqualsExpression => "~=",
                SyntaxKind.LessThanExpression => "<",
                SyntaxKind.LessThanOrEqualExpression => "<=",
                SyntaxKind.GreaterThanExpression => ">",
                SyntaxKind.GreaterThanOrEqualExpression => ">=",
                SyntaxKind.LogicalAndExpression => "and",
                SyntaxKind.LogicalOrExpression => "or",
                _ => throw new TranspilerException(
                    $"Unsupported binary operator: {bin.Kind()}. " +
                    "Note: bitwise operators require DotaCompat.band() etc.",
                    bin.GetLocation())
            };

            return new LuaBinary(left, op, right);
        }

        private bool IsStringType(ExpressionSyntax expr)
        {
            var ti = _model.GetTypeInfo(expr);
            return ti.Type?.SpecialType == SpecialType.System_String;
        }

        private LuaNode TransformAssignment(AssignmentExpressionSyntax assign)
        {
            var target = TransformExpression(assign.Left);
            LuaNode value;

            if (assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                value = TransformExpression(assign.Right);
            }
            else
            {
                // Compound: +=, -=, *=, /= → expand to target = target op right
                var right = TransformExpression(assign.Right);
                var op = assign.Kind() switch
                {
                    SyntaxKind.AddAssignmentExpression => "+",
                    SyntaxKind.SubtractAssignmentExpression => "-",
                    SyntaxKind.MultiplyAssignmentExpression => "*",
                    SyntaxKind.DivideAssignmentExpression => "/",
                    SyntaxKind.ModuloAssignmentExpression => "%",
                    _ => throw new TranspilerException(
                        $"Unsupported compound assignment: {assign.Kind()}",
                        assign.GetLocation())
                };
                value = new LuaBinary(target, op, right);
            }

            return new LuaAssignment(new[] { target }, new[] { value });
        }

        private LuaNode TransformMemberAccess(MemberAccessExpressionSyntax member)
        {
            var memberName = member.Name.Identifier.Text;

            // DotaSingletons.GameRules → GameRules (bare global)
            if (member.Expression is IdentifierNameSyntax dotaSingletons
                && dotaSingletons.Identifier.Text == "DotaSingletons")
                return new LuaIdentifier(memberName);

            // Dota.XXX static calls are handled in TransformInvocation for method calls.
            if (member.Expression is IdentifierNameSyntax dotaId
                && dotaId.Identifier.Text == "Dota")
                return new LuaIdentifier(memberName);

            // Block BCL namespace access — no OS, IO, networking in Dota Lua
            if (member.Expression is IdentifierNameSyntax bcl)
            {
                var blocked = GetBlockedBclNamespace(bcl.Identifier.Text);
                if (blocked != null)
                    throw new TranspilerException(
                        $"Access to '{bcl.Identifier.Text}' is not supported. " +
                        $"{blocked} " +
                        "Dota vscripts have no access to the OS, file system, or network.",
                        member.GetLocation());
            }

            // Enum member access: AbilityBehavior.X, ModifierProperty.X etc.
            // → emit the member name as a bare Lua global constant
            if (member.Expression is IdentifierNameSyntax enumTypeName)
            {
                var typeName = enumTypeName.Identifier.Text;
                if (IsKnownDotaEnumType(typeName))
                    return new LuaIdentifier(memberName);
            }

            // Fully-qualified enum: DotaVScripts.AbilityBehavior.X
            if (member.Expression is MemberAccessExpressionSyntax qualifiedEnum
                && qualifiedEnum.Expression is IdentifierNameSyntax namespaceName
                && namespaceName.Identifier.Text == "DotaVScripts"
                && IsKnownDotaEnumType(qualifiedEnum.Name.Identifier.Text))
            {
                return new LuaIdentifier(memberName);
            }

            var obj = TransformExpression(member.Expression);
            return new LuaMemberAccess(obj, memberName);
        }

        private static string? GetBlockedBclNamespace(string name) => name switch
        {
            "File" or "Directory" or "Path" or "Stream"
                or "FileStream" or "StreamReader" or "StreamWriter"
                => "System.IO types are not available.",

            "Console"
                => "System.Console is not available. Use DebugPrint() instead.",

            "Thread" or "Task" or "Parallel" or "ThreadPool"
                => "System.Threading types are not available. Dota Lua is single-threaded.",

            "HttpClient" or "WebClient" or "Socket" or "TcpClient" or "UdpClient"
                => "Networking types are not available.",

            "Reflection" or "Assembly" or "Type"
                => "System.Reflection is not available. Lua 5.1 has no runtime reflection.",

            "Environment" or "Process" or "AppDomain"
                => "System process/environment types are not available.",

            "Math"
                => null,  // Math is fine — maps to Lua math library

            _ => null
        };

        private static bool IsKnownDotaEnumType(string typeName) =>
            typeName is "AbilityBehavior"
                or "ModifierProperty"
                or "ModifierState"
                or "ModifierAttribute"
                or "TeamNumber"
                or "LuaModifierType"
                or "DotaClassKind";   // internal, shouldn't appear but guard anyway

        private LuaNode TransformInvocation(InvocationExpressionSyntax invoke)
        {
            var args = invoke.ArgumentList.Arguments
                .Select(a => TransformExpression(a.Expression))
                .ToArray();

            // Case 1: Dota.GlobalFunction(args) → GlobalFunction(args)
            if (invoke.Expression is MemberAccessExpressionSyntax dotaAccess
                && dotaAccess.Expression is IdentifierNameSyntax dotaId
                && dotaId.Identifier.Text == "Dota")
            {
                var fnName = dotaAccess.Name.Identifier.Text;

                // ListenToGameEvent self-context rewrite
                if (fnName == "ListenToGameEvent" && args.Length >= 2)
                    return TransformListenToGameEvent(invoke, args);

                return new LuaCall(new LuaIdentifier(fnName), args);
            }

            // Case 2: DotaSingletons.GameRules.SomeMethod(args) → GameRules:SomeMethod(args)
            if (invoke.Expression is MemberAccessExpressionSyntax outerAccess)
            {
                var methodName = outerAccess.Name.Identifier.Text;
                var target = TransformExpression(outerAccess.Expression);

                // ListenToGameEvent special binding:
                // Dota.ListenToGameEvent("event", Handler, this)
                // → ListenToGameEvent("event", Dynamic.ClassName.Handler, self)
                // The third argument (context) when it's `this` stays as `self`.
                // The second argument (callback) when it's a method group needs
                // to reference the class method as a function value.
                // We handle this in the bare-call path below; this branch is for
                // handle method calls.

                bool isHandleCall = IsHandleType(outerAccess.Expression);
                if (isHandleCall)
                    return new LuaMethodCall(target, methodName, args);
                else
                    return new LuaCall(new LuaMemberAccess(target, methodName), args);
            }

            // Case 3: bare function call (global function or local)
            if (invoke.Expression is IdentifierNameSyntax idName)
            {
                var fnName = idName.Identifier.Text;

                // ListenToGameEvent("event_name", MethodName, this) →
                // ListenToGameEvent("event_name", Dynamic.ClassName.MethodName, self)
                // We detect this by the function name and rewrite the args
                if (fnName == "ListenToGameEvent" && args.Length >= 2)
                    return TransformListenToGameEvent(invoke, args);

                return new LuaCall(new LuaIdentifier(fnName), args);
            }

            throw new TranspilerException(
                $"Unsupported invocation expression: {invoke.Expression.GetType().Name}",
                invoke.GetLocation());
        }

        private bool IsHandleType(ExpressionSyntax expr)
        {
            var typeInfo = _model.GetTypeInfo(expr);
            var type = typeInfo.Type;
            if (type == null) return false;

            // Handle interfaces (ICDOTA_*, ICDOTAGameRules, etc.)
            if (type.TypeKind == TypeKind.Interface)
                return type.Name.StartsWith("I", StringComparison.Ordinal);

            // self (this) is always colon-call
            if (expr is ThisExpressionSyntax) return true;

            return false;
        }

        /// <summary>
        /// Rewrites ListenToGameEvent("event", CallbackMethod, this) →
        /// ListenToGameEvent("event", ClassName.CallbackMethod, self)
        ///
        /// Dota requires the callback to be a method reference (dot access, not colon)
        /// and the context to be the table that owns it. We pass the class table
        /// itself as the context so Dota can invoke callback(context, event_data).
        /// </summary>
        private LuaNode TransformListenToGameEvent(
            InvocationExpressionSyntax invoke,
            LuaNode[] args)
        {
            var rewrittenArgs = args.ToList();

            // Arg 1 (index 1) is the callback. If the user wrote `OnSomeEvent`
            // (a bare method name), we need to rewrite it to `self.OnSomeEvent`
            // so Dota passes `self` as the first argument when calling back.
            if (args.Length >= 2 && args[1] is LuaIdentifier callbackId)
            {
                // Rewrite to self.MethodName — dot not colon, Dota handles context
                rewrittenArgs[1] = new LuaMemberAccess(
                    new LuaIdentifier("self"), callbackId.Name);
            }

            // Arg 2 (index 2) is the context. If it was `this`, it's already been
            // transformed to `self` by the time we get here. Leave it as-is.

            return new LuaCall(
                new LuaIdentifier("ListenToGameEvent"),
                rewrittenArgs);
        }

        private LuaNode TransformElementAccess(ElementAccessExpressionSyntax elem)
        {
            // In Lua, arrays are 1-based. We don't auto-offset here —
            // the developer is responsible for 1-based indexing in Dota Lua.
            var table = TransformExpression(elem.Expression);

            if (elem.ArgumentList.Arguments.Count != 1)
                throw new TranspilerException(
                    "Multi-dimensional array access is not supported",
                    elem.GetLocation());

            var indexExpr = TransformExpression(
                elem.ArgumentList.Arguments[0].Expression);
            return new LuaIndexAccess(table, indexExpr);
        }

        private LuaNode TransformTernary(ConditionalExpressionSyntax ternary)
        {
            // C# a ? b : c → Lua: (condition and b or c)
            // Note: this has the classic Lua pitfall where b=false gives wrong result.
            // Acceptable for Phase 1; document in warnings.
            var cond = TransformExpression(ternary.Condition);
            var whenTrue = TransformExpression(ternary.WhenTrue);
            var whenFalse = TransformExpression(ternary.WhenFalse);
            return new LuaParenthesized(
                new LuaBinary(
                    new LuaBinary(cond, "and", whenTrue),
                    "or",
                    whenFalse));
        }

        private LuaNode TransformCast(CastExpressionSyntax cast)
        {
            var inner = TransformExpression(cast.Expression);
            var typeName = cast.Type.ToString();

            // int/long cast → math.floor
            if (typeName is "int" or "long" or "Int32" or "Int64")
                return new LuaCall(
                    new LuaMemberAccess(new LuaIdentifier("math"), "floor"),
                    new[] { inner });

            // float/double cast → identity (Lua numbers are already float)
            if (typeName is "float" or "double" or "Single" or "Double")
                return inner;

            // Other casts: emit as identity with a comment
            return inner;
        }

        private static LuaNode TransformObjectCreation(ObjectCreationExpressionSyntax ctor)
        {
            var typeName = ctor.Type.ToString();
            if (typeName is "Vector" or "QAngle")
            {
                var args = ctor.ArgumentList?.Arguments
                    .Select(a => (LuaNode)new LuaIdentifier(a.Expression.ToString()))
                    .ToArray() ?? Array.Empty<LuaNode>();
                return new LuaCall(new LuaIdentifier(typeName), args);
            }

            throw new TranspilerException(
                $"Object creation for type '{typeName}' is not supported. " +
                "Only Vector and QAngle constructors are allowed in Dota vscripts.",
                ctor.GetLocation());
        }

        private LuaNode TransformArrayCreation(ArrayCreationExpressionSyntax arrayCreate)
        {
            // new T[] { a, b, c } or new T[0] → { a, b, c } or {}
            if (arrayCreate.Initializer != null)
            {
                var fields = arrayCreate.Initializer.Expressions
                    .Select(e => new LuaTableField(TransformExpression(e)))
                    .ToList<LuaTableField>();
                return new LuaTableConstructor(fields);
            }
            // new T[0] or new T[n] with no initializer → empty table
            return new LuaTableConstructor(new List<LuaTableField>());
        }

        private LuaNode TransformImplicitArrayCreation(
            ImplicitArrayCreationExpressionSyntax implicitArray)
        {
            // new[] { a, b, c } → { a, b, c }
            var fields = implicitArray.Initializer.Expressions
                .Select(e => new LuaTableField(TransformExpression(e)))
                .ToList<LuaTableField>();
            return new LuaTableConstructor(fields);
        }

        private LuaNode TransformConditionalAccess(ConditionalAccessExpressionSyntax condAccess)
        {
            // x?.Method(args) →
            //   (x ~= nil and x:Method(args) or nil)
            var receiver = TransformExpression(condAccess.Expression);
            var notNilCheck = new LuaBinary(receiver, "~=", LuaNil.Instance);

            // The WhenNotNull part is a MemberBindingExpression or ElementBindingExpression
            LuaNode whenNotNull;
            if (condAccess.WhenNotNull is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberBindingExpressionSyntax binding)
            {
                var args = invocation.ArgumentList.Arguments
                    .Select(a => TransformExpression(a.Expression))
                    .ToArray();
                whenNotNull = new LuaMethodCall(receiver, binding.Name.Identifier.Text, args);
            }
            else if (condAccess.WhenNotNull is MemberBindingExpressionSyntax propBinding)
            {
                whenNotNull = new LuaMemberAccess(receiver, propBinding.Name.Identifier.Text);
            }
            else
            {
                throw new TranspilerException(
                    "Only simple null-conditional method calls and property access are supported",
                    condAccess.GetLocation());
            }

            return new LuaParenthesized(
                new LuaBinary(
                    new LuaBinary(notNilCheck, "and", whenNotNull),
                    "or",
                    LuaNil.Instance));
        }

        private LuaNode TransformCoalesce(BinaryExpressionSyntax coalesce)
        {
            // x ?? y → (x ~= nil and x or y)
            var left = TransformExpression(coalesce.Left);
            var right = TransformExpression(coalesce.Right);
            return new LuaParenthesized(
                new LuaBinary(
                    new LuaBinary(left, "~=", LuaNil.Instance),
                    "and",
                    new LuaBinary(left, "or", right)));
        }
    }
}
