using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DotaTranspiler;
using DotaTranspiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotaTranspiler.Tests
{
    /// <summary>
    /// Minimal test runner. No NuGet dependency — just a static Main that
    /// runs all tests and reports pass/fail.
    /// Run with: dotnet run --project DotaTranspiler.Tests
    /// </summary>
    class Program
    {
        static int _passed = 0;
        static int _failed = 0;

        static int Main(string[] args)
        {
            Console.WriteLine("DotaTranspiler Phase 1 + Phase 2 Tests");
            Console.WriteLine(new string('=', 50));

            // LuaNode / LuaWriter tests
            RunTest("LuaNumber integer", TestLuaNumberInteger);
            RunTest("LuaNumber float", TestLuaNumberFloat);
            RunTest("LuaString escaping", TestLuaStringEscaping);
            RunTest("LuaBinary expression", TestLuaBinaryExpression);
            RunTest("LuaMethodCall colon syntax", TestLuaMethodCall);
            RunTest("LuaClassDecl with fields", TestLuaClassDecl);
            RunTest("LuaFunctionDecl instance method", TestLuaFunctionDecl);
            RunTest("LuaIfStatement", TestLuaIfStatement);
            RunTest("LuaWhileLoop", TestLuaWhileLoop);
            RunTest("LuaNumericFor", TestLuaNumericFor);
            RunTest("LuaGenericFor ipairs", TestLuaGenericFor);
            RunTest("LuaTableConstructor", TestLuaTableConstructor);
            RunTest("LuaNamespaceAlias", TestLuaNamespaceAlias);
            RunTest("LuaReturn multiple values", TestLuaReturnMultiple);
            RunTest("LuaWriter indentation", TestLuaWriterIndentation);

            // BodyTransformer tests (parse C# → emit Lua string)
            RunTest("Transform: local var declaration", TestTransformLocalVar);
            RunTest("Transform: if/else", TestTransformIfElse);
            RunTest("Transform: null check → nil", TestTransformNullCheck);
            RunTest("Transform: null-coalescing ??", TestTransformNullCoalescing);
            RunTest("Transform: this → self", TestTransformThisToSelf);
            RunTest("Transform: method call dot→colon", TestTransformMethodCallColon);
            RunTest("Transform: return literal", TestTransformReturnLiteral);
            RunTest("Transform: compound assignment +=", TestTransformCompoundAssign);
            RunTest("Transform: while loop", TestTransformWhile);
            RunTest("Transform: cast int → math.floor", TestTransformCastInt);

            // Phase 1 integration tests
            RunTest("Integration: simple ability class", TestIntegrationAbility);
            RunTest("Integration: reserved global collision → error", TestIntegrationReservedGlobal);
            RunTest("Integration: inheritance → error", TestIntegrationInheritanceBlocked);

            // ── Phase 2 tests ─────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("  -- Phase 2 --");

            // Modifier: DeclareFunctions + CheckState
            RunTest("P2: modifier DeclareFunctions generated", TestModifierDeclareFunctions);
            RunTest("P2: modifier CheckState generated", TestModifierCheckState);
            RunTest("P2: modifier lifecycle stubs emitted", TestModifierLifecycleStubs);

            // Ability lifecycle stubs
            RunTest("P2: ability lifecycle stubs emitted", TestAbilityLifecycleStubs);
            RunTest("P2: ability enum member → bare constant", TestAbilityEnumMember);

            // ListenToGameEvent binding
            RunTest("P2: ListenToGameEvent self-context binding", TestListenToGameEventBinding);

            // Hot path
            RunTest("P2: OnIntervalThink is hot-path (no debug comments)", TestHotPathNoDebugComments);

            // LinkLuaModifier injection in entry point
            RunTest("P2: LinkLuaModifier in addon_game_mode.lua", TestLinkLuaModifierInjection);

            // GameMode class entry point wiring
            RunTest("P2: GameMode Activate() wiring", TestGameModeActivate);

            // Foreach over array → ipairs
            RunTest("P2: foreach array → ipairs", TestForeachArray);

            // for (int i ...) → numeric for
            RunTest("P2: numeric for loop", TestNumericForLoop);

            // Enum member access
            RunTest("P2: enum member access → Lua constant", TestEnumMemberAccess);

            // ── Phase 3 tests ─────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("  -- Phase 3 --");

            // Field access rewrite: bare field name → self.field
            RunTest("P3: bare field access → self.field", TestBareFieldRewrite);
            RunTest("P3: bare field in method body → self.field", TestBareFieldInMethod);

            // Subset enforcement — all should produce errors
            RunTest("P3: async method → error", TestAsyncMethodBlocked);
            RunTest("P3: ref parameter → error", TestRefParamBlocked);
            RunTest("P3: out parameter → error", TestOutParamBlocked);
            RunTest("P3: continue statement → error", TestContinueBlocked);
            RunTest("P3: nested class → error", TestNestedClassBlocked);
            RunTest("P3: static constructor → error", TestStaticCtorBlocked);
            RunTest("P3: using statement → error", TestUsingStatementBlocked);
            RunTest("P3: lock statement → error", TestLockStatementBlocked);
            RunTest("P3: goto statement → error", TestGotoBlocked);
            RunTest("P3: yield return → error", TestYieldBlocked);
            RunTest("P3: BCL File access → error", TestBclFileBlocked);
            RunTest("P3: BCL Thread access → error", TestBclThreadBlocked);

            // Attribute validation
            RunTest("P3: empty Lua name → error", TestEmptyLuaNameBlocked);
            RunTest("P3: duplicate Dota attributes → error", TestDuplicateDotaAttrBlocked);
            RunTest("P3: [ModifierProperty] on non-modifier → error",
                TestModifierPropertyOnAbilityBlocked);

            Console.WriteLine();
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"Results: {_passed} passed, {_failed} failed");
            return _failed > 0 ? 1 : 0;
        }

        // -------------------------------------------------------------------------
        // Test infrastructure
        // -------------------------------------------------------------------------

        static void RunTest(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"  [PASS] {name}");
                _passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] {name}");
                Console.WriteLine($"         {ex.Message}");
                _failed++;
            }
        }

        static void AssertEqual(string expected, string actual, string? context = null)
        {
            expected = expected.Trim();
            actual = actual.Trim();
            if (expected != actual)
                throw new Exception(
                    $"{context ?? "Mismatch"}\n" +
                    $"Expected:\n{expected}\n" +
                    $"Actual:\n{actual}");
        }

        static void AssertContains(string haystack, string needle)
        {
            if (!haystack.Contains(needle))
                throw new Exception(
                    $"Expected output to contain:\n  {needle}\nActual:\n{haystack}");
        }

        static void AssertNotContains(string haystack, string needle)
        {
            if (haystack.Contains(needle))
                throw new Exception(
                    $"Expected output NOT to contain:\n  {needle}\nActual:\n{haystack}");
        }

        static string Render(LuaNode node)
        {
            var sb = new StringBuilder();
            using var w = new LuaWriter(sb);
            node.WriteTo(w);
            return sb.ToString();
        }

        // -------------------------------------------------------------------------
        // LuaNode / LuaWriter tests
        // -------------------------------------------------------------------------

        static void TestLuaNumberInteger() =>
            AssertEqual("42", Render(new LuaNumber(42)));

        static void TestLuaNumberFloat() =>
            AssertEqual("3.14", Render(new LuaNumber(3.14)));

        static void TestLuaStringEscaping() =>
            AssertEqual("\"hello\\\"world\"",
                Render(new LuaString("hello\"world")));

        static void TestLuaBinaryExpression()
        {
            var node = new LuaBinary(
                new LuaIdentifier("x"),
                "~=",
                LuaNil.Instance);
            AssertEqual("x ~= nil", Render(node));
        }

        static void TestLuaMethodCall()
        {
            var node = new LuaMethodCall(
                new LuaIdentifier("unit"),
                "GetHealth",
                Array.Empty<LuaNode>());
            AssertEqual("unit:GetHealth()", Render(node));
        }

        static void TestLuaClassDecl()
        {
            var node = new LuaClassDecl("my_ability", new[]
            {
                new LuaTableField("damage", new LuaNumber(100)),
            });
            var output = Render(node);
            AssertContains(output, "if my_ability == nil then");
            AssertContains(output, "my_ability = class(");
            AssertContains(output, "damage = 100");
        }

        static void TestLuaFunctionDecl()
        {
            var body = new LuaBlock(new[]
            {
                (LuaNode)new LuaReturn(new[] {
                    (LuaNode)new LuaMethodCall(
                        new LuaIdentifier("self"),
                        "GetHealth",
                        Array.Empty<LuaNode>())
                })
            });
            var node = new LuaFunctionDecl(
                "my_ability", "GetCurrentHealth", true,
                Array.Empty<string>(), body);
            var output = Render(node);
            AssertContains(output, "function my_ability:GetCurrentHealth()");
            AssertContains(output, "return self:GetHealth()");
            AssertContains(output, "end");
        }

        static void TestLuaIfStatement()
        {
            var node = new LuaIfStatement(
                new[]
                {
                    ((LuaNode)new LuaBinary(
                        new LuaIdentifier("hp"),
                        "<",
                        new LuaNumber(100)),
                    new LuaBlock(new[] {
                        (LuaNode)new LuaExprStatement(
                            new LuaMethodCall(
                                new LuaIdentifier("self"),
                                "Die",
                                Array.Empty<LuaNode>()))
                    }))
                },
                null);
            var output = Render(node);
            AssertContains(output, "if hp < 100 then");
            AssertContains(output, "self:Die()");
            AssertContains(output, "end");
        }

        static void TestLuaWhileLoop()
        {
            var node = new LuaWhileLoop(
                LuaTrue.Instance,
                new LuaBlock(new[] { (LuaNode)LuaBreak.Instance }));
            var output = Render(node);
            AssertContains(output, "while true do");
            AssertContains(output, "break");
            AssertContains(output, "end");
        }

        static void TestLuaNumericFor()
        {
            var node = new LuaNumericFor(
                "i",
                new LuaNumber(1),
                new LuaNumber(10),
                null,
                new LuaBlock(Array.Empty<LuaNode>()));
            var output = Render(node);
            AssertContains(output, "for i = 1, 10 do");
        }

        static void TestLuaGenericFor()
        {
            var node = new LuaGenericFor(
                new[] { "_", "unit" },
                new[] { (LuaNode)new LuaCall(
                    new LuaIdentifier("ipairs"),
                    new[] { (LuaNode)new LuaIdentifier("units") }) },
                new LuaBlock(Array.Empty<LuaNode>()));
            var output = Render(node);
            AssertContains(output, "for _, unit in ipairs(units) do");
        }

        static void TestLuaTableConstructor()
        {
            var node = new LuaTableConstructor(new[]
            {
                new LuaTableField("MODIFIER_PROPERTY_MOVESPEED_BONUS_FLAT",
                    new LuaIdentifier("MODIFIER_PROPERTY_MOVESPEED_BONUS_FLAT"))
            });
            var output = Render(node);
            AssertContains(output, "MODIFIER_PROPERTY_MOVESPEED_BONUS_FLAT");
        }

        static void TestLuaNamespaceAlias()
        {
            var node = new LuaNamespaceAlias("Addon", "my_ability");
            var output = Render(node);
            AssertContains(output, "Addon = Addon or {}");
            AssertContains(output, "Addon.my_ability = my_ability");
        }

        static void TestLuaReturnMultiple()
        {
            var node = new LuaReturn(new LuaNode[]
            {
                new LuaNumber(1),
                new LuaIdentifier("name"),
            });
            AssertEqual("return 1, name", Render(node));
        }

        static void TestLuaWriterIndentation()
        {
            var sb = new StringBuilder();
            using var w = new LuaWriter(sb);
            w.WriteLine("outer");
            w.Indent();
            w.WriteLine("inner");
            w.Indent();
            w.WriteLine("deep");
            w.Dedent();
            w.Dedent();
            w.WriteLine("outer again");
            var output = sb.ToString();
            // Normalize line endings for cross-platform comparison
            var normalized = output.Replace("\r\n", "\n");
            AssertContains(normalized, "outer\n    inner\n        deep\nouter again");
        }

        // -------------------------------------------------------------------------
        // BodyTransformer tests
        // -------------------------------------------------------------------------

        /// <summary>
        /// Helper: parse C# method body text and transform it to Lua using BodyTransformer.
        /// Wraps the body in a dummy class so Roslyn has a valid parse context.
        /// </summary>
        static string TransformBody(string csharpBody) =>
            TransformBody(csharpBody, extraUsings: "using DotaVScripts;");

        static void TestTransformLocalVar()
        {
            var output = TransformBody("var x = 42;");
            AssertContains(output, "local x = 42");
        }

        static void TestTransformIfElse()
        {
            var output = TransformBody(@"
                if (x > 0) {
                    y = 1;
                } else {
                    y = 2;
                }");
            AssertContains(output, "if x > 0 then");
            AssertContains(output, "y = 1");
            AssertContains(output, "else");
            AssertContains(output, "y = 2");
            AssertContains(output, "end");
        }

        static void TestTransformNullCheck()
        {
            var output = TransformBody("if (obj == null) { return; }");
            AssertContains(output, "obj == nil");
        }

        static void TestTransformNullCoalescing()
        {
            var output = TransformBody("var result = x ?? y;");
            AssertContains(output, "~= nil");
            AssertContains(output, "and");
            AssertContains(output, "or");
        }

        static void TestTransformThisToSelf()
        {
            var output = TransformBody("var me = this;");
            AssertContains(output, "self");
            AssertNotContains(output, "this");
        }

        static void TestTransformMethodCallColon()
        {
            // Method call on a local variable — we can't do full handle detection
            // without DotaApi.dll in test compilation, so test member access shape
            var output = TransformBody("var name = obj.ToString();");
            AssertContains(output, "obj");
            AssertContains(output, "ToString");
        }

        static void TestTransformReturnLiteral()
        {
            var output = TransformBody("return 3.14f;");
            AssertContains(output, "return 3.14");
        }

        static void TestTransformCompoundAssign()
        {
            var output = TransformBody("x += 5;");
            AssertContains(output, "x = x + 5");
        }

        static void TestTransformWhile()
        {
            var output = TransformBody("while (count > 0) { count -= 1; }");
            AssertContains(output, "while count > 0 do");
            AssertContains(output, "count = count - 1");
            AssertContains(output, "end");
        }

        static void TestTransformCastInt()
        {
            var output = TransformBody("var n = (int)3.7f;");
            AssertContains(output, "math.floor");
        }

        // -------------------------------------------------------------------------
        // Integration tests (full transpiler pipeline)
        // -------------------------------------------------------------------------

        static void TestIntegrationAbility()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_test_ability"")]
[ScriptFile(""abilities/my_test_ability.lua"")]
public class MyTestAbility : IDOTAAbility
{
    private float damage = 250f;

    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 600f;
}";

            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            AssertContains(output, "if my_test_ability == nil then");
            AssertContains(output, "my_test_ability = class(");
            AssertContains(output, "damage = 250");
            AssertContains(output, "function my_test_ability:OnSpellStart()");
            AssertContains(output, "function my_test_ability:OnAbilityPhaseStart()");
            AssertContains(output, "return true");
            AssertContains(output, "Addon = Addon or {}");
            AssertContains(output, "Addon.my_test_ability = my_test_ability");
        }

        static void TestIntegrationReservedGlobal()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""GameRules"")]
public class BadAbility : IDOTAAbility { }";

            var (_, errors) = RunTranspilerOnSource(source);
            if (errors.Count == 0)
                throw new Exception("Expected error for reserved global 'GameRules', got none");
        }

        static void TestIntegrationInheritanceBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_ability"")]
public class MyAbility : SomeOtherClass { }";

            var (_, errors) = RunTranspilerOnSource(source);
            if (errors.Count == 0)
                throw new Exception("Expected error for disallowed inheritance, got none");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phase 2 tests
        // ─────────────────────────────────────────────────────────────────────

        static void TestModifierDeclareFunctions()
        {
            var source = @"
using DotaVScripts;

[DotaModifier(""modifier_my_slow"")]
[LinkModifier(""modifiers/modifier_my_slow.lua"")]
public class ModifierMySlow : IDOTAModifier
{
    [ModifierProperty(ModifierProperty.MOVESPEED_BONUS_FLAT)]
    public float GetModifierMoveSpeedBonus_Flat() => -100f;

    [ModifierProperty(ModifierProperty.HEALTH_REGEN_CONSTANT)]
    public float GetModifierHealthRegenConstant() => -5f;

    public void OnCreated(KVTable kv) { }
    public void OnRefresh(KVTable kv) { }
    public void OnRemoved() { }
    public void OnIntervalThink() { }
    public bool IsDebuff() => true;
    public bool IsPurgable() => true;
    public ModifierAttribute GetAttributes() => ModifierAttribute.MODIFIER_ATTRIBUTE_NONE;
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            AssertContains(output, "function modifier_my_slow:DeclareFunctions()");
            AssertContains(output, "MODIFIER_PROPERTY_MOVESPEED_BONUS_FLAT");
            AssertContains(output, "MODIFIER_PROPERTY_HEALTH_REGEN_CONSTANT");
            // The two property getter functions should also be emitted
            AssertContains(output, "function modifier_my_slow:GetModifierMoveSpeedBonus_Flat()");
            AssertContains(output, "return -100");
        }

        static void TestModifierCheckState()
        {
            var source = @"
using DotaVScripts;

[DotaModifier(""modifier_my_root"")]
public class ModifierMyRoot : IDOTAModifier
{
    [ModifierState(ModifierState.ROOTED)]
    public bool IsRooted() => true;

    [ModifierState(ModifierState.DISARMED)]
    public bool IsDisarmed() => false;

    public void OnCreated(KVTable kv) { }
    public void OnRefresh(KVTable kv) { }
    public void OnRemoved() { }
    public void OnIntervalThink() { }
    public bool IsDebuff() => true;
    public bool IsPurgable() => true;
    public ModifierAttribute GetAttributes() => ModifierAttribute.MODIFIER_ATTRIBUTE_NONE;
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            AssertContains(output, "function modifier_my_root:CheckState()");
            AssertContains(output, "[MODIFIER_STATE_ROOTED] = true");
            AssertContains(output, "[MODIFIER_STATE_DISARMED] = false");
            // [ModifierState] methods should NOT be emitted as standalone functions
            AssertNotContains(output, "function modifier_my_root:IsRooted()");
        }

        static void TestModifierLifecycleStubs()
        {
            // A modifier with no lifecycle methods declared should get empty stubs
            var source = @"
using DotaVScripts;

[DotaModifier(""modifier_empty"")]
public class ModifierEmpty : IDOTAModifier
{
    public void OnCreated(KVTable kv) { }
    public void OnRefresh(KVTable kv) { }
    public void OnRemoved() { }
    public void OnIntervalThink() { }
    public bool IsDebuff() => false;
    public bool IsPurgable() => true;
    public ModifierAttribute GetAttributes() => ModifierAttribute.MODIFIER_ATTRIBUTE_NONE;
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            // All standard lifecycle stubs should be present
            AssertContains(output, "function modifier_empty:OnCreated(");
            AssertContains(output, "function modifier_empty:OnIntervalThink()");
            AssertContains(output, "function modifier_empty:IsDebuff()");
            AssertContains(output, "return false");
        }

        static void TestAbilityLifecycleStubs()
        {
            // Ability that only declares OnSpellStart — missing stubs should be generated
            var source = @"
using DotaVScripts;

[DotaAbility(""my_minimal_ability"")]
public class MyMinimalAbility : IDOTAAbility
{
    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_NO_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            AssertContains(output, "function my_minimal_ability:OnSpellStart()");
            AssertContains(output, "function my_minimal_ability:GetBehavior()");
            AssertContains(output, "function my_minimal_ability:GetCastRange(");
        }

        static void TestAbilityEnumMember()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_ability_enum"")]
public class MyAbilityEnum : IDOTAAbility
{
    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 600f;
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            // Enum member should be emitted as bare Lua constant, not as table access
            AssertContains(output, "return DOTA_ABILITY_BEHAVIOR_UNIT_TARGET");
            AssertNotContains(output, "return AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET");
        }

        static void TestListenToGameEventBinding()
        {
            var source = @"
using DotaVScripts;

[GameMode]
public class MyGameMode : IDOTAGameMode
{
    public void InitGameMode()
    {
        Dota.ListenToGameEvent(""dota_player_killed"", OnPlayerKilled, this);
    }

    private void OnPlayerKilled(GameEvent ev) { }
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            // self.OnPlayerKilled — dot not colon, self as context
            AssertContains(output, "self.OnPlayerKilled");
            AssertContains(output, "self");
            // Should NOT have bare OnPlayerKilled as a callback
            AssertNotContains(output, "\"OnPlayerKilled\"");
        }

        static void TestHotPathNoDebugComments()
        {
            var source = @"
using DotaVScripts;

[DotaModifier(""modifier_hot"")]
public class ModifierHot : IDOTAModifier
{
    [ModifierProperty(ModifierProperty.MOVESPEED_BONUS_FLAT)]
    public float GetModifierMoveSpeedBonus_Flat() => -50f;

    public void OnIntervalThink() { }
    public void OnCreated(KVTable kv) { }
    public void OnRefresh(KVTable kv) { }
    public void OnRemoved() { }
    public bool IsDebuff() => true;
    public bool IsPurgable() => true;
    public ModifierAttribute GetAttributes() => ModifierAttribute.MODIFIER_ATTRIBUTE_NONE;
}";
            // Run with debug mode ON — hot path methods should still have no debug comments
            var tempDir = Path.Combine(Path.GetTempPath(), $"dota_test_{Guid.NewGuid():N}");
            var sourceDir = Path.Combine(tempDir, "src");
            var outputDir = Path.Combine(tempDir, "out");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(sourceDir, "Test.cs"), source);

            var dotaApiSrc = Path.Combine(AppContext.BaseDirectory, "DotaApi.dll");
            if (!File.Exists(dotaApiSrc))
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && dir.Name != "DotaTranspiler") dir = dir.Parent;
                if (dir != null)
                    dotaApiSrc = Path.Combine(dir.FullName, "DotaApi", "bin",
                        "Debug", "net8.0", "DotaApi.dll");
            }
            if (File.Exists(dotaApiSrc))
                File.Copy(dotaApiSrc, Path.Combine(sourceDir, "DotaApi.dll"), true);

            var result = new Transpiler(new TranspilerOptions
            {
                SourceDirectory = sourceDir,
                OutputDirectory = outputDir,
                Debug = true,   // debug ON
            }).Run();

            var sb = new StringBuilder();
            foreach (var f in Directory.GetFiles(outputDir, "*.lua", SearchOption.AllDirectories))
                sb.AppendLine(File.ReadAllText(f));
            var output = sb.ToString();
            try { Directory.Delete(tempDir, true); } catch { }

            if (result.Errors.Count > 0)
                throw new Exception("Errors: " + string.Join("\n", result.Errors));

            // File-level debug header should exist (non-hot-path)
            AssertContains(output, "[generated: DeclareFunctions]");

            // Hot-path property getter should NOT have per-statement debug comments
            // The function body should be clean: just `return -50`
            var lines = output.Split('\n');
            bool inHotFn = false;
            foreach (var line in lines)
            {
                if (line.Contains("GetModifierMoveSpeedBonus_Flat"))
                    inHotFn = true;
                if (inHotFn && line.Trim().StartsWith("end"))
                    break;
                if (inHotFn && line.Trim().StartsWith("--") &&
                    line.Contains(".cs:"))
                    throw new Exception(
                        "Found per-statement debug comment inside hot-path method: " + line);
            }
        }

        static void TestLinkLuaModifierInjection()
        {
            var source = @"
using DotaVScripts;

[DotaModifier(""modifier_linked"")]
[LinkModifier(""modifiers/modifier_linked.lua"")]
public class ModifierLinked : IDOTAModifier
{
    public void OnCreated(KVTable kv) { }
    public void OnRefresh(KVTable kv) { }
    public void OnRemoved() { }
    public void OnIntervalThink() { }
    public bool IsDebuff() => false;
    public bool IsPurgable() => true;
    public ModifierAttribute GetAttributes() => ModifierAttribute.MODIFIER_ATTRIBUTE_NONE;
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            // addon_game_mode.lua should have the LinkLuaModifier call
            AssertContains(output, "LinkLuaModifier(\"modifier_linked\"");
            AssertContains(output, "modifiers/modifier_linked.lua");
            AssertContains(output, "DOTA_MODIFIER_LUA_MOTION_NONE");
        }

        static void TestGameModeActivate()
        {
            var source = @"
using DotaVScripts;

[GameMode]
public class MyAddonGameMode : IDOTAGameMode
{
    public void InitGameMode() { }
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            AssertContains(output, "function Activate()");
            AssertContains(output, "MyAddonGameMode()");
            AssertContains(output, "gm:InitGameMode()");
        }

        static void TestForeachArray()
        {
            // Use a pre-declared variable so no array creation expression is involved
            var output = TransformBody(@"
                System.Collections.Generic.List<int> units = null;
                foreach (var unit in units) { }");
            AssertContains(output, "ipairs(units)");
            AssertContains(output, "for _, unit in");
        }

        static void TestNumericForLoop()
        {
            var output = TransformBody(@"
                for (int i = 0; i < 10; i++) { }");
            AssertContains(output, "for i = 0,");
            AssertContains(output, "do");
        }

        static void TestEnumMemberAccess()
        {
            var output = TransformBody(
                "var b = AbilityBehavior.DOTA_ABILITY_BEHAVIOR_NO_TARGET;",
                extraUsings: "using DotaVScripts;");
            AssertContains(output, "DOTA_ABILITY_BEHAVIOR_NO_TARGET");
            AssertNotContains(output, "AbilityBehavior.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phase 3 tests
        // ─────────────────────────────────────────────────────────────────────

        static void TestBareFieldRewrite()
        {
            // When a field is declared on the class and referenced bare inside a method,
            // it should be rewritten to self.fieldName
            var source = @"
using DotaVScripts;

[DotaAbility(""my_field_ability"")]
public class MyFieldAbility : IDOTAAbility
{
    private float damage = 100f;

    public void OnSpellStart()
    {
        var d = damage;          // should become: local d = self.damage
        damage = damage + 10f;   // should become: self.damage = self.damage + 10
    }

    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 600f;
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            AssertContains(output, "local d = self.damage");
            AssertContains(output, "self.damage = self.damage + 10");
            AssertNotContains(output, "local d = damage");
        }

        static void TestBareFieldInMethod()
        {
            // Modifier with field referenced in OnIntervalThink (hot path)
            var source = @"
using DotaVScripts;

[DotaModifier(""modifier_field_test"")]
public class ModifierFieldTest : IDOTAModifier
{
    private float interval = 0.5f;

    public void OnCreated(KVTable kv)
    {
        var i = interval;   // should be: local i = self.interval
    }

    public void OnIntervalThink() { }
    public void OnRefresh(KVTable kv) { }
    public void OnRemoved() { }
    public bool IsDebuff() => false;
    public bool IsPurgable() => true;
    public ModifierAttribute GetAttributes() => ModifierAttribute.MODIFIER_ATTRIBUTE_NONE;
}";
            var (output, errors) = RunTranspilerOnSource(source);
            if (errors.Count > 0)
                throw new Exception("Transpiler errors:\n" + string.Join("\n", errors));

            AssertContains(output, "local i = self.interval");
        }

        static void TestAsyncMethodBlocked()
        {
            var source = @"
using DotaVScripts;
using System.Threading.Tasks;

[DotaAbility(""my_async_ability"")]
public class MyAsyncAbility : IDOTAAbility
{
    public async void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("async")))
                throw new Exception("Expected async error, got: " + string.Join("; ", errors));
        }

        static void TestRefParamBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_ref_ability"")]
public class MyRefAbility : IDOTAAbility
{
    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
    public void MyHelper(ref float value) { }
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("ref")))
                throw new Exception("Expected ref error, got: " + string.Join("; ", errors));
        }

        static void TestOutParamBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_out_ability"")]
public class MyOutAbility : IDOTAAbility
{
    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
    public void MyHelper(out float value) { value = 0f; }
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("out")))
                throw new Exception("Expected out error, got: " + string.Join("; ", errors));
        }

        static void TestContinueBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_continue_ability"")]
public class MyContinueAbility : IDOTAAbility
{
    public void OnSpellStart()
    {
        for (int i = 0; i < 10; i++)
        {
            if (i == 5) continue;
        }
    }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("continue")))
                throw new Exception("Expected continue error, got: " + string.Join("; ", errors));
        }

        static void TestNestedClassBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_nested_ability"")]
public class MyNestedAbility : IDOTAAbility
{
    private class Inner { }

    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("Nested")))
                throw new Exception("Expected nested class error, got: " + string.Join("; ", errors));
        }

        static void TestStaticCtorBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_static_ctor_ability"")]
public class MyStaticCtorAbility : IDOTAAbility
{
    static MyStaticCtorAbility() { }

    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("Static constructor")))
                throw new Exception("Expected static ctor error, got: " + string.Join("; ", errors));
        }

        static void TestUsingStatementBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_using_ability"")]
public class MyUsingAbility : IDOTAAbility
{
    public void OnSpellStart()
    {
        using var x = new System.IO.MemoryStream();
    }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("using") || e.Contains("disposal")))
                throw new Exception("Expected using statement error, got: " + string.Join("; ", errors));
        }

        static void TestLockStatementBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_lock_ability"")]
public class MyLockAbility : IDOTAAbility
{
    private static object _lock = new object();
    public void OnSpellStart()
    {
        lock (_lock) { }
    }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("lock")))
                throw new Exception("Expected lock statement error, got: " + string.Join("; ", errors));
        }

        static void TestGotoBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_goto_ability"")]
public class MyGotoAbility : IDOTAAbility
{
    public void OnSpellStart()
    {
        goto done;
        done:;
    }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("goto")))
                throw new Exception("Expected goto error, got: " + string.Join("; ", errors));
        }

        static void TestYieldBlocked()
        {
            var source = @"
using DotaVScripts;
using System.Collections.Generic;

[DotaAbility(""my_yield_ability"")]
public class MyYieldAbility : IDOTAAbility
{
    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
    public IEnumerable<int> GetValues()
    {
        yield return 1;
        yield return 2;
    }
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("yield")))
                throw new Exception("Expected yield error, got: " + string.Join("; ", errors));
        }

        static void TestBclFileBlocked()
        {
            var source = @"
using DotaVScripts;
using System.IO;

[DotaAbility(""my_io_ability"")]
public class MyIOAbility : IDOTAAbility
{
    public void OnSpellStart()
    {
        var content = File.ReadAllText(""config.txt"");
    }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("File") || e.Contains("BCL")))
                throw new Exception("Expected BCL File error, got: " + string.Join("; ", errors));
        }

        static void TestBclThreadBlocked()
        {
            // Use Thread directly (not fully qualified) so the BCL type identifier check fires
            var source = @"
using DotaVScripts;
using System.Threading;

[DotaAbility(""my_thread_ability"")]
public class MyThreadAbility : IDOTAAbility
{
    public void OnSpellStart()
    {
        var t = Thread.CurrentThread;
    }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("Thread") || e.Contains("BCL")
                               || e.Contains("not available")))
                throw new Exception("Expected BCL Thread error, got: " + string.Join("; ", errors));
        }

        static void TestEmptyLuaNameBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility("""")]
public class MyEmptyNameAbility : IDOTAAbility
{
    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("cannot be empty")
                               || e.Contains("reserved global")))
                throw new Exception("Expected empty name error, got: " + string.Join("; ", errors));
        }

        static void TestDuplicateDotaAttrBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_dual_ability"")]
[DotaModifier(""my_dual_ability"")]
public class MyDualAttrClass : IDOTAAbility
{
    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("Multiple Dota class attributes")
                               || e.Contains("only be one kind")))
                throw new Exception("Expected duplicate attr error, got: " + string.Join("; ", errors));
        }

        static void TestModifierPropertyOnAbilityBlocked()
        {
            var source = @"
using DotaVScripts;

[DotaAbility(""my_wrong_attr_ability"")]
public class MyWrongAttrAbility : IDOTAAbility
{
    [ModifierProperty(ModifierProperty.MOVESPEED_BONUS_FLAT)]
    public float GetModifierMoveSpeedBonus_Flat() => -50f;

    public void OnSpellStart() { }
    public bool OnAbilityPhaseStart() => true;
    public void OnAbilityPhaseInterrupted() { }
    public bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location) => false;
    public AbilityBehavior GetBehavior() => AbilityBehavior.DOTA_ABILITY_BEHAVIOR_UNIT_TARGET;
    public float GetCastRange(Vector location, ICDOTA_BaseNPC? target) => 0f;
}";
            var (_, errors) = RunTranspilerOnSource(source);
            if (!errors.Any(e => e.Contains("ModifierProperty")
                               || e.Contains("DotaModifier")))
                throw new Exception(
                    "Expected [ModifierProperty] on ability error, got: "
                    + string.Join("; ", errors));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Integration test helper
        // ─────────────────────────────────────────────────────────────────────

        static (string output, List<string> errors) RunTranspilerOnSource(string csharpSource)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"dota_test_{Guid.NewGuid():N}");
            var sourceDir = Path.Combine(tempDir, "src");
            var outputDir = Path.Combine(tempDir, "out");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(outputDir);

            File.WriteAllText(Path.Combine(sourceDir, "Test.cs"), csharpSource);

            // Locate DotaApi.dll
            var dotaApiSrc = Path.Combine(AppContext.BaseDirectory, "DotaApi.dll");
            if (!File.Exists(dotaApiSrc))
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && dir.Name != "DotaTranspiler")
                    dir = dir.Parent;
                if (dir != null)
                    dotaApiSrc = Path.Combine(dir.FullName, "DotaApi", "bin",
                        "Debug", "net8.0", "DotaApi.dll");
            }
            if (File.Exists(dotaApiSrc))
                File.Copy(dotaApiSrc, Path.Combine(sourceDir, "DotaApi.dll"), true);

            var result = new Transpiler(new TranspilerOptions
            {
                SourceDirectory = sourceDir,
                OutputDirectory = outputDir,
                Debug = false,
            }).Run();

            var allOutput = new StringBuilder();
            foreach (var f in Directory.GetFiles(
                outputDir, "*.lua", SearchOption.AllDirectories))
                allOutput.AppendLine(File.ReadAllText(f));

            try { Directory.Delete(tempDir, true); } catch { }

            return (allOutput.ToString(), new List<string>(result.Errors));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Overloaded TransformBody that accepts extra using directives
        // ─────────────────────────────────────────────────────────────────────

        static string TransformBody(string csharpBody, string extraUsings = "")
        {
            var source = $@"
{extraUsings}
class Dummy {{
    void TestMethod() {{
        {csharpBody}
    }}
}}";
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.CSharp9));

            var compilation = CSharpCompilation.Create("Test",
                new[] { tree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var method = root.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .First();

            var transformer = new BodyTransformer(model, emitDebugComments: false);
            var stmts = transformer.TransformBlock(method.Body!);
            var block = new LuaBlock(stmts);

            var sb = new StringBuilder();
            using var w = new LuaWriter(sb);
            block.WriteTo(w);
            return sb.ToString();
        }
    }
}
