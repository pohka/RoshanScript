using System;

namespace DotaVScripts
{
    /// <summary>
    /// Marks a class as a Dota 2 ability. The string argument becomes the Lua class name
    /// and must match the ability name in npc_abilities_custom.txt.
    /// Emits: AbilityName = class({}) with full ability lifecycle stubs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class DotaAbilityAttribute : Attribute
    {
        public string AbilityName { get; }
        public DotaAbilityAttribute(string abilityName) => AbilityName = abilityName;
    }

    /// <summary>
    /// Marks a class as a Dota 2 modifier (buff/debuff).
    /// Emits: ModifierName = class({}) with modifier lifecycle stubs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class DotaModifierAttribute : Attribute
    {
        public string ModifierName { get; }
        public DotaModifierAttribute(string modifierName) => ModifierName = modifierName;
    }

    /// <summary>
    /// Marks a class as the addon game mode entry point.
    /// Emits: Activate() and Precache() entry point wiring in addon_game_mode.lua.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class GameModeAttribute : Attribute { }

    /// <summary>
    /// Specifies the output Lua file path relative to game/scripts/vscripts/.
    /// If omitted the transpiler infers the path from the class name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ScriptFileAttribute : Attribute
    {
        public string Path { get; }
        public ScriptFileAttribute(string path) => Path = path;
    }

    /// <summary>
    /// On a modifier method: declares the method as a MODIFIER_PROPERTY getter.
    /// The transpiler collects all decorated methods and emits them in DeclareFunctions().
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ModifierPropertyAttribute : Attribute
    {
        public ModifierProperty Property { get; }
        public ModifierPropertyAttribute(ModifierProperty property) => Property = property;
    }

    /// <summary>
    /// On a modifier method: declares the method as contributing a MODIFIER_STATE entry.
    /// The transpiler collects all decorated methods and emits them in CheckState().
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ModifierStateAttribute : Attribute
    {
        public ModifierState State { get; }
        public ModifierStateAttribute(ModifierState state) => State = state;
    }

    /// <summary>
    /// Specifies the Lua file path for LinkLuaModifier() injection.
    /// Applied to modifier classes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class LinkModifierAttribute : Attribute
    {
        public string FilePath { get; }
        public LinkModifierAttribute(string filePath) => FilePath = filePath;
    }
}
