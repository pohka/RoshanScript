using System;

namespace DotaVScripts
{
    /// <summary>
    /// Dota 2 global functions. In Lua these are plain globals (no instance prefix).
    /// The transpiler detects calls to methods on this class and emits them as
    /// bare Lua function calls without any table prefix.
    /// </summary>
    public static class Dota
    {
        // Event system
        public static EventListenerID ListenToGameEvent(
            string eventName,
            Action<GameEvent> callback,
            object? context = null) => new();

        public static void StopListeningToGameEvent(EventListenerID id) { }

        // Unit creation
        public static ICDOTA_BaseNPC CreateUnitByName(
            string unitName, Vector location, bool findClearSpace,
            ICDOTA_BaseNPC_Hero? hero, ICDOTA_BaseNPC? owner, TeamNumber team) => null!;

        public static void CreateUnitByNameAsync(
            string unitName, Vector location, bool findClearSpace,
            ICDOTA_BaseNPC_Hero? hero, ICDOTA_BaseNPC? owner, TeamNumber team,
            Action<ICDOTA_BaseNPC> callback) { }

        // Unit querying
        public static ICDOTA_BaseNPC[] FindUnitsInRadius(
            TeamNumber team, Vector location, ICDOTA_BaseNPC? cacheUnit, float radius,
            int teamFilter, int typeFilter, int flagFilter, int order,
            bool canGrowCache) => System.Array.Empty<ICDOTA_BaseNPC>();

        // Modifier linking — must be called before the modifier class is used
        public static void LinkLuaModifier(
            string modifierName, string filePath, LuaModifierType motionController) { }

        // Item creation
        public static ICDOTA_BaseAbility CreateItemByName(string itemName) => null!;

        // Particles
        public static ParticleID ParticleManager_CreateParticle(
            string particleName, int attachType, ICDOTA_BaseNPC entity) => new();
        public static void ParticleManager_DestroyParticle(ParticleID particle, bool immediate) { }
        public static void ParticleManager_SetParticleControl(
            ParticleID particle, int control, Vector value) { }

        // Projectiles
        public static ProjectileID ProjectileManager_CreateTrackingProjectile(
            ICDOTA_BaseNPC target, ICDOTA_BaseAbility ability, int speed,
            string particleName, Vector spawnOrigin, bool dodgeable, bool isAttack,
            bool expireOnImpact, int extraData) => new();

        // Sound
        public static void EmitGlobalSound(string soundName) { }
        public static void EmitSoundOn(string soundName, ICDOTA_BaseNPC entity) { }
        public static void EmitSoundOnLocationWithCaster(
            Vector location, string soundName, ICDOTA_BaseNPC caster) { }

        // Math / random
        public static float RandomFloat(float min, float max) => 0f;
        public static int RandomInt(int min, int max) => 0;
        public static float Time() => 0f;
        public static float FrameTime() => 0f;
        public static Vector AddRotationToVector(Vector vec, Vector normal, float angle) => vec;
        public static Vector RotatePosition(Vector origin, Vector rotation, Vector point) => point;

        // Debug
        public static void DebugPrint(string message) { }
        public static void DebugDrawCircle(Vector center, Vector color, float alpha,
            float radius, bool ztest, float duration) { }
        public static void DebugDrawLine(Vector origin, Vector target,
            int r, int g, int b, bool ztest, float duration) { }

        // Map / world
        public static Vector GetGroundPosition(Vector origin, ICDOTA_BaseNPC? entity) => origin;
        public static bool IsLocationVisible(TeamNumber team, Vector location) => true;
        public static Vector GetTreeLocation(int treeId) => new();

        // String / localization
        public static string Localize(string token) => token;

        // Minimap
        public static void MinimapEvent(TeamNumber team, ICDOTA_BaseNPC entity,
            float x, float y, int eventType, float duration) { }
    }

    /// <summary>
    /// Global singleton accessors. In Lua these are plain globals (GameRules, Entities etc).
    /// The transpiler rewrites property access on this class to bare Lua global names.
    /// </summary>
    public static class DotaSingletons
    {
        public static ICDOTAGameRules GameRules => null!;
    }
}
