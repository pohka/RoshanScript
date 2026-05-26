namespace DotaVScripts
{
    // -------------------------------------------------------------------------
    // Primitive types used in the Dota API
    // -------------------------------------------------------------------------

    /// <summary>Dota 2 3D vector. Maps to Lua's Vector() global constructor.</summary>
    public sealed class Vector
    {
        public float x, y, z;
        public Vector(float x = 0, float y = 0, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector operator +(Vector a, Vector b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector operator -(Vector a, Vector b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector operator *(Vector a, float s) => new(a.x * s, a.y * s, a.z * s);
        public float Length() => 0f;
        public float Length2D() => 0f;
        public Vector Normalized() => this;
        public float Dot(Vector other) => 0f;
    }

    /// <summary>Arbitrary key-value table passed to modifier OnCreated/OnRefresh.</summary>
    public sealed class KVTable
    {
        public float GetFloat(string key, float defaultValue = 0f) => defaultValue;
        public int GetInt(string key, int defaultValue = 0) => defaultValue;
        public string GetString(string key, string defaultValue = "") => defaultValue;
    }

    /// <summary>Opaque game event data table from ListenToGameEvent callbacks.</summary>
    public sealed class GameEvent
    {
        public float GetFloat(string key) => 0f;
        public int GetInt(string key) => 0;
        public string GetString(string key) => "";
        public bool GetBool(string key) => false;
    }

    public sealed class EventListenerID { }
    public sealed class ProjectileID { }
    public sealed class ParticleID { }

    // -------------------------------------------------------------------------
    // Handle interfaces — implemented by engine objects, never by user code.
    // These give Roslyn type information for method calls on engine handles.
    // The transpiler emits colon-call syntax for all methods on these types.
    // -------------------------------------------------------------------------

    public interface IBaseEntity
    {
        Vector GetAbsOrigin();
        int GetEntityIndex();
        bool IsNull();
        string GetClassname();
        void SetAbsOrigin(Vector origin);
    }

    public interface ICDOTA_BaseNPC : IBaseEntity
    {
        // Health
        float GetHealth();
        float GetMaxHealth();
        void SetHealth(float health);
        void AddHealth(float amount);
        float GetHealthRegen();

        // Mana
        float GetMana();
        float GetMaxMana();
        void SetMana(float mana);
        void SpendMana(float mana, ICDOTA_BaseAbility ability);

        // Position / movement
        float GetMoveSpeedModifier(float baseMoveSpeed);
        void MoveToPosition(Vector position);
        void MoveToTargetToAttack(ICDOTA_BaseNPC target);
        void Stop();

        // Team
        int GetTeamNumber();
        bool IsSameTeam(ICDOTA_BaseNPC other);

        // Identity
        bool IsHero();
        bool IsCreep();
        bool IsBuilding();
        bool IsAncient();
        bool IsAlive();
        bool IsInvulnerable();
        bool HasScepter();
        bool HasShard();
        string GetUnitName();

        // Modifiers
        ICDOTA_Buff AddNewModifier(ICDOTA_BaseNPC caster, ICDOTA_BaseAbility? ability,
            string modifierName, KVTable? kv);
        bool HasModifier(string modifierName);
        void RemoveModifierByName(string modifierName);
        ICDOTA_Buff? FindModifierByName(string modifierName);

        // Abilities
        ICDOTA_BaseAbility? FindAbilityByName(string abilityName);
        void GiveAbility(string abilityName);
        int GetAbilityCount();

        // Combat
        void ForceKill(bool reincarnate);
        void EmitSound(string soundName);
        void StopSound(string soundName);

        // Stats
        float GetAttackDamage();
        int GetStrength();
        int GetAgility();
        int GetIntellect();
        void SetBaseStrength(int value);
        void SetBaseAgility(int value);
        void SetBaseIntellect(int value);
    }

    public interface ICDOTA_BaseNPC_Hero : ICDOTA_BaseNPC
    {
        int GetPlayerID();
        int GetLevel();
        void SetCustomDeathXP(int xp);
        void AddExperience(float xp, int reason, bool applyBotDiff);
        int GetAbilityPoints();
        void UpgradeAbility(ICDOTA_BaseAbility ability);
        bool IsTempestDouble();
        bool IsIllusion();
    }

    public interface ICDOTA_BaseAbility
    {
        // Caster/target info
        ICDOTA_BaseNPC GetCaster();
        ICDOTA_BaseNPC GetCursorTarget();
        Vector GetCursorPosition();

        // Ability stats
        int GetLevel();
        float GetCastRange(Vector location, ICDOTA_BaseNPC? target);
        float GetSpecialValueFor(string name);
        float GetCooldown(int level);
        float GetManaCost(int level);
        bool IsItem();
        bool IsActivated();
        bool IsHidden();

        // Resource consumption
        void UseResources(bool mana, bool cooldown, bool charge);
        void StartCooldown(float cooldown);
        void EndCooldown();
        float GetCooldownTimeRemaining();
    }

    public interface ICDOTA_Buff
    {
        // Owner info
        ICDOTA_BaseNPC GetParent();
        ICDOTA_BaseNPC GetCaster();
        ICDOTA_BaseAbility? GetAbility();

        // Duration
        float GetDuration();
        float GetRemainingTime();
        float GetElapsedTime();
        void SetDuration(float duration, bool informClient);

        // Lifecycle
        void Destroy();
        void ForceRefresh();
        void StartIntervalThink(float interval);

        // Stack count
        int GetStackCount();
        void SetStackCount(int count);
        void IncrementStackCount();
        void DecrementStackCount();

        // Particles
        int AddParticle(int index, bool destroyImmediately, bool statusEffect,
            int priority, bool heroEffect, bool overheadEffect);
    }



    // -------------------------------------------------------------------------
    // Game rules / mode interfaces
    // -------------------------------------------------------------------------

    public interface ICDOTAGameRules
    {
        float GetGameTime();
        float GetDOTATime(bool bIgnoreGameFlowCheck, bool bIgnorePause);
        int GetGameMode();
        bool IsGamePaused();
        void SetGamePaused(ICDOTA_BaseNPC_Hero hero, bool paused);
        void SetHeroRespawnEnabled(bool enabled);
        void SetUseUniversalShopMode(bool useUniversal);
        void SetCustomGameTeamMaxPlayers(TeamNumber team, int maxPlayers);
        void SetCustomGameSetupAutoLaunchDelay(float delay);
        void SetStrategyTime(float time);
        void SetPreGameTime(float time);
        void SetPostGameTime(float time);
        float GetTimeOfDay();
        void SetTimeOfDay(float time);
        ICDOTABaseGameMode GetGameModeEntity();
        void SetGameWinner(TeamNumber team);
    }

    public interface ICDOTABaseGameMode
    {
        void SetFogOfWarDisabled(bool disabled);
        void SetDaynightCycleDisabled(bool disabled);
        void SetKillingSpreeAnnouncerDisabled(bool disabled);
        void SetStickyItemDisabled(bool disabled);
        void SetBuybackEnabled(bool enabled);
        void SetTopBarTeamValuesOverride(bool enabled);
        void SetTopBarTeamValue(TeamNumber team, int value);
        void SetDeathOverlay(string file);
        void SetHUDVisible(int iElement, bool bVisible);
        void SetCustomAttributeDerivedStatValue(int statType, float value);
    }

    // -------------------------------------------------------------------------
    // Interfaces the user class implements for IDE support.
    // The transpiler reads these only for type resolution — never emits them.
    // -------------------------------------------------------------------------

    public interface IDOTAAbility
    {
        void OnSpellStart();
        bool OnAbilityPhaseStart();
        void OnAbilityPhaseInterrupted();
        bool OnProjectileHit(ICDOTA_BaseNPC? target, Vector location);
        AbilityBehavior GetBehavior();
        float GetCastRange(Vector location, ICDOTA_BaseNPC? target);
    }

    public interface IDOTAModifier
    {
        void OnCreated(KVTable kv);
        void OnRefresh(KVTable kv);
        void OnRemoved();
        void OnIntervalThink();
        bool IsDebuff();
        bool IsPurgable();
        ModifierAttribute GetAttributes();
    }

    public interface IDOTAGameMode
    {
        void InitGameMode();
    }
}
