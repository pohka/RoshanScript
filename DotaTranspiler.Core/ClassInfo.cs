using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotaTranspiler
{
    public enum DotaClassKind { Ability, Modifier, GameMode }

    /// <summary>
    /// One entry in the [ModifierProperty] collection — the method name and
    /// the MODIFIER_PROPERTY_* enum string that should appear in DeclareFunctions().
    /// </summary>
    public sealed class ModifierPropertyEntry
    {
        public string MethodName { get; init; } = "";
        public string PropertyConstant { get; init; } = "";  // e.g. "MODIFIER_PROPERTY_MOVESPEED_BONUS_FLAT"
    }

    /// <summary>
    /// One entry in the [ModifierState] collection — the method name and
    /// the MODIFIER_STATE_* enum string that should appear in CheckState().
    /// </summary>
    public sealed class ModifierStateEntry
    {
        public string MethodName { get; init; } = "";
        public string StateConstant { get; init; } = "";     // e.g. "MODIFIER_STATE_ROOTED"
    }

    /// <summary>
    /// Collects everything the emitter needs about one user-defined class,
    /// discovered during the attribute-scanning pass.
    /// </summary>
    public sealed class ClassInfo
    {
        public DotaClassKind Kind { get; init; }

        /// <summary>The Lua class name (from [DotaAbility("name")] etc.).</summary>
        public string LuaName { get; init; } = "";

        /// <summary>Output path relative to game/scripts/vscripts/ (from [ScriptFile]).</summary>
        public string ScriptFilePath { get; init; } = "";

        /// <summary>Path for LinkLuaModifier() call — modifiers only.</summary>
        public string? LinkModifierPath { get; init; }

        /// <summary>The original class declaration node.</summary>
        public ClassDeclarationSyntax Syntax { get; init; } = null!;

        /// <summary>C# source file this class came from.</summary>
        public string SourceFile { get; init; } = "";

        /// <summary>
        /// Methods decorated with [ModifierProperty]. The emitter generates
        /// DeclareFunctions() from these. Empty for non-modifier classes.
        /// </summary>
        public IReadOnlyList<ModifierPropertyEntry> PropertyMethods { get; init; }
            = new List<ModifierPropertyEntry>();

        /// <summary>
        /// Methods decorated with [ModifierState]. The emitter generates
        /// CheckState() from these. Empty for non-modifier classes.
        /// </summary>
        public IReadOnlyList<ModifierStateEntry> StateMethods { get; init; }
            = new List<ModifierStateEntry>();

        /// <summary>
        /// Names of methods that are on the hot path (OnIntervalThink + all
        /// property getters). The emitter skips DotaCompat wrappers for these.
        /// </summary>
        public IReadOnlySet<string> HotPathMethods { get; init; }
            = new HashSet<string>();
    }
}
