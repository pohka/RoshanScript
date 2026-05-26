using System.Collections.Generic;

namespace DotaTranspiler
{
    /// <summary>
    /// Global names injected by Valve's Source 2 / Dota 2 Lua environment.
    /// Any user C# class or top-level variable whose transpiled name collides
    /// with one of these is a compile-time error.
    /// </summary>
    public static class ReservedGlobals
    {
        public static readonly IReadOnlySet<string> Names = new HashSet<string>
        {
            // Singleton handles
            "GameRules",
            "Entities",
            "Players",
            "ParticleManager",
            "ProjectileManager",
            "GridNav",
            "GameMode",
            "Tutorial",

            // Global functions
            "ListenToGameEvent",
            "StopListeningToGameEvent",
            "LinkLuaModifier",
            "FindUnitsInRadius",
            "CreateUnitByName",
            "CreateUnitByNameAsync",
            "CreateItemByName",
            "CreateTempUnit",
            "CreateModifierThinker",
            "EmitGlobalSound",
            "EmitSoundOn",
            "EmitSoundOnLocationWithCaster",
            "DebugPrint",
            "DebugDrawCircle",
            "DebugDrawLine",
            "DebugDrawText",
            "RandomFloat",
            "RandomInt",
            "Time",
            "FrameTime",
            "GetGroundPosition",
            "IsLocationVisible",
            "AddFOWViewer",
            "RemoveFOWViewer",
            "MinimapEvent",
            "Localize",
            "UnitFilter",
            "GetTreeLocation",
            "ApplyDamage",
            "SendOverheadEventMessage",
            "ShowGenericPopup",
            "UTIL_MessageTextAll",

            // Global constructors / types
            "Vector",
            "QAngle",
            "class",

            // Lua standard library globals (Lua 5.1)
            "print",
            "pairs",
            "ipairs",
            "next",
            "type",
            "tostring",
            "tonumber",
            "rawget",
            "rawset",
            "rawequal",
            "setmetatable",
            "getmetatable",
            "require",
            "dofile",
            "load",
            "loadstring",
            "pcall",
            "xpcall",
            "error",
            "assert",
            "select",
            "unpack",
            "math",
            "string",
            "table",
            "io",
            "os",
            "bit",
            "coroutine",
            "_G",
            "_VERSION",
        };
    }
}
