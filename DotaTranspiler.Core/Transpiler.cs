using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DotaTranspiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotaTranspiler
{
    public sealed class TranspilerOptions
    {
        /// <summary>Source directory containing C# files.</summary>
        public string SourceDirectory { get; init; } = "";

        /// <summary>Output directory (game/scripts/vscripts/).</summary>
        public string OutputDirectory { get; init; } = "";

        /// <summary>Emit debug comments with C# source locations.</summary>
        public bool Debug { get; init; } = false;

        /// <summary>Addon namespace prefix for generated Lua.</summary>
        public string AddonNamespace { get; init; } = "Addon";
    }

    public sealed class TranspilerResult
    {
        public IReadOnlyList<string> OutputFiles { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        public bool Success => Errors.Count == 0;
    }

    /// <summary>
    /// Main orchestrator. Loads C# source files, builds a Roslyn compilation,
    /// scans for Dota attributes, emits a LuaFile per class, and writes output.
    /// </summary>
    public sealed class Transpiler
    {
        private readonly TranspilerOptions _options;

        public Transpiler(TranspilerOptions options) => _options = options;

        public TranspilerResult Run()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var outputFiles = new List<string>();

            try
            {
                // 1. Collect all C# source files
                var csFiles = Directory.GetFiles(
                    _options.SourceDirectory, "*.cs", SearchOption.AllDirectories);

                if (csFiles.Length == 0)
                {
                    warnings.Add($"No .cs files found in {_options.SourceDirectory}");
                    return new TranspilerResult
                    {
                        OutputFiles = outputFiles,
                        Errors = errors,
                        Warnings = warnings
                    };
                }

                // 2. Parse all files into syntax trees
                var syntaxTrees = csFiles.Select(f =>
                    CSharpSyntaxTree.ParseText(
                        File.ReadAllText(f),
                        new CSharpParseOptions(LanguageVersion.CSharp9),
                        f))
                    .ToArray();

                // 3. Build Roslyn compilation
                //    References: System.Runtime (for base types), plus DotaApi.dll
                var dotaApiPath = FindDotaApiDll();
                var references = BuildReferences(dotaApiPath);

                var compilation = CSharpCompilation.Create(
                    "DotaAddon",
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                        .WithNullableContextOptions(NullableContextOptions.Enable));

                // 4. Report Roslyn diagnostics (real C# errors)
                foreach (var diag in compilation.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    errors.Add($"CS error: {diag.GetMessage()} [{diag.Location.GetLineSpan()}]");
                }

                if (errors.Count > 0)
                    return new TranspilerResult
                    {
                        OutputFiles = outputFiles,
                        Errors = errors,
                        Warnings = warnings
                    };

                // 5. Scan all classes for Dota attributes
                var classes = new List<ClassInfo>();
                foreach (var tree in syntaxTrees)
                {
                    var model = compilation.GetSemanticModel(tree);
                    var root = tree.GetRoot();

                    foreach (var cls in root.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>())
                    {
                        try
                        {
                            var info = ScanClass(cls, model, tree.FilePath);
                            if (info != null)
                            {
                                ValidateClass(cls, info, errors);
                                classes.Add(info);
                            }
                        }
                        catch (TranspilerException ex)
                        {
                            errors.Add(ex.Message);
                        }
                    }
                }

                if (errors.Count > 0)
                    return new TranspilerResult
                    {
                        OutputFiles = outputFiles,
                        Errors = errors,
                        Warnings = warnings
                    };

                // 6. Emit Lua for each class
                Directory.CreateDirectory(_options.OutputDirectory);

                foreach (var cls in classes)
                {
                    try
                    {
                        var tree = syntaxTrees.First(t => t.FilePath == cls.SourceFile);
                        var model = compilation.GetSemanticModel(tree);
                        var emitter = new ClassEmitter(model, _options.Debug);
                        var luaFile = emitter.Emit(cls);

                        var outPath = Path.Combine(_options.OutputDirectory, luaFile.FilePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                        var sb = new StringBuilder();
                        using var writer = new LuaWriter(sb);
                        luaFile.WriteTo(writer);

                        File.WriteAllText(outPath, sb.ToString());
                        outputFiles.Add(outPath);
                    }
                    catch (TranspilerException ex)
                    {
                        errors.Add(ex.Message);
                    }
                }

                // 7. Generate addon_game_mode.lua entry point
                var gameModeClass = classes.FirstOrDefault(c => c.Kind == DotaClassKind.GameMode);
                GenerateEntryPoint(classes, gameModeClass, outputFiles, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Internal transpiler error: {ex.Message}\n{ex.StackTrace}");
            }

            return new TranspilerResult
            {
                OutputFiles = outputFiles,
                Errors = errors,
                Warnings = warnings
            };
        }

        // -------------------------------------------------------------------------
        // Attribute scanning
        // -------------------------------------------------------------------------

        private static ClassInfo? ScanClass(
            ClassDeclarationSyntax cls,
            SemanticModel model,
            string sourceFile)
        {
            var attrs = cls.AttributeLists
                .SelectMany(al => al.Attributes)
                .ToList();

            string? luaName = null;
            DotaClassKind? kind = null;
            string? scriptFile = null;
            string? linkModifierPath = null;

            foreach (var attr in attrs)
            {
                var attrName = attr.Name.ToString().Replace("Attribute", "");

                switch (attrName)
                {
                    case "DotaAbility":
                        kind = DotaClassKind.Ability;
                        luaName = GetFirstStringArg(attr);
                        break;

                    case "DotaModifier":
                        kind = DotaClassKind.Modifier;
                        luaName = GetFirstStringArg(attr);
                        break;

                    case "GameMode":
                        kind = DotaClassKind.GameMode;
                        luaName = cls.Identifier.Text;
                        break;

                    case "ScriptFile":
                        scriptFile = GetFirstStringArg(attr);
                        break;

                    case "LinkModifier":
                        linkModifierPath = GetFirstStringArg(attr);
                        break;
                }
            }

            if (kind == null) return null; // Not a Dota class

            luaName ??= cls.Identifier.Text;
            scriptFile ??= InferScriptFilePath(luaName, kind.Value);

            // Check for reserved global name collision
            if (ReservedGlobals.Names.Contains(luaName))
                throw new TranspilerException(
                    $"Class Lua name '{luaName}' collides with a Dota/Lua reserved global. " +
                    "Choose a different name.",
                    cls.GetLocation());

            // Scan method-level attributes for modifier property/state declarations
            var propertyMethods = new List<ModifierPropertyEntry>();
            var stateMethods = new List<ModifierStateEntry>();
            var hotPathMethods = new HashSet<string>();

            if (kind == DotaClassKind.Modifier)
            {
                // OnIntervalThink is always hot-path
                hotPathMethods.Add("OnIntervalThink");

                foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
                {
                    foreach (var methodAttr in method.AttributeLists
                        .SelectMany(al => al.Attributes))
                    {
                        var methodAttrName = methodAttr.Name.ToString()
                            .Replace("Attribute", "");

                        if (methodAttrName == "ModifierProperty")
                        {
                            var propConstant = GetEnumArgAsLuaConstant(
                                methodAttr, "MODIFIER_PROPERTY_");
                            if (propConstant != null)
                            {
                                propertyMethods.Add(new ModifierPropertyEntry
                                {
                                    MethodName = method.Identifier.Text,
                                    PropertyConstant = propConstant,
                                });
                                // Property getters are hot-path
                                hotPathMethods.Add(method.Identifier.Text);
                            }
                        }
                        else if (methodAttrName == "ModifierState")
                        {
                            var stateConstant = GetEnumArgAsLuaConstant(
                                methodAttr, "MODIFIER_STATE_");
                            if (stateConstant != null)
                            {
                                stateMethods.Add(new ModifierStateEntry
                                {
                                    MethodName = method.Identifier.Text,
                                    StateConstant = stateConstant,
                                });
                            }
                        }
                    }
                }
            }

            return new ClassInfo
            {
                Kind = kind.Value,
                LuaName = luaName,
                ScriptFilePath = scriptFile,
                LinkModifierPath = linkModifierPath,
                Syntax = cls,
                SourceFile = sourceFile,
                PropertyMethods = propertyMethods,
                StateMethods = stateMethods,
                HotPathMethods = hotPathMethods,
            };
        }

        /// <summary>
        /// Reads an enum attribute argument and converts it to a Lua global constant name.
        /// E.g. ModifierProperty.MOVESPEED_BONUS_FLAT → "MODIFIER_PROPERTY_MOVESPEED_BONUS_FLAT"
        ///      ModifierState.ROOTED → "MODIFIER_STATE_ROOTED"
        /// </summary>
        private static string? GetEnumArgAsLuaConstant(
            AttributeSyntax attr, string prefix)
        {
            var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
            if (arg == null) return null;

            // MemberAccessExpression: ModifierProperty.MOVESPEED_BONUS_FLAT
            if (arg.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var memberName = memberAccess.Name.Identifier.Text;
                return prefix + memberName;
            }

            // Simple identifier: MOVESPEED_BONUS_FLAT (if using a direct value)
            if (arg.Expression is IdentifierNameSyntax id)
                return prefix + id.Identifier.Text;

            return null;
        }

        private static string? GetFirstStringArg(AttributeSyntax attr)
        {
            var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
            if (arg?.Expression is LiteralExpressionSyntax lit
                && lit.IsKind(SyntaxKind.StringLiteralExpression))
                return lit.Token.ValueText;
            return null;
        }

        private static string InferScriptFilePath(string luaName, DotaClassKind kind) =>
            kind switch
            {
                DotaClassKind.Ability => $"abilities/{luaName}.lua",
                DotaClassKind.Modifier => $"modifiers/{luaName}.lua",
                DotaClassKind.GameMode => $"gamemode/{luaName}.lua",
                _ => $"{luaName}.lua"
            };

        // -------------------------------------------------------------------------
        // Class validation (Phase 1 + Phase 3 subset enforcement)
        // -------------------------------------------------------------------------

        private static void ValidateClass(
            ClassDeclarationSyntax cls,
            ClassInfo info,
            List<string> errors)
        {
            // ── Inheritance ──────────────────────────────────────────────────
            if (cls.BaseList != null)
            {
                foreach (var baseType in cls.BaseList.Types)
                {
                    var typeName = baseType.Type.ToString();
                    bool isAllowedBase = typeName is "IDOTAAbility" or "IDOTAModifier"
                        or "IDOTAGameMode" or "DotaVScripts.IDOTAAbility"
                        or "DotaVScripts.IDOTAModifier" or "DotaVScripts.IDOTAGameMode";

                    if (!isAllowedBase)
                        errors.Add(
                            $"{cls.Identifier.Text}: Inheritance from '{typeName}' is not " +
                            $"supported. Dota transpiler classes must be flat. " +
                            $"Use [DotaAbility], [DotaModifier], or [GameMode] attributes " +
                            $"instead of base class inheritance. " +
                            $"[{cls.GetLocation().GetLineSpan()}]");
                }
            }

            // ── Constructors ─────────────────────────────────────────────────
            var ctors = cls.Members.OfType<ConstructorDeclarationSyntax>().ToList();
            if (ctors.Count > 1)
                errors.Add(
                    $"{cls.Identifier.Text}: Multiple constructors are not supported. " +
                    $"Use OnCreated(kv) for modifier initialization. " +
                    $"[{cls.GetLocation().GetLineSpan()}]");

            var staticCtors = ctors.Where(c =>
                c.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))).ToList();
            if (staticCtors.Any())
                errors.Add(
                    $"{cls.Identifier.Text}: Static constructors are not supported. " +
                    $"[{cls.GetLocation().GetLineSpan()}]");

            // ── Class modifiers ──────────────────────────────────────────────
            if (cls.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)))
                errors.Add(
                    $"{cls.Identifier.Text}: Abstract classes are not supported. " +
                    $"[{cls.GetLocation().GetLineSpan()}]");

            // ── Nested classes ───────────────────────────────────────────────
            if (cls.Members.OfType<ClassDeclarationSyntax>().Any())
                errors.Add(
                    $"{cls.Identifier.Text}: Nested classes are not supported. " +
                    $"Move nested classes to top level. " +
                    $"[{cls.GetLocation().GetLineSpan()}]");

            // ── Empty Lua name ───────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(info.LuaName))
                errors.Add(
                    $"{cls.Identifier.Text}: The Lua class name cannot be empty. " +
                    $"[{cls.GetLocation().GetLineSpan()}]");

            // ── Duplicate Dota attributes ─────────────────────────────────────
            var dotaAttrNames = cls.AttributeLists
                .SelectMany(al => al.Attributes)
                .Select(a => a.Name.ToString().Replace("Attribute", ""))
                .Where(n => n is "DotaAbility" or "DotaModifier" or "GameMode")
                .ToList();

            if (dotaAttrNames.Count > 1)
                errors.Add(
                    $"{cls.Identifier.Text}: Multiple Dota class attributes found: " +
                    $"{string.Join(", ", dotaAttrNames.Select(n => $"[{n}]"))}. " +
                    $"A class can only be one kind. " +
                    $"[{cls.GetLocation().GetLineSpan()}]");

            // ── Method-level diagnostics ──────────────────────────────────────
            foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                ValidateMethod(cls, method, info, errors);
            }

            // ── [ModifierProperty]/[ModifierState] on non-modifier class ─────
            if (info.Kind != DotaClassKind.Modifier)
            {
                foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
                {
                    foreach (var attr in method.AttributeLists
                        .SelectMany(al => al.Attributes))
                    {
                        var attrName = attr.Name.ToString().Replace("Attribute", "");
                        if (attrName is "ModifierProperty" or "ModifierState")
                            errors.Add(
                                $"{cls.Identifier.Text}.{method.Identifier.Text}: " +
                                $"[{attrName}] can only be used on a class decorated with [DotaModifier]. " +
                                $"[{method.GetLocation().GetLineSpan()}]");
                    }
                }
            }
        }

        private static void ValidateMethod(
            ClassDeclarationSyntax cls,
            MethodDeclarationSyntax method,
            ClassInfo info,
            List<string> errors)
        {
            var methodName = $"{cls.Identifier.Text}.{method.Identifier.Text}";
            var loc = method.GetLocation().GetLineSpan();

            // ── async methods ────────────────────────────────────────────────
            if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
                errors.Add(
                    $"{methodName}: async methods are not supported. " +
                    $"Dota vscripts are single-threaded and synchronous. " +
                    $"Use Timers:CreateTimer() for deferred execution. " +
                    $"[{loc}]");

            // ── ref / in / out parameters ────────────────────────────────────
            foreach (var param in method.ParameterList.Parameters)
            {
                bool hasRef = param.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword));
                bool hasOut = param.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword));
                bool hasIn  = param.Modifiers.Any(m => m.IsKind(SyntaxKind.InKeyword));

                if (hasRef || hasOut || hasIn)
                {
                    var modifier = hasRef ? "ref" : hasOut ? "out" : "in";
                    errors.Add(
                        $"{methodName}: Parameter '{param.Identifier.Text}' uses '{modifier}', " +
                        $"which is not supported. Lua has no pass-by-reference semantics. " +
                        $"[{param.GetLocation().GetLineSpan()}]");
                }
            }

            // ── abstract methods on user classes ─────────────────────────────
            // (user abstract classes are already blocked at class level,
            //  but check at method level too for clarity)
            if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword))
                && !cls.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)))
                errors.Add(
                    $"{methodName}: Abstract methods are not supported. " +
                    $"[{loc}]");

            // ── Scan method body for unsupported statement kinds ─────────────
            if (method.Body != null)
                ValidateMethodBody(method.Body, methodName, errors);
        }

        private static void ValidateMethodBody(
            BlockSyntax block, string methodName, List<string> errors)
        {
            foreach (var node in block.DescendantNodes())
            {
                switch (node)
                {
                    // Locked statements — no threading
                    case LockStatementSyntax lockStmt:
                        errors.Add(
                            $"{methodName}: lock statements are not supported. " +
                            $"Dota Lua is single-threaded. " +
                            $"[{lockStmt.GetLocation().GetLineSpan()}]");
                        break;

                    // Using statement (IDisposable) — no RAII in Lua
                    case UsingStatementSyntax usingStmt:
                        errors.Add(
                            $"{methodName}: using statements are not supported. " +
                            $"Lua has no deterministic disposal. " +
                            $"[{usingStmt.GetLocation().GetLineSpan()}]");
                        break;

                    // C# 8 `using var x = ...` declaration form
                    case LocalDeclarationStatementSyntax localDecl
                        when localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword):
                        errors.Add(
                            $"{methodName}: 'using var' declarations are not supported. " +
                            $"Lua has no deterministic disposal. " +
                            $"[{localDecl.GetLocation().GetLineSpan()}]");
                        break;

                    // unsafe blocks
                    case UnsafeStatementSyntax unsafeStmt:
                        errors.Add(
                            $"{methodName}: unsafe blocks are not supported. " +
                            $"[{unsafeStmt.GetLocation().GetLineSpan()}]");
                        break;

                    // fixed blocks
                    case FixedStatementSyntax fixedStmt:
                        errors.Add(
                            $"{methodName}: fixed blocks are not supported. " +
                            $"[{fixedStmt.GetLocation().GetLineSpan()}]");
                        break;

                    // goto — not in Lua 5.1
                    case GotoStatementSyntax gotoStmt:
                        errors.Add(
                            $"{methodName}: goto is not supported in Lua 5.1. " +
                            $"[{gotoStmt.GetLocation().GetLineSpan()}]");
                        break;

                    // yield return / yield break — no coroutines in this subset
                    case YieldStatementSyntax yieldStmt:
                        errors.Add(
                            $"{methodName}: yield return/break is not supported. " +
                            $"[{yieldStmt.GetLocation().GetLineSpan()}]");
                        break;

                    // switch expression (pattern matching) — not supported
                    case SwitchStatementSyntax switchStmt
                        when HasNonLiteralCases(switchStmt):
                        errors.Add(
                            $"{methodName}: switch with pattern matching cases is not supported. " +
                            $"Rewrite using if/else if chains. " +
                            $"[{switchStmt.GetLocation().GetLineSpan()}]");
                        break;

                    // await expression inside method body
                    case ExpressionStatementSyntax exprStmt
                        when exprStmt.Expression is AwaitExpressionSyntax:
                        errors.Add(
                            $"{methodName}: await is not supported. " +
                            $"[{exprStmt.GetLocation().GetLineSpan()}]");
                        break;

                    // BCL type usage detected via identifier names
                    case MemberAccessExpressionSyntax maExpr
                        when maExpr.Expression is IdentifierNameSyntax idSyntax
                            && IsBlockedBclType(idSyntax.Identifier.Text):
                        errors.Add(
                            $"{methodName}: '{idSyntax.Identifier.Text}' is from the .NET BCL " +
                            $"which is not available in Dota vscripts. " +
                            $"[{maExpr.GetLocation().GetLineSpan()}]");
                        break;
                }
            }
        }

        private static bool HasNonLiteralCases(SwitchStatementSyntax switchStmt) =>
            switchStmt.Sections.Any(s => s.Labels.Any(
                l => l is CasePatternSwitchLabelSyntax));

        private static bool IsBlockedBclType(string name) => name is
            "File" or "Directory" or "Path" or "Stream" or "FileStream"
            or "StreamReader" or "StreamWriter" or "Console"
            or "Thread" or "Task" or "Parallel" or "ThreadPool"
            or "HttpClient" or "WebClient" or "Socket"
            or "Assembly" or "MethodInfo" or "PropertyInfo"
            or "Environment" or "Process" or "AppDomain"
            or "Regex" or "StringBuilder";  // StringBuilder: use string.format instead

        // -------------------------------------------------------------------------
        // Entry point generation
        // -------------------------------------------------------------------------

        private void GenerateEntryPoint(
            IReadOnlyList<ClassInfo> classes,
            ClassInfo? gameModeClass,
            List<string> outputFiles,
            List<string> errors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- Generated by DotaTranspiler");
            sb.AppendLine("-- addon_game_mode.lua — entry point");
            sb.AppendLine();
            sb.AppendLine("require(\"DotaCompat\")");
            sb.AppendLine();

            // Require all generated files
            foreach (var cls in classes.Where(c => c.Kind != DotaClassKind.GameMode))
            {
                // Convert path to Lua require format (strip .lua, use dots or slashes)
                var requirePath = cls.ScriptFilePath
                    .Replace(".lua", "")
                    .Replace("\\", "/");
                sb.AppendLine($"require(\"{requirePath}\")");
            }

            if (gameModeClass != null)
            {
                var requirePath = gameModeClass.ScriptFilePath
                    .Replace(".lua", "")
                    .Replace("\\", "/");
                sb.AppendLine($"require(\"{requirePath}\")");
            }

            sb.AppendLine();

            // LinkLuaModifier calls for all modifiers with [LinkModifier]
            foreach (var cls in classes.Where(c => c.Kind == DotaClassKind.Modifier
                && c.LinkModifierPath != null))
            {
                sb.AppendLine(
                    $"LinkLuaModifier(\"{cls.LuaName}\", \"{cls.LinkModifierPath}\", " +
                    $"DOTA_MODIFIER_LUA_MOTION_NONE)");
            }

            sb.AppendLine();
            sb.AppendLine("function Precache(context)");
            sb.AppendLine("end");
            sb.AppendLine();
            sb.AppendLine("function Activate()");

            if (gameModeClass != null)
            {
                sb.AppendLine($"    local gm = {gameModeClass.LuaName}()");
                sb.AppendLine("    gm:InitGameMode()");
            }
            else
            {
                sb.AppendLine("    -- No [GameMode] class found. Add InitGameMode logic here.");
            }

            sb.AppendLine("end");

            var entryPath = Path.Combine(_options.OutputDirectory, "addon_game_mode.lua");
            File.WriteAllText(entryPath, sb.ToString());
            outputFiles.Add(entryPath);
        }

        // -------------------------------------------------------------------------
        // Roslyn references
        // -------------------------------------------------------------------------

        private static IEnumerable<MetadataReference> BuildReferences(string? dotaApiPath)
        {
            // Core .NET runtime assemblies
            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment
                .GetRuntimeDirectory();

            var refs = new List<MetadataReference>();

            void TryAdd(string path)
            {
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }

            TryAdd(Path.Combine(runtimeDir, "System.Runtime.dll"));
            TryAdd(Path.Combine(runtimeDir, "System.Collections.dll"));
            TryAdd(Path.Combine(runtimeDir, "System.Linq.dll"));
            TryAdd(Path.Combine(runtimeDir, "netstandard.dll"));

            // mscorlib / object fundamentals
            TryAdd(typeof(object).Assembly.Location);
            TryAdd(typeof(System.Collections.Generic.List<>).Assembly.Location);

            // DotaApi.dll stubs
            if (dotaApiPath != null && File.Exists(dotaApiPath))
                refs.Add(MetadataReference.CreateFromFile(dotaApiPath));

            return refs;
        }

        private string? FindDotaApiDll()
        {
            // Look relative to the source directory and the executing assembly
            var candidates = new[]
            {
                Path.Combine(_options.SourceDirectory, "DotaApi.dll"),
                Path.Combine(_options.SourceDirectory, "..", "DotaApi", "bin",
                    "Debug", "net8.0", "DotaApi.dll"),
                Path.Combine(AppContext.BaseDirectory, "DotaApi.dll"),
            };

            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
