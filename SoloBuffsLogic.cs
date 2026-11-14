using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace SoloBuffs
{
    internal static class SoloBuffsLogic
    {
        private static int ClanOfflineThresholdMinutes => Plugin.SoloClanOfflineThresholdMinutes!.Value;

        private static World? _serverWorld;
        private static EntityQuery _userQuery;
        private static bool _queryCreated = false;

        private static readonly HashSet<Entity> _buffedCharacters = new();
        private static readonly HashSet<Entity> _optOutUsers = new();

        // Tracks when a user disconnected
        private static readonly Dictionary<Entity, DateTime> _userDisconnectTimes = new();
        // Tracks when a user left a clan WITH online/recently online members (penalty timer)
        private static readonly Dictionary<Entity, DateTime> _userClanLeaveTimes = new();
        // Tracks last known clan per user
        private static readonly Dictionary<Entity, Entity> _userLastClan = new();
        // Tracks when a member left/disconnected from a clan (for remaining members)
        private static readonly Dictionary<Entity, DateTime> _clanMemberDepartureTimes = new();

        private static bool _pendingRefresh = false;
        public static bool GlobalEnabled = true;
        private static DateTime _nextExpiry = DateTime.MaxValue;

        public static bool HasActiveTimers => _nextExpiry != DateTime.MaxValue;

        public static bool ConsumePendingRefresh()
        {
            if (!_pendingRefresh) return false;
            _pendingRefresh = false;
            return true;
        }

        public static void RequestRefresh() => _pendingRefresh = true;

        public static float GetSecondsUntilNextExpiry()
        {
            if (_nextExpiry == DateTime.MaxValue) return float.MaxValue;
            var remaining = (_nextExpiry - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? (float)remaining : 0f;
        }

        private static void UpdateNextExpiry(DateTime candidateExpiry)
        {
            if (candidateExpiry < _nextExpiry)
                _nextExpiry = candidateExpiry;
        }

        public static World? GetServerWorld()
        {
            if (_serverWorld != null && _serverWorld.IsCreated)
                return _serverWorld;

            var worlds = World.All;
            for (int i = 0; i < worlds.Count; i++)
            {
                var w = worlds[i];
                if (w != null && w.IsCreated && w.Name == "Server")
                {
                    _serverWorld = w;
                    break;
                }
            }
            return _serverWorld;
        }

        private static EntityQuery GetOrCreateUserQuery(EntityManager em)
        {
            if (!_queryCreated || !_userQuery.HasFilter())
            {
                _userQuery = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
                _queryCreated = true;
            }
            return _userQuery;
        }

        // Returns true if a clan had online OR recently online members (excluding the given user)
        private static bool ClanHadActiveMembers(EntityManager em, Entity clanEntity, Entity excludeUser)
        {
            DateTime nowUtc = DateTime.UtcNow;
            var offlineThreshold = TimeSpan.FromMinutes(Math.Max(0, ClanOfflineThresholdMinutes));

            var query = GetOrCreateUserQuery(em);
            var users = query.ToEntityArray(Allocator.Temp);

            try
            {
                for (int i = 0; i < users.Length; i++)
                {
                    var uEntity = users[i];
                    if (uEntity == excludeUser) continue;
                    if (!em.Exists(uEntity) || !em.HasComponent<User>(uEntity)) continue;

                    var u = em.GetComponentData<User>(uEntity);
                    if (u.ClanEntity._Entity != clanEntity) continue;

                    if (u.IsConnected) return true;

                    if (_userDisconnectTimes.TryGetValue(uEntity, out DateTime disconnectTime))
                    {
                        if (nowUtc - disconnectTime < offlineThreshold) return true;
                    }
                    else
                    {
                        try
                        {
                            var lastOnline = DateTime.FromBinary(u.TimeLastConnected).ToUniversalTime();
                            if (lastOnline != DateTime.MinValue && nowUtc - lastOnline < offlineThreshold)
                                return true;
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                users.Dispose();
            }

            return false;
        }

        public static void UpdateSoloBuffs()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) return;

            var em = serverWorld.EntityManager;
            _nextExpiry = DateTime.MaxValue;

            var query = GetOrCreateUserQuery(em);
            var users = query.ToEntityArray(Allocator.Temp);

            // Pass 1: track disconnects and clan changes
            for (int i = 0; i < users.Length; i++)
            {
                var userEntity = users[i];
                if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity)) continue;

                var user = em.GetComponentData<User>(userEntity);
                var currentClan = user.ClanEntity._Entity;

                if (!user.IsConnected && !_userDisconnectTimes.ContainsKey(userEntity))
                {
                    _userDisconnectTimes[userEntity] = DateTime.UtcNow;

                    // Last known clan, a kick can clear ClanEntity on disconnect
                    Entity clanForDeparture = _userLastClan.TryGetValue(userEntity, out var lastKnownClan) && lastKnownClan != Entity.Null
                        ? lastKnownClan
                        : currentClan;

                    // Only if others are in the clan, so you don't count as your own recent clanmate
                    if (clanForDeparture != Entity.Null && em.Exists(clanForDeparture) &&
                        ClanHadActiveMembers(em, clanForDeparture, userEntity))
                    {
                        _clanMemberDepartureTimes[clanForDeparture] = DateTime.UtcNow;
                    }
                }
                else if (user.IsConnected && _userDisconnectTimes.ContainsKey(userEntity))
                {
                    if (!_userClanLeaveTimes.ContainsKey(userEntity))
                        _userDisconnectTimes.Remove(userEntity);
                }

                if (user.IsConnected)
                {
                    if (_userLastClan.TryGetValue(userEntity, out Entity lastClan))
                    {
                        bool leftClan = lastClan != Entity.Null && em.Exists(lastClan) && lastClan != currentClan;

                        if (leftClan)
                        {
                            bool hadActiveMembers = ClanHadActiveMembers(em, lastClan, userEntity);
                            if (hadActiveMembers)
                            {
                                _userClanLeaveTimes[userEntity] = DateTime.UtcNow;
                                _clanMemberDepartureTimes[lastClan] = DateTime.UtcNow;
                            }
                        }

                        bool joinedNewClan = currentClan != Entity.Null && em.Exists(currentClan) && currentClan != lastClan && !leftClan;
                        if (joinedNewClan && _userClanLeaveTimes.ContainsKey(userEntity))
                        {
                            bool newClanHasOthers = ClanHadActiveMembers(em, currentClan, userEntity);
                            if (newClanHasOthers)
                            {
                                _userClanLeaveTimes.Remove(userEntity);
                                _userDisconnectTimes.Remove(userEntity);
                            }
                        }
                    }

                    _userLastClan[userEntity] = currentClan;
                }
            }

            // Pass 2: update buffs
            for (int i = 0; i < users.Length; i++)
                UpdateBuffForUser(em, users[i]);

            users.Dispose();

            // Cleanup stale entities
            var toRemove = new List<Entity>();
            foreach (var e in _buffedCharacters)
                if (!em.Exists(e)) toRemove.Add(e);
            foreach (var e in toRemove) _buffedCharacters.Remove(e);

            toRemove.Clear();
            foreach (var e in _optOutUsers)
                if (!em.Exists(e)) toRemove.Add(e);
            foreach (var e in toRemove) _optOutUsers.Remove(e);

            toRemove.Clear();
            foreach (var kvp in _userDisconnectTimes)
                if (!em.Exists(kvp.Key)) toRemove.Add(kvp.Key);
            foreach (var key in toRemove)
            {
                _userDisconnectTimes.Remove(key);
                _userClanLeaveTimes.Remove(key);
                _userLastClan.Remove(key);
            }
        }

        public static void UpdateBuffForUser(EntityManager em, Entity userEntity)
        {
            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity)) return;

            var user = em.GetComponentData<User>(userEntity);

            if (IsUserOptedOut(em, userEntity))
            {
                var charEntity = user.LocalCharacter._Entity;
                if (charEntity != Entity.Null && em.Exists(charEntity))
                {
                    BuffService.RemoveBuff(charEntity);
                    _buffedCharacters.Remove(charEntity);
                }
                return;
            }

            if (!user.IsConnected)
            {
                var charEntity = user.LocalCharacter._Entity;
                if (charEntity != Entity.Null && em.Exists(charEntity))
                {
                    BuffService.RemoveBuff(charEntity);
                    _buffedCharacters.Remove(charEntity);
                }
                return;
            }

            if (!GlobalEnabled)
            {
                var charEntity = user.LocalCharacter._Entity;
                if (charEntity != Entity.Null && em.Exists(charEntity))
                {
                    BuffService.RemoveBuff(charEntity);
                    _buffedCharacters.Remove(charEntity);
                }
                return;
            }

            var character = user.LocalCharacter._Entity;
            if (character == Entity.Null || !em.Exists(character)) return;

            bool isSolo = IsUserSolo(em, userEntity, user, out _);

            if (isSolo)
            {
                BuffService.ApplyBuff(userEntity, character);
                _buffedCharacters.Add(character);
            }
            else if (_buffedCharacters.Contains(character))
            {
                BuffService.RemoveBuff(character);
                _buffedCharacters.Remove(character);
            }
        }

        private static bool IsUserSolo(EntityManager em, Entity userEntity, User user, out Entity clanEntity)
        {
            clanEntity = user.ClanEntity._Entity;
            DateTime nowUtc = DateTime.UtcNow;
            var offlineThreshold = TimeSpan.FromMinutes(Math.Max(0, ClanOfflineThresholdMinutes));

            if (_userClanLeaveTimes.TryGetValue(userEntity, out DateTime leaveTime))
            {
                var timeSinceLeave = nowUtc - leaveTime;
                if (timeSinceLeave < offlineThreshold)
                {
                    UpdateNextExpiry(leaveTime + offlineThreshold);
                    return false;
                }
                _userClanLeaveTimes.Remove(userEntity);
            }

            if (clanEntity == Entity.Null || !em.Exists(clanEntity))
                return true;

            if (_clanMemberDepartureTimes.TryGetValue(clanEntity, out DateTime departureTime))
            {
                var timeSinceDeparture = nowUtc - departureTime;
                if (timeSinceDeparture < offlineThreshold)
                {
                    UpdateNextExpiry(departureTime + offlineThreshold);
                    return false;
                }
                _clanMemberDepartureTimes.Remove(clanEntity);
            }

            var query = GetOrCreateUserQuery(em);
            var users = query.ToEntityArray(Allocator.Temp);
            bool otherRecentOrOnline = false;

            try
            {
                for (int i = 0; i < users.Length; i++)
                {
                    var uEntity = users[i];
                    if (!em.Exists(uEntity) || !em.HasComponent<User>(uEntity)) continue;

                    var u = em.GetComponentData<User>(uEntity);
                    if (u.ClanEntity._Entity != clanEntity) continue;

                    if (u.IsConnected)
                    {
                        if (uEntity != userEntity)
                        {
                            otherRecentOrOnline = true;
                            break;
                        }
                    }
                    else if (uEntity != userEntity)
                    {
                        if (_userDisconnectTimes.TryGetValue(uEntity, out DateTime disconnectTime))
                        {
                            if (nowUtc - disconnectTime < offlineThreshold)
                            {
                                UpdateNextExpiry(disconnectTime + offlineThreshold);
                                otherRecentOrOnline = true;
                            }
                        }
                        else
                        {
                            try
                            {
                                var lastOnline = DateTime.FromBinary(u.TimeLastConnected).ToUniversalTime();
                                if (lastOnline != DateTime.MinValue && nowUtc - lastOnline < offlineThreshold)
                                {
                                    UpdateNextExpiry(lastOnline + offlineThreshold);
                                    otherRecentOrOnline = true;
                                }
                                else if (lastOnline == DateTime.MinValue)
                                    otherRecentOrOnline = true;
                            }
                            catch { otherRecentOrOnline = true; }
                        }
                    }
                }
            }
            finally
            {
                users.Dispose();
            }

            return !otherRecentOrOnline;
        }

        public static bool TryGetStatusForUser(EntityManager em, Entity userEntity, out bool isSolo, out bool hasSoloBuff, out string reasonLine, out string timingLine)
        {
            isSolo = false;
            hasSoloBuff = false;
            reasonLine = string.Empty;
            timingLine = string.Empty;

            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
            {
                reasonLine = "User entity not found.";
                return false;
            }

            var user = em.GetComponentData<User>(userEntity);
            var charEntity = user.LocalCharacter._Entity;

            if (charEntity == Entity.Null || !em.Exists(charEntity))
            {
                reasonLine = "No character found.";
                return false;
            }

            isSolo = IsUserSolo(em, userEntity, user, out Entity clanEntity);
            bool optedOut = IsUserOptedOut(em, userEntity);
            hasSoloBuff = _buffedCharacters.Contains(charEntity) && BuffService.HasBuff(charEntity);

            if (optedOut)
            {
                reasonLine = "You have Solo Buffs disabled.";
            }
            else if (_userClanLeaveTimes.ContainsKey(userEntity))
            {
                reasonLine = "You have recently left a clan.";
            }
            else if (clanEntity == Entity.Null || !em.Exists(clanEntity))
            {
                reasonLine = "You are not in a clan.";
            }
            else
            {
                var query = GetOrCreateUserQuery(em);
                var users = query.ToEntityArray(Allocator.Temp);
                int totalMembers = 0, onlineMembers = 0;
                for (int i = 0; i < users.Length; i++)
                {
                    if (!em.Exists(users[i]) || !em.HasComponent<User>(users[i])) continue;
                    var u = em.GetComponentData<User>(users[i]);
                    if (u.ClanEntity._Entity != clanEntity) continue;
                    totalMembers++;
                    if (u.IsConnected) onlineMembers++;
                }
                users.Dispose();

                if (onlineMembers > 1)
                    reasonLine = "Clanmate is online.";
                else if (!isSolo)
                    reasonLine = "Clanmate has recently been online.";
                else
                    reasonLine = "You are solo.";
            }

            if (optedOut)
            {
                timingLine = "Type <color=#00FFBB>.solo on</color> to re-enable them.";
            }
            else if (hasSoloBuff)
            {
                timingLine = "Solo Buffs are applied.";
            }
            else if (isSolo && !GlobalEnabled)
            {
                timingLine = "Solo Buffs system is currently disabled by an admin.";
            }
            else if (!isSolo)
            {
                float seconds = GetSecondsUntilNextExpiry();
                if (seconds < float.MaxValue && seconds > 0)
                {
                    var remaining = TimeSpan.FromSeconds(seconds);
                    timingLine = $"Solo Buffs applied in <color=#00FFBB>{FormatTimeSpanShort(remaining)}</color>.";
                }
                else
                    timingLine = "Solo Buffs are not currently available.";
            }
            else
            {
                timingLine = "Solo Buffs will be applied shortly.";
            }

            return true;
        }

        private static string FormatTimeSpanShort(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            if (ts.TotalHours >= 1.0) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1.0) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }

        internal static bool IsUserOptedOut(EntityManager em, Entity userEntity) => _optOutUsers.Contains(userEntity);

        internal static void SetUserOptOut(EntityManager em, Entity userEntity)
        {
            if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity)) return;
            if (!_optOutUsers.Contains(userEntity)) _optOutUsers.Add(userEntity);
            var user = em.GetComponentData<User>(userEntity);
            var charEntity = user.LocalCharacter._Entity;
            if (charEntity != Entity.Null && em.Exists(charEntity))
            {
                BuffService.RemoveBuff(charEntity);
                _buffedCharacters.Remove(charEntity);
            }
        }

        internal static void SetUserOptIn(EntityManager em, Entity userEntity) => _optOutUsers.Remove(userEntity);

        internal static void ClearAllBuffs()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) return;
            var em = serverWorld.EntityManager;
            var chars = new List<Entity>(_buffedCharacters);
            foreach (var c in chars)
                if (em.Exists(c)) BuffService.RemoveBuff(c);
            _buffedCharacters.Clear();
        }

        internal static void RefreshAllBuffedCharacters()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) return;
            var em = serverWorld.EntityManager;
            foreach (var c in _buffedCharacters)
                if (em.Exists(c)) BuffService.RefreshBuff(c);
        }

        internal static void ResetAllTimers()
        {
            var pastThreshold = DateTime.UtcNow.AddMinutes(-(ClanOfflineThresholdMinutes + 1));
            var keys = new List<Entity>(_userDisconnectTimes.Keys);
            foreach (var key in keys)
                _userDisconnectTimes[key] = pastThreshold;

            var leaveKeys = new List<Entity>(_userClanLeaveTimes.Keys);
            foreach (var key in leaveKeys)
                _userClanLeaveTimes[key] = pastThreshold;

            var departureKeys = new List<Entity>(_clanMemberDepartureTimes.Keys);
            foreach (var key in departureKeys)
                _clanMemberDepartureTimes[key] = pastThreshold;

            _nextExpiry = DateTime.MaxValue;
        }

        internal static void RemoveAllVisualBuffs()
        {
            var serverWorld = GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) return;
            var em = serverWorld.EntityManager;
            var chars = new List<Entity>(_buffedCharacters);
            foreach (var c in chars)
                if (em.Exists(c)) BuffService.RemoveVisualBuff(c, em);
        }
    }
}