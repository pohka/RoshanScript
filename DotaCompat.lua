-- DotaCompat.lua
-- Runtime shim for the DotaTranspiler C# to Lua transpiler.
-- Place this file at game/scripts/vscripts/DotaCompat.lua
-- It is required once from addon_game_mode.lua and provides helpers
-- that fill gaps between C# idioms and Lua 5.1.

DotaCompat = DotaCompat or {}

-- ---------------------------------------------------------------------------
-- Handle validity
-- ---------------------------------------------------------------------------

--- Returns true if the handle is non-nil and not a "null entity" handle.
--- Use this instead of plain nil checks on engine handles.
function DotaCompat.IsValid(handle)
    if handle == nil then return false end
    if handle.IsNull and handle:IsNull() then return false end
    return true
end

-- ---------------------------------------------------------------------------
-- String helpers (maps common C# string methods)
-- ---------------------------------------------------------------------------

--- string.Contains equivalent
function DotaCompat.StringContains(s, sub)
    return s:find(sub, 1, true) ~= nil
end

--- string.StartsWith equivalent
function DotaCompat.StringStartsWith(s, prefix)
    return s:sub(1, #prefix) == prefix
end

--- string.EndsWith equivalent
function DotaCompat.StringEndsWith(s, suffix)
    return suffix == "" or s:sub(-#suffix) == suffix
end

--- string.Split equivalent — returns a table of parts
function DotaCompat.StringSplit(s, sep)
    local result = {}
    local pattern = "([^" .. sep .. "]+)"
    for part in s:gmatch(pattern) do
        table.insert(result, part)
    end
    return result
end

--- string.Trim equivalent
function DotaCompat.StringTrim(s)
    return s:match("^%s*(.-)%s*$")
end

--- string.ToUpper / ToLower
function DotaCompat.StringToUpper(s) return s:upper() end
function DotaCompat.StringToLower(s) return s:lower() end

-- ---------------------------------------------------------------------------
-- List<T> helpers (C# list operations on Lua array tables)
-- ---------------------------------------------------------------------------

--- List.Add
function DotaCompat.ListAdd(t, v)
    table.insert(t, v)
end

--- List.Remove (first occurrence)
function DotaCompat.ListRemove(t, v)
    for i, x in ipairs(t) do
        if x == v then
            table.remove(t, i)
            return true
        end
    end
    return false
end

--- List.RemoveAt
function DotaCompat.ListRemoveAt(t, index)
    table.remove(t, index + 1) -- C# 0-based → Lua 1-based
end

--- List.Count / Length
function DotaCompat.ListCount(t)
    return #t
end

--- List.Contains
function DotaCompat.ListContains(t, v)
    for _, x in ipairs(t) do
        if x == v then return true end
    end
    return false
end

--- List.Clear
function DotaCompat.ListClear(t)
    for i = #t, 1, -1 do
        t[i] = nil
    end
end

--- List.IndexOf (returns -1 if not found, matching C# convention)
function DotaCompat.ListIndexOf(t, v)
    for i, x in ipairs(t) do
        if x == v then return i - 1 end -- Lua 1-based → C# 0-based
    end
    return -1
end

-- ---------------------------------------------------------------------------
-- Dictionary<K,V> helpers (C# dictionary operations on Lua hash tables)
-- ---------------------------------------------------------------------------

--- Dictionary.ContainsKey
function DotaCompat.DictContainsKey(t, k)
    return t[k] ~= nil
end

--- Dictionary.TryGetValue — returns value or nil
function DotaCompat.DictTryGet(t, k)
    return t[k]
end

--- Dictionary.Remove
function DotaCompat.DictRemove(t, k)
    t[k] = nil
end

--- Dictionary.Count (note: O(n))
function DotaCompat.DictCount(t)
    local count = 0
    for _ in pairs(t) do count = count + 1 end
    return count
end

-- ---------------------------------------------------------------------------
-- Math helpers
-- ---------------------------------------------------------------------------

--- Integer division (C# int / int)
function DotaCompat.IntDiv(a, b)
    return math.floor(a / b)
end

--- Clamp
function DotaCompat.Clamp(value, min, max)
    if value < min then return min end
    if value > max then return max end
    return value
end

--- Lerp
function DotaCompat.Lerp(a, b, t)
    return a + (b - a) * t
end

-- ---------------------------------------------------------------------------
-- Enum flag helpers (C# [Flags] enums → bit operations)
-- ---------------------------------------------------------------------------

--- Checks if a flag is set: (flags & flag) != 0
function DotaCompat.HasFlag(flags, flag)
    return bit.band(flags, flag) ~= 0
end

--- Sets a flag: flags | flag
function DotaCompat.SetFlag(flags, flag)
    return bit.bor(flags, flag)
end

--- Clears a flag: flags & ~flag
function DotaCompat.ClearFlag(flags, flag)
    return bit.band(flags, bit.bnot(flag))
end

-- ---------------------------------------------------------------------------
-- string.Format / string interpolation
-- ---------------------------------------------------------------------------

--- Equivalent of C# string.Format("{0} dealt {1} damage", unit, dmg)
--- Uses standard Lua string.format with %s/%d/%f patterns.
--- Note: the transpiler converts $"..." interpolation to string.format() calls.
function DotaCompat.StringFormat(fmt, ...)
    -- Convert C#-style {0}, {1} to %s placeholders
    local luaFmt = fmt:gsub("{(%d+)}", "%%s")
    local argList = {...}
    local converted = {}
    for _, v in ipairs(argList) do
        table.insert(converted, tostring(v))
    end
    return string.format(luaFmt, table.unpack(converted))
end

-- ---------------------------------------------------------------------------
-- Timer helpers (wraps Dota's Timers library)
-- ---------------------------------------------------------------------------

--- Creates a one-shot timer. Callback receives no arguments.
function DotaCompat.CreateTimer(delay, callback)
    return Timers:CreateTimer(delay, callback)
end

--- Creates a repeating timer. Callback should return the next interval,
--- or nil to stop.
function DotaCompat.CreateRepeatingTimer(interval, callback)
    return Timers:CreateTimer(interval, function()
        return callback()
    end)
end

-- ---------------------------------------------------------------------------
-- Table utilities (general purpose, not List/Dict specific)
-- ---------------------------------------------------------------------------

--- Returns the keys of a hash table as an array
function DotaCompat.TableKeys(t)
    local keys = {}
    for k in pairs(t) do
        table.insert(keys, k)
    end
    return keys
end

--- Returns the values of a hash table as an array
function DotaCompat.TableValues(t)
    local values = {}
    for _, v in pairs(t) do
        table.insert(values, v)
    end
    return values
end

--- Shallow-copies a table
function DotaCompat.TableCopy(t)
    local copy = {}
    for k, v in pairs(t) do
        copy[k] = v
    end
    return copy
end
