using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace AionMeter.Core.Combat;

/// <summary>
/// Turns the decoded event stream into fight segments. All mutation happens under one lock: the capture thread
/// calls <see cref="Process"/>, the UI thread reads immutable snapshots.
/// </summary>
public sealed class CombatTracker
{
    private readonly object _gate = new();
    private readonly GameData _data;
    private readonly EntityRegistry _entities;
    private readonly List<Encounter> _finished = new(); // newest first
    private readonly List<Encounter> _justFinished = new();
    private Encounter? _current;
    private bool _viewCleared;
    // 1 HP: the party roster's names (Options.PartyOnly); empty = on your own.
    private readonly HashSet<string> _party = new(StringComparer.OrdinalIgnoreCase);
    // 1 HP: the slot each name has on the game's party panel (0 …): the /state party list keeps its order.
    private readonly Dictionary<string, int> _partySlots = new(StringComparer.OrdinalIgnoreCase);
    private int _partyVersion;
    // 1 HP: combat power by character name, from the party rosters.
    private readonly Dictionary<string, long> _powers = new(StringComparer.OrdinalIgnoreCase);
    // 1 HP: gear score of players you inspected (name → score)
    private readonly Dictionary<string, int> _gear = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>1 HP: the gear score the game showed when you inspected this player; 0 when unknown.</summary>
    public int GearOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        lock (_gate) return _gear.GetValueOrDefault(name.Trim());
    }

    public CombatTracker(GameData data, MeterOptions options)
    {
        _data = data;
        Options = options;
        _entities = new EntityRegistry(data);
    }

    public MeterOptions Options { get; }

    /// <summary>
    /// 1 HP: your character's name from earlier runs. The game names you only on a loading screen: a meter started
    /// mid-session (an update, a restart) knows you as soon as your name shows up in any record (your kills, your
    /// party), and until then the party filter still leaves out everyone but your party and this name.
    /// </summary>
    public string? KnownSelfName { get; set; }
    public GameData Data => _data;

    /// <summary>Raised on the processing thread after a segment closes (kill, wipe, idle, zone change, manual).</summary>
    public event Action<FightRecord>? EncounterFinished;

    /// <summary>1 HP: you died; raised outside the lock, again when the kill notice names the killer.</summary>
    public event Action<DeathRecap>? DeathRecapped;

    private readonly DeathRecaps _recaps = new();
    private readonly List<DeathRecap> _pendingRecaps = new();

    /// <summary>1 HP: a party member died (their name) — raised outside the lock.</summary>
    public event Action<string>? PartyMemberDied;
    private readonly List<string> _pendingPartyDeaths = new();

    /// <summary>1 HP: someone asks to join your party — raised outside the lock.</summary>
    public event Action<PartyRequestEvent>? PartyRequested;
    private readonly List<PartyRequestEvent> _pendingRequests = new();
    private readonly Dictionary<string, long> _partyDeathAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>1 HP: the newest death recap of this run, or null.</summary>
    public DeathRecap? LastDeath { get; private set; }

    /// <summary>Raised when the local character is identified.</summary>
    public event Action<string>? SelfIdentified;

    public string? SelfName
    {
        get { lock (_gate) return _entities.Self?.Name; }
    }

    public string? ZoneName
    {
        get { lock (_gate) return _entities.ZoneName; }
    }

    /// <summary>The local character's server (0 until known; restored with the name cache).</summary>
    /// <summary>1 HP: your character's actor id, once known.</summary>
    public uint? SelfActorId { get { lock (_gate) return _entities.SelfId; } }

    public int SelfServerId
    {
        get { lock (_gate) return _entities.Self?.ServerId ?? 0; }
    }

    public long LastEventMs { get; private set; }

    // ---------------------------------------------------------------- cache (survives a meter restart)

    /// <summary>Changes whenever something in <see cref="ExportCache"/> does.</summary>
    public int CacheVersion
    {
        get { lock (_gate) return _entities.CacheVersion + _partyVersion; }
    }

    /// <summary>1 HP: the names in the last party roster (empty when on your own).</summary>
    public IReadOnlyList<string> PartyNames
    {
        get { lock (_gate) return _party.ToList(); }
    }

    /// <summary>1 HP: a character's combat power as the last party roster with them stated it; 0 when unknown.</summary>
    public long PowerOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        lock (_gate) return _powers.GetValueOrDefault(name.Trim());
    }

    /// <summary>Players' names and the bosses around: what a meter restarted in this zone could not learn again.</summary>
    public SessionState ExportCache()
    {
        lock (_gate) return _entities.Export() with { Party = _party.Concat(_members.Keys).Concat(_manualIn).Where(n => !_manualOut.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), Powers = new Dictionary<string, long>(_powers), Gear = new Dictionary<string, int>(_gear) };
    }

    public void ImportCache(SessionState state)
    {
        lock (_gate)
        {
            _entities.Import(state);
            if (state.Party is { } party)
                foreach (var name in party) _party.Add(name);
            if (state.Powers is { } powers)
                foreach (var (name, power) in powers) _powers[name] = power;
            if (state.Gear is { } gear)
                foreach (var (name, score) in gear) _gear[name] = score;
        }
    }

    /// <summary>1 HP: sees every event too (soul monsters in view).</summary>
    public OneHp.PetRadar? Pets { get; set; }
    /// <summary>1 HP: sees every event too (NPC casts for the boss mechanics).</summary>
    public OneHp.BossCasts? Casts { get; set; }
    /// <summary>1 HP: sees every event too (hidden cubes near).</summary>
    public OneHp.CubeRadar? Cubes { get; set; }

    public void Process(GameEvent e)
    {
        Pets?.Observe(e);
        Casts?.Observe(e);
        Cubes?.Observe(e);
        string? selfName = null;
        lock (_gate)
        {
            if (e.TimeMs > LastEventMs) LastEventMs = e.TimeMs;
            CheckIdle(e.TimeMs);

            switch (e)
            {
                case DamageEvent d:
                    OnDamage(d);
                    break;
                case HealEvent h:
                    OnHeal(h);
                    break;
                case SelfIdentifiedEvent s:
                    _entities.SetSelf(s.ActorId, s.Name, s.ServerId, s.Class);
                    LinkSummonsCastBy(s.ActorId, s.Name);
                    selfName = s.Name;
                    break;
                case PlayerSeenEvent p:
                    if (_entities.SelfId is null && KnownSelfName is { Length: > 0 } known &&
                        string.Equals(p.Name.Trim(), known.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        _entities.SetSelf(p.ActorId, p.Name, p.ServerId, p.Class); // it is you
                        selfName = p.Name;
                    }
                    else _entities.UpsertPlayer(p.ActorId, p.Name, p.ServerId, p.Class);
                    LinkSummonsCastBy(p.ActorId, p.Name);
                    break;
                case NpcSeenEvent n:
                    if (_entities.IsKnownPlayer(n.ActorId)) break;
                    // The server's own announcement replaces whatever the cache of a previous run said about this id.
                    if (_entities.TryGetNpc(n.ActorId, out var cached) && cached.FromCache) _entities.ForgetNpc(n.ActorId);
                    var npc = _entities.UpsertNpc(n.ActorId, n.NpcCode);
                    if (n.Name is not null) npc.Name = n.Name;
                    _entities.SetMaxHp(npc, n.MaxHp, known: true);
                    if (_current is { IsActive: true } running && running.BossId == n.ActorId) RefreshBoss(running, npc);
                    if (n.X != 0 || n.Y != 0) Remember(n.ActorId, n.X, n.Y, n.Z, n.TimeMs);
                    NoticeBoss(npc, BossNoticeKind.Alive, n.TimeMs, n.X, n.Y, n.Z);
                    break;
                case SummonSeenEvent s:
                    var owner = s.OwnerId != 0 ? s.OwnerId : s.OwnerName is { } on ? _entities.FindPlayerByName(on) ?? 0 : 0;
                    if (owner != 0) LinkSummon(s.ActorId, owner);
                    else _entities.MarkUnowned(s.ActorId, s.TimeMs, s.NpcCode, s.OwnerName);
                    break;
                case CastEvent c:
                    if (GameData.IsClassSkill(c.SkillCode)) _lastCastBy[c.ActorId] = (c.SkillCode, c.TimeMs);
                    OnCast(c);
                    break;
                case FieldBossListEvent list:
                    _pendingLists.Add(list with { ServerId = _entities.Self?.ServerId ?? 0 });
                    break;
                case NpcHpEvent hp:
                    OnNpcHp(hp);
                    break;
                case PlayerMpEvent mp:
                    if (_entities.IsPlayerLike(mp.ActorId) && mp.Mp is >= 0 and <= 100_000_000)
                    {
                        var max = _playerMp.TryGetValue(mp.ActorId, out var mana) ? mana.MpMax : 0;
                        _playerMp[mp.ActorId] = (mp.Mp, max, mp.TimeMs);
                    }
                    break;
                case PlayerVitalsEvent vitals:
                    // 1 HP: the character record is the only packet with the maxima; its current values are as of
                    // entering the game, so a later 00 8D update keeps its HP and MP, and only the maxima are added.
                    if (_entities.IsPlayerLike(vitals.ActorId))
                    {
                        var (hp, at) = _playerHp.TryGetValue(vitals.ActorId, out var hit) && hit.AtMs > vitals.TimeMs
                            ? (hit.Hp, hit.AtMs)
                            : (vitals.Hp, vitals.TimeMs);
                        _playerHp[vitals.ActorId] = (hp, vitals.HpMax, at);
                        var (mp, mpAt) = _playerMp.TryGetValue(vitals.ActorId, out var mana) && mana.AtMs > vitals.TimeMs
                            ? (mana.Mp, mana.AtMs)
                            : (vitals.Mp, vitals.TimeMs);
                        _playerMp[vitals.ActorId] = (mp, vitals.MpMax, mpAt);
                    }
                    break;
                case DeathEvent death:
                    if (_playerHp.TryGetValue(death.ActorId, out var lastHp)) _playerHp[death.ActorId] = (0, lastHp.Max, death.TimeMs);
                    if (!death.AlreadyDead && _current is { IsActive: true } fight && fight.Combatants.TryGetValue(death.ActorId, out var died)) died.Deaths++;
                    RunDeath(death);
                    OnDeath(death);
                    if (_entities.SelfId == death.ActorId && !death.AlreadyDead) Recap(death.TimeMs, null);
                    else if (!death.AlreadyDead && HasParty && _entities.TryGetPlayer(death.ActorId, out var fallen) &&
                             !string.IsNullOrEmpty(fallen.Name) && InParty(fallen.Name) &&
                             death.TimeMs - _partyDeathAt.GetValueOrDefault(fallen.Name.Trim(), long.MinValue / 2) > 5_000)
                    {
                        _partyDeathAt[fallen.Name.Trim()] = death.TimeMs;
                        _pendingPartyDeaths.Add(fallen.Name.Trim());
                    }
                    break;
                case PlayerGearEvent g:
                    if (g.GearScore > 0 && _gear.GetValueOrDefault(g.Name.Trim()) != g.GearScore)
                    {
                        _gear[g.Name.Trim()] = g.GearScore;
                        _partyVersion++; // saved with the name cache
                    }
                    break;
                case PlayerKilledEvent kill:
                    if (_entities.SelfId == kill.VictimId) Recap(kill.TimeMs, (kill.KillerId, kill.KillerName, kill.KillerServer));
                    if (_entities.SelfId is { } killer && killer != kill.VictimId)
                    {
                        if (_entities.ResolveOwner(kill.KillerId) == killer && _pvp.Tracks(kill.VictimId)) _pvp.YouKilled(kill.TimeMs, kill.VictimId);
                    }
                    if (_entities.SelfId == kill.VictimId && kill.KillerId != kill.VictimId && IsPvpOpponent(kill.KillerId))
                        _pvp.KilledYou(kill.TimeMs, kill.KillerId);
                    break;
                case BattleStateEvent b:
                    OnBattleState(b);
                    break;
                case PartyRosterEvent roster:
                    OnPartyRoster(roster);
                    break;
                case PartyMemberEvent member:
                    OnPartyMember(member);
                    break;
                case SelfPowerEvent sp:
                    SelfPower = sp.Power;
                    if (_entities.Self?.Name is { Length: > 0 } myName) _powers[myName.Trim()] = sp.Power;
                    break;
                case PlayerPowerEvent pp:
                    if (_powers.GetValueOrDefault(pp.Name) != pp.Power)
                    {
                        _powers[pp.Name] = pp.Power;
                        _partyVersion++; // saved with the name cache
                    }
                    break;
                case ResourceEvent res:
                    _resources[res.Id] = (res.Count, res.Bonus);
                    break;
                case PartyRequestEvent request:
                    if (request.Power > 0) _powers[request.Name] = request.Power;
                    _pendingRequests.Add(request);
                    break;
                case SkillCooldownEvent cd:
                    _cooldowns[(_entities.ResolveOwner(cd.ActorId), cd.SkillCode / 10_000)] =
                        (cd.SkillCode, cd.TimeMs, cd.TimeMs + cd.CooldownMs, cd.Charges);
                    break;
                case BuffEvent buff:
                    _buffBook.Apply(buff, _data.BuffName(buff.Code), _data.Buffs.TryGetValue(buff.Code, out var info) && info.Debuff);
                    LearnBuffSkill(buff);
                    // A buff that lasts until removed is a toggle (mantra, stance): the skill can report on/off later.
                    if (!buff.Removing && buff.DurationMs <= 0 && _buffSkills.TryGetValue(buff.Code, out var source) && source > 0)
                        _toggleSkills.Add(source / 10_000);
                    break;
                case MoveEvent move:
                    // 1 HP: the server's corrections carry everyone's position now and then (you: mostly on skills/dashes)
                    Remember(move.ActorId, move.X, move.Y, move.Z, move.TimeMs);
                    break;
                case PartyListEvent list:
                    OnPartyList(list);
                    break;
                case PartyJoinEvent join:
                    OnPartyJoin(join);
                    break;
                case PartyLeftEvent:
                    ClearParty();
                    _partyFromGame = true;
                    break;
                case ZoneChangedEvent z:
                    if (z.IsTeleport) break;
                    _manualIds.Clear(); // ids are handed out again in the new zone
                    _buffBook.Clear();
                    _playerHp.Clear();
                    _playerMp.Clear();
                    _positions.Clear();
                    _partySlots.Clear();
                    if (_current is { IsActive: true }) Finish(EncounterEndReason.ZoneChange, _current.LastDamageMs);
                    // 1 HP: dungeon runs — leaving the instance ends the run, entering one starts it
                    if (_run is { } run && run.MapId != z.MapId)
                    {
                        LastRun = run.Build(z.TimeMs, ended: true);
                        _pendingRuns.Add(LastRun);
                        _run = null;
                    }
                    if (_run is null && (z.IsDungeon || GameData.IsDungeonMap(z.MapId))) _run = new DungeonRunBuilder(z.MapId, z.ZoneName, z.TimeMs);
                    _entities.OnZoneChanged(z.MapId, z.ZoneName);
                    break;
            }
        }
        FlushFinished();
        if (selfName is not null) SelfIdentified?.Invoke(selfName);
    }

    /// <summary>Call periodically with the wall clock so idle segments close even when no packets arrive.</summary>
    public void Tick(long nowMs)
    {
        lock (_gate)
        {
            if (_current is { IsActive: true } enc) AttributeOrphans(enc, nowMs);
            CheckIdle(nowMs);
        }
        FlushFinished();
    }

    /// <summary>Closes the running segment (it is still saved if long enough) and blanks the live view.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (_current is { IsActive: true }) Finish(EncounterEndReason.Manual, _current.LastDamageMs);
            _viewCleared = true;
        }
        FlushFinished();
    }

    public void ClearHistory()
    {
        lock (_gate)
        {
            _finished.Clear();
            _viewCleared = true;
        }
    }

    // ---------------------------------------------------------------- events

    private void OnDamage(DamageEvent d)
    {
        if (d.Damage <= 0 || d.Damage > Options.MaxSingleHit * Math.Max(1, d.HitCount)) return;

        if (_data.IsNonDamageSkill(d.SkillCode)) return; // heals and spirit-link records ride in damage packets too
        // 1 HP: every hit you take, for the death recap (players included: PvP)
        if (_entities.SelfId is { } me && d.TargetId == me && d.SourceId != me)
            _recaps.OnHit(d.TimeMs, d.SourceId, d.SkillCode, d.Damage, d.Flags, d.PowerScalar);

        var source = _entities.ResolveOwner(d.SourceId);
        var target = d.TargetId;
        // A player's own pet is never an enemy (nor is anyone's spirit / skill effect whose owner is still unknown).
        if (source == target || _entities.IsSummon(target) || _entities.IsUnowned(target)) return;
        if (source != d.SourceId || _entities.IsUnowned(source)) _entities.NoteEntitySkill(d.SkillCode);
        TrackPvp(d, source, target);

        // Class skills mark a player; 7-digit NPC skills mark a mob unless the actor already fought like a player
        // (players fire 7-digit item / godstone procs too).
        var skillClass = GameData.ClassFromSkill(d.SkillCode);
        if (skillClass != GameClass.Unknown)
        {
            _entities.MarkPlayerLike(source);
            if (source == d.SourceId) _entities.ForgetCachedIfClassDiffers(source, skillClass);
        }
        else if (GameData.IsNpcSkill(d.SkillCode) && !_entities.IsPlayerLike(source) && !_entities.IsSummon(d.SourceId))
            _entities.UpsertNpc(source);

        var targetIsPlayer = IsLikelyPlayer(target);
        var sourceIsPlayer = IsLikelyPlayer(source);

        if (targetIsPlayer)
        {
            // Mob → player: incoming damage for the defence view. Player → player (PvP, duels) is ignored for now.
            if (!sourceIsPlayer && _current is { IsActive: true } && _current.Combatants.TryGetValue(target, out var victim))
            {
                victim.DamageTaken += d.Damage;
                victim.HitsTaken++;
                var from = _entities.ResolveOwner(source);
                victim.TakenFrom[from] = Combatant.Add(victim.TakenFrom.GetValueOrDefault(from), d.Damage);
            }
            return;
        }
        if (!sourceIsPlayer && IsLikelyNpc(source)) return; // NPC hitting NPC

        var npc = _entities.UpsertNpc(target);
        if (npc.Hp == 0) return; // trailing DoT ticks on a corpse must not open a new segment after the kill

        if (WorldScope())
        {
            // 1 HP: in the open world only your own fights count. A player outside the party as known now (with the
            // party filter on) does not open a fight; in your fight their hits are kept all the same — the party may
            // still be unknown (a meter started mid-session, a roster that comes later), and the filter only decides
            // who is shown, so a party member never loses damage.
            if (Options.PartyOnly && IsStranger(source) && (_current is not { IsActive: true } running || !IsOurs(running))) return;
            if (_current is { IsActive: true } nearby && !IsOurs(nearby) && IsOursActor(source)) _current = null;
        }

        if (Options.TargetMode == TargetMode.BossOnly)
        {
            if (_current is { IsActive: true, BossId: not null } && !npc.IsBoss) return; // adds during a boss fight
            if (_current is { IsActive: true, BossId: null } && npc.IsBoss && _current.TotalDamage > 0)
                Finish(EncounterEndReason.Idle, _current.LastDamageMs); // trash segment ends when the boss is pulled
        }

        if (_current is not { IsActive: true }) StartEncounter(d.TimeMs);
        var enc = _current!;

        if (npc.IsBoss && (enc.BossId is null || !IsAlive(enc.BossId.Value))) SetBoss(enc, npc);

        var c = GetCombatant(enc, source);
        if (c.Class == GameClass.Unknown && (d.Flags & HitFlags.Dot) == 0)
            c.Class = GameData.ClassFromSkill(d.SkillCode);

        var offset = (int)Math.Max(0, d.TimeMs - enc.StartMs);
        var flags = d.HitCount > 1 ? d.Flags | HitFlags.Multi : d.Flags;
        c.AddDamage(offset, d.SkillCode, d.Damage, flags);
        if (d.PowerScalar is >= 1_000 and <= 200_000 && d.SourceId == source) c.Scalars.Add(d.PowerScalar);
        if (c.FirstHitMs == 0) c.FirstHitMs = d.TimeMs;
        c.LastHitMs = d.TimeMs;

        enc.TotalDamage += d.Damage;
        enc.DamageByTarget[target] = enc.DamageByTarget.GetValueOrDefault(target) + d.Damage;
        if (enc.BossId == target) enc.AddBossDamage(d.TimeMs, d.Damage);
        enc.LastHitByTarget[target] = d.TimeMs;
        if (d.TimeMs > enc.LastDamageMs) enc.LastDamageMs = d.TimeMs;
    }

    /// <summary>1 HP: time to the kill at the current pace, and the enrage: see <see cref="BossForecast"/>.</summary>
    private BossForecast? Forecast(Encounter enc, long hp, long nowMs)
    {
        if (hp <= 0 || enc.BossPullMs == 0) return null;
        // the game's clock: the last hit, so a replay (or a lull) does not drift the forecast
        var now = Math.Min(nowMs, Math.Max(enc.LastDamageMs, enc.BossPullMs));
        var pace = enc.BossPaceDps(now);
        double? kill = pace > 0 ? hp / pace : null;
        double? enrageLeft = null, required = null;
        if (_data.Enrage.TryGetValue(enc.BossCode, out var enrage))
        {
            enrageLeft = Math.Max(0, enrage - (now - enc.BossPullMs) / 1000.0);
            required = enrageLeft > 0 ? hp / enrageLeft : null;
        }
        return new BossForecast(kill, enrageLeft, required, pace);
    }

    /// <summary>
    /// 1 HP: a fight with no boss ends the moment every target hit in the last few seconds is dead, so the next mob
    /// starts a fight of its own (its name on the card) instead of the card holding a dead target until the idle timeout.
    /// </summary>
    private void EndTrashIfAllDead(uint deadId, long timeMs)
    {
        if (_current is not { IsActive: true, BossId: null } enc || !enc.DamageByTarget.ContainsKey(deadId)) return;
        foreach (var (target, last) in enc.LastHitByTarget)
            if (target != deadId && timeMs - last < 4_000 && IsAlive(target)) return; // still fighting something else
        Finish(EncounterEndReason.Kill, Math.Max(timeMs, enc.LastDamageMs));
    }

    // 1 HP: players you heal while the party roster is unknown — your party, as far as the meter can tell.
    private readonly HashSet<string> _healed = new(StringComparer.OrdinalIgnoreCase);

    private void OnHeal(HealEvent h)
    {
        if (!_partyFromGame && _members.Count == 0 && _entities.SelfId is { } me && _entities.ResolveOwner(h.SourceId) == me && h.TargetId != me &&
            _entities.TryGetPlayer(h.TargetId, out var healed) && !string.IsNullOrEmpty(healed.Name) && _healed.Add(healed.Name.Trim()))
            _partyVersion++;
        if (_current is not { IsActive: true } enc) return;
        var source = _entities.ResolveOwner(h.SourceId);
        if (enc.Combatants.TryGetValue(source, out var c)) c.Healing += h.Amount;
        if (enc.Combatants.TryGetValue(h.TargetId, out var receiver))
            receiver.HealedBy[source] = Combatant.Add(receiver.HealedBy.GetValueOrDefault(source), h.Amount);
    }

    private void OnNpcHp(NpcHpEvent e)
    {
        if (_pvp.Tracks(e.ActorId)) _pvp.Hp(e.ActorId, e.CurrentHp); // 1 HP: an opponent's HP (PvP)
        if (_entities.IsPlayerLike(e.ActorId))
        {
            // 1 HP: a player's HP for the party window and /state. The update (00 8D) carries no maximum, so the one
            // from the character record stays; 0 means unknown and is served as null, never as a made-up maximum.
            if (e.CurrentHp >= 0)
            {
                var knownMax = _playerHp.TryGetValue(e.ActorId, out var previous) ? previous.Max : 0;
                _playerHp[e.ActorId] = (e.CurrentHp, Math.Max(knownMax, e.MaxHp), e.TimeMs);
            }
            return;
        }
        // HP/MP updates are sent for players too: only trust them for entities already known to be NPCs.
        if (!e.IsNpc && !_entities.IsKnownNpc(e.ActorId)) return;
        var npc = _entities.UpsertNpc(e.ActorId);
        // A cached boss is only an assumption: an HP pool that does not fit it means the id now belongs to another NPC.
        if (npc.FromCache && npc.MaxHp > 0 && (e.CurrentHp > npc.MaxHp || (e.MaxHp > 0 && e.MaxHp != npc.MaxHp)))
            npc = _entities.ForgetNpc(e.ActorId);
        var died = e.CurrentHp <= 0 && npc.Hp > 0;
        if (died)
        {
            NoticeBoss(npc, BossNoticeKind.Killed, e.TimeMs);
            _entities.NoteDeath(npc);
        }
        npc.Hp = e.CurrentHp;
        var max = Math.Max(e.MaxHp, Math.Max(npc.MaxHp, e.CurrentHp));
        _entities.SetMaxHp(npc, max, known: npc.MaxHpKnown || e.MaxHp >= max);

        if (_current is not { IsActive: true } enc) return;
        if (died && enc.BossId is null)
        {
            npc.Hp = 0;
            EndTrashIfAllDead(e.ActorId, e.TimeMs);
            if (!enc.IsActive) return;
        }

        if (enc.BossId is null && npc.IsBoss && enc.DamageByTarget.ContainsKey(e.ActorId)) SetBoss(enc, npc);
        if (enc.BossId != e.ActorId) return;

        RefreshBoss(enc, npc);
        enc.BossHp = e.CurrentHp;
        if (e.CurrentHp < enc.BossLowestHp) enc.BossLowestHp = e.CurrentHp;

        if (e.CurrentHp <= 0)
        {
            Finish(EncounterEndReason.Kill, Math.Max(e.TimeMs, enc.LastDamageMs));
        }
        else if (enc.BossMaxHp > 0 && enc.BossLowestHp < enc.BossMaxHp * 0.9 && e.CurrentHp >= enc.BossMaxHp * 0.995)
        {
            Finish(EncounterEndReason.Wipe, enc.LastDamageMs); // boss healed back to full: the group wiped or reset
        }
    }

    private void OnDeath(DeathEvent e)
    {
        if (!_entities.TryGetNpc(e.ActorId, out var npc)) return;
        if (npc.Hp != 0)
        {
            NoticeBoss(npc, e.AlreadyDead ? BossNoticeKind.SeenDead : BossNoticeKind.Killed, e.TimeMs);
            _entities.NoteDeath(npc);
        }
        npc.Hp = 0;
        if (_current is { IsActive: true } enc && enc.BossId == e.ActorId)
        {
            enc.BossHp = 0;
            Finish(EncounterEndReason.Kill, Math.Max(e.TimeMs, enc.LastDamageMs));
        }
        else EndTrashIfAllDead(e.ActorId, e.TimeMs);
    }

    private void OnBattleState(BattleStateEvent e)
    {
        if (_entities.IsPlayerLike(e.ActorId)) return;
        var npc = _entities.UpsertNpc(e.ActorId);
        npc.InCombat = e.InCombat;
        if (e.InCombat || _current is not { IsActive: true } enc || enc.BossId != e.ActorId) return;

        var reason = npc.Hp switch
        {
            0 => EncounterEndReason.Kill,
            > 0 => EncounterEndReason.Wipe,
            _ => EncounterEndReason.Idle,
        };
        Finish(reason, reason == EncounterEndReason.Kill ? Math.Max(e.TimeMs, enc.LastDamageMs) : enc.LastDamageMs);
    }

    // ---------------------------------------------------------------- 1 HP: death recap

    private void Recap(long timeMs, (uint Id, string Name, int Server)? killer)
    {
        var recap = _recaps.OnDeath(timeMs, _entities.Self?.Name ?? "", killer, _entities.ResolveOwner, Who, _data.SkillName);
        if (recap is null) return;
        LastDeath = recap;
        _pendingRecaps.Add(recap);
    }

    // ---------------------------------------------------------------- 1 HP: dungeon runs

    private DungeonRunBuilder? _run;
    private readonly List<DungeonRun> _pendingRuns = new();
    private readonly Dictionary<uint, long> _runDeaths = new();

    /// <summary>Raised outside the lock when you leave a dungeon: the whole run.</summary>
    public event Action<DungeonRun>? DungeonRunEnded;

    /// <summary>The last dungeon run of this run of the meter that ended (you left the instance), or null.</summary>
    public DungeonRun? LastRun { get; private set; }

    /// <summary>The dungeon run in progress, or null outside instances.</summary>
    public DungeonRun? CurrentRun(long nowMs)
    {
        lock (_gate) return _run?.Build(nowMs, ended: false);
    }

    private void RunDeath(DeathEvent e)
    {
        if (_run is not { } run || e.AlreadyDead) return;
        if (!_entities.TryGetPlayer(e.ActorId, out var p) || string.IsNullOrEmpty(p.Name)) return;
        // The death record and the kill notice report the same death.
        if (_runDeaths.TryGetValue(e.ActorId, out var last) && Math.Abs(e.TimeMs - last) < 5_000) return;
        _runDeaths[e.ActorId] = e.TimeMs;
        run.AddDeath(p.Name);
    }

    // ---------------------------------------------------------------- 1 HP: PvP

    private readonly PvpTracker _pvp = new();
    private readonly List<PvpSession> _pendingPvp = new();
    private readonly HashSet<long> _scalars = new(); // power scalars seen: records carrying one in place of damage are not hits

    /// <summary>Raised outside the lock when a PvP session ends (a minute without PvP hits).</summary>
    public event Action<PvpSession>? PvpSessionEnded;

    /// <summary>The PvP session running, or the last one until a minute after it (opponents, kills, deaths).</summary>
    public PvpSession? Pvp(long nowMs)
    {
        lock (_gate) return _pvp.Snapshot(nowMs, WhoPvp);
    }

    private (string Name, GameClass Class, int Server) WhoPvp(uint id)
    {
        var (name, cls, server, _) = Who(id);
        return (name, cls, server);
    }

    /// <summary>Another player — named or fighting like one — who is not you or your party.</summary>
    private bool IsPvpOpponent(uint id) =>
        _entities.SelfId != id && (_entities.TryGetPlayer(id, out _) || _entities.IsPlayerLike(id)) && !IsOursActor(id);

    private void TrackPvp(DamageEvent d, uint source, uint target)
    {
        if (d.PowerScalar != 0)
        {
            if (_scalars.Count > 4_096) _scalars.Clear();
            _scalars.Add(d.PowerScalar);
        }
        else if ((d.Flags & HitFlags.Dot) == 0 && _scalars.Contains(d.Damage)) return; // the scalar where damage goes
        if (_entities.SelfId is not { } me) return;
        if (source == me && target != me && IsPvpOpponent(target))
            _pvp.Dealt(d.TimeMs, target, d.Damage, GameData.ClassFromSkill(d.SkillCode));
        else if (target == me && source != me && IsPvpOpponent(source))
            _pvp.Taken(d.TimeMs, source, d.Damage, GameData.ClassFromSkill(d.SkillCode));
    }

    private (string Name, GameClass Class, int Server, bool IsPlayer) Who(uint id)
    {
        if (_entities.TryGetPlayer(id, out var p) && !string.IsNullOrEmpty(p.Name))
            return (p.Name, p.Class, p.ServerId, true);
        if (_entities.IsPlayerLike(id)) return ($"#{id}", GameClass.Unknown, 0, true);
        return (_entities.NpcName(id), GameClass.Unknown, 0, false);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Pets and skill effects whose owner is unknown deal damage under their own id. Two kinds are handled:
    /// <list type="bullet">
    /// <item>pet-like actors — spawned as a pet / effect, or unannounced and using nothing but pet abilities — go to
    /// the one player whose power scalar they carry, or else to the "unknown summons" row (never a player of their own);</item>
    /// <item>unannounced actors with a tiny skill set (a Cleric's aura, totems) go to the one player of the same class
    /// whose scalars match (exactly, or 1000 lower — a buff state) and who runs a real rotation.</item>
    /// </list>
    /// </summary>
    private void AttributeOrphans(Encounter enc, long nowMs)
    {
        List<(Combatant Orphan, Combatant? Owner)>? moves = null;
        foreach (var o in enc.Combatants.Values)
        {
            if (o.ActorId == Combatant.UnknownSummonsId || _entities.IsKnownPlayer(o.ActorId)) continue;
            var spawned = _entities.TryGetUnowned(o.ActorId, out var entity);
            if (spawned || (o.Skills.Count > 0 && o.Skills.Keys.All(_entities.IsEntitySkill)))
            {
                // Give the name / cast links a moment before settling for the power scalar.
                if (nowMs - (spawned ? entity.SpawnMs : o.FirstHitMs) < 3_000) continue;
                (moves ??= new()).Add((o, ScalarOwner(enc, o, needsRotation: false)));
                continue;
            }
            if (o.Total.Hits + o.Total.DotTicks < 3 || o.Skills.Count > 2) continue;
            if (ScalarOwner(enc, o, needsRotation: true) is { } owner) (moves ??= new()).Add((o, owner));
        }
        if (moves is null) return;
        foreach (var (orphan, owner) in moves)
        {
            (owner ?? UnknownSummons(enc)).MergeFrom(orphan);
            enc.Combatants.Remove(orphan.ActorId);
            if (owner is not null) _entities.SetSummon(orphan.ActorId, owner.ActorId);
        }
    }

    /// <summary>The one player whose power scalars the orphan shares; null when none or several qualify.</summary>
    private Combatant? ScalarOwner(Encounter enc, Combatant o, bool needsRotation)
    {
        if (o.Scalars.Count == 0 || (needsRotation && o.Class == GameClass.Unknown)) return null;
        Combatant? owner = null;
        var candidates = 0;
        foreach (var p in enc.Combatants.Values)
        {
            if (p == o || p.ActorId == Combatant.UnknownSummonsId || p.Class == GameClass.Unknown) continue;
            if (o.Class != GameClass.Unknown && p.Class != o.Class) continue;
            if (_entities.IsUnowned(p.ActorId) || p.Skills.Count < (needsRotation ? 3 * o.Skills.Count : 3)) continue;
            if (!needsRotation && p.Skills.Keys.All(_entities.IsEntitySkill)) continue; // another pet, not an owner
            if (!o.Scalars.Any(s => p.Scalars.Contains(s) || p.Scalars.Contains(s + 1_000))) continue;
            owner = p;
            candidates++;
        }
        return candidates == 1 ? owner : null;
    }

    private static Combatant UnknownSummons(Encounter enc)
    {
        if (enc.Combatants.TryGetValue(Combatant.UnknownSummonsId, out var c)) return c;
        c = new Combatant { ActorId = Combatant.UnknownSummonsId, Name = "Summons" };
        enc.Combatants[c.ActorId] = c;
        return c;
    }

    /// <summary>Ties a pet / effect to its owner; damage it already dealt in the running fight moves along.</summary>
    private void LinkSummon(uint summonId, uint ownerId)
    {
        _entities.SetSummon(summonId, ownerId);
        if (!_entities.IsSummon(summonId)) return; // refused: the id belongs to an announced player
        if (_current is not { IsActive: true } enc || !enc.Combatants.TryGetValue(summonId, out var pet)) return;
        GetCombatant(enc, _entities.ResolveOwner(ownerId)).MergeFrom(pet);
        enc.Combatants.Remove(summonId);
    }

    /// <summary>A character was named: effects spawned with that caster name can now be linked.</summary>
    private void LinkSummonsCastBy(uint playerId, string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        foreach (var id in _entities.UnownedCastBy(name)) LinkSummon(id, playerId);
    }

    // Player casts of the last few seconds, to tie lingering skill effects (Bittercold Wind, Fire Wall …) to a caster.
    private readonly List<CastEvent> _recentCasts = new();
    private readonly HashSet<uint> _castLinkTried = new();

    /// <summary>
    /// An effect entity announces its own ticks (actor = target = itself) with its caster's skill variant, last digit
    /// changed (cast 15280140 → ticks 15280141..3). Its owner is the most recent caster of that variant up to 3 s before
    /// it spawned — the variant tells apart players of the same class whose skills are levelled differently.
    /// </summary>
    // 1 HP: which skill puts each buff on — learnt from the caster's cast just before the buff arrives, so the card can
    // show the skill's own icon for the buff (the game data the meter has carries no buff icons)
    private readonly Dictionary<uint, (int Skill, long Ms)> _lastCastBy = new();
    private readonly Dictionary<int, int> _buffSkills = new();

    /// <summary>Changes whenever a buff's skill is learnt (to save the list now and then).</summary>
    public int BuffSkillsVersion { get; private set; }

    private void LearnBuffSkill(BuffEvent buff)
    {
        if (buff.Removing || buff.CasterId == 0 || _buffSkills.ContainsKey(buff.Code)) return;
        if (!_lastCastBy.TryGetValue(buff.CasterId, out var cast) || buff.TimeMs - cast.Ms is < 0 or > 1_500) return;
        _buffSkills[buff.Code] = cast.Skill;
        BuffSkillsVersion++;
    }

    /// <summary>The skill that puts this buff on, 0 while not known.</summary>
    public int BuffSkill(int buffCode)
    {
        lock (_gate) return _buffSkills.GetValueOrDefault(buffCode);
    }

    public Dictionary<int, int> ExportBuffSkills()
    {
        lock (_gate) return new Dictionary<int, int>(_buffSkills);
    }

    public void ImportBuffSkills(IReadOnlyDictionary<int, int> known)
    {
        lock (_gate)
            foreach (var (buff, skill) in known) _buffSkills.TryAdd(buff, skill);
    }

    private void OnCast(CastEvent c)
    {
        if (_entities.TryGetUnowned(c.ActorId, out var entity))
        {
            if (c.TargetId != c.ActorId || !_castLinkTried.Add(c.ActorId)) return;
            var variant = c.SkillCode / 10;
            for (var i = _recentCasts.Count - 1; i >= 0; i--)
            {
                var cast = _recentCasts[i];
                if (cast.TimeMs > entity.SpawnMs + 300) continue;
                if (cast.TimeMs < entity.SpawnMs - 3_000) break;
                if (cast.SkillCode / 10 != variant) continue;
                LinkSummon(c.ActorId, cast.ActorId);
                return;
            }
            return;
        }
        if (_entities.IsSummon(c.ActorId) || _entities.IsKnownNpc(c.ActorId)) return;
        _recentCasts.Add(c);
        var cutoff = c.TimeMs - 6_000;
        var stale = 0;
        while (stale < _recentCasts.Count && _recentCasts[stale].TimeMs < cutoff) stale++;
        if (stale > 0) _recentCasts.RemoveRange(0, stale);
        if (_castLinkTried.Count > 4_096) _castLinkTried.Clear();
    }

    // ---------------------------------------------------------------- boss sightings (respawn timers)

    private readonly List<BossNotice> _pendingNotices = new();
    private readonly List<FieldBossListEvent> _pendingLists = new();

    /// <summary>The in-game map's field boss list arrived (respawn times from the server). Raised outside the lock.</summary>
    public event Action<FieldBossListEvent>? FieldBossListed;

    /// <summary>A world boss (per the data tables) came into view, died, or was found dead. Raised outside the lock.</summary>
    public event Action<BossNotice>? BossNoticed;

    private void NoticeBoss(NpcInfo npc, BossNoticeKind kind, long timeMs, float x = 0, float y = 0, float z = 0)
    {
        if (npc.NpcCode == 0 || !_data.Npcs.TryGetValue(npc.NpcCode, out var def) || !def.IsBoss || def.IsDummy) return;
        if (GameData.IsDungeonMap(_entities.MapId)) return; // instance bosses do not respawn on a timer
        _pendingNotices.Add(new BossNotice(npc.NpcCode, _entities.MapId, _entities.ZoneName, kind, timeMs, x, y, z, _entities.Self?.ServerId ?? 0));
    }

    private bool IsLikelyPlayer(uint id) =>
        _entities.IsPlayerLike(id) || (_current is { IsActive: true } enc && enc.Combatants.ContainsKey(id));

    private bool IsLikelyNpc(uint id) =>
        (_entities.IsKnownNpc(id) && !_entities.IsPlayerLike(id)) ||
        (_current is { IsActive: true } enc && enc.DamageByTarget.ContainsKey(id));

    private bool IsAlive(uint npcId) => !_entities.TryGetNpc(npcId, out var n) || n.Hp != 0;

    private void SetBoss(Encounter enc, NpcInfo npc)
    {
        enc.BossId = npc.ActorId;
        enc.BossHp = npc.Hp;
        RefreshBoss(enc, npc);
    }

    /// <summary>The boss's identity and max HP as known now: the server may announce them after the fight began.</summary>
    private void RefreshBoss(Encounter enc, NpcInfo npc)
    {
        enc.BossCode = npc.NpcCode;
        enc.BossName = _entities.NpcName(npc.ActorId);
        enc.BossMaxHp = npc.MaxHp;
        enc.BossMaxHpKnown = npc.MaxHpKnown;
    }

    private void StartEncounter(long timeMs)
    {
        _current = new Encounter
        {
            StartMs = timeMs, LastDamageMs = timeMs, Zone = _entities.ZoneName, InDungeon = GameData.IsDungeonMap(_entities.MapId),
        };
        _viewCleared = false;
    }

    private Combatant GetCombatant(Encounter enc, uint actorId)
    {
        if (enc.Combatants.TryGetValue(actorId, out var c)) return c;
        c = new Combatant { ActorId = actorId };
        RefreshIdentity(c);
        enc.Combatants[actorId] = c;
        return c;
    }

    private void RefreshIdentity(Combatant c)
    {
        if (_entities.TryGetPlayer(c.ActorId, out var p))
        {
            if (!string.IsNullOrEmpty(p.Name)) c.Name = p.Name;
            if (p.Class != GameClass.Unknown) c.Class = p.Class;
            c.ServerId = p.ServerId;
        }
        c.IsSelf = _entities.SelfId == c.ActorId;
        if (string.IsNullOrEmpty(c.Name)) c.Name = $"#{c.ActorId}";
    }

    private void CheckIdle(long nowMs)
    {
        if (_members.Count > 0) ExpireMembers(nowMs);
        if (_pvp.EndIfIdle(nowMs))
        {
            if (_pvp.Snapshot(nowMs, WhoPvp) is { } ended) _pendingPvp.Add(ended);
            _pvp.Reset();
        }
        if (_current is not { IsActive: true } enc) return;
        var bossAlive = enc.BossId is { } b && IsAlive(b);
        var dummy = enc.BossCode != 0 && _data.Npcs.TryGetValue(enc.BossCode, out var def) && def.IsDummy;
        var timeout = dummy ? Options.DummyIdleTimeoutMs : bossAlive ? Options.BossIdleTimeoutMs : Options.IdleTimeoutMs;
        if (nowMs - enc.LastDamageMs > timeout) Finish(EncounterEndReason.Idle, enc.LastDamageMs);
    }

    private void Finish(EncounterEndReason reason, long endMs)
    {
        var enc = _current;
        if (enc is null || !enc.IsActive) return;
        enc.Reason = reason;
        enc.EndMs = Math.Max(endMs, enc.StartMs);
        if (enc.TotalDamage <= 0) return;
        AttributeOrphans(enc, long.MaxValue / 2);
        if (!IsOurs(enc)) return; // 1 HP: a fight nearby you took no part in is neither shown nor saved
        if (_run is { } run && enc.InDungeon)
        {
            foreach (var c in enc.Combatants.Values) RefreshIdentity(c);
            run.AddFight(enc, Title(enc));
        }
        // …nor one where the party filter would show no damage at all (before the meter knew you)
        if (PartyFilter(enc) is { } shown && !enc.Combatants.Values.Any(c => shown(c) && c.Total.Damage > 0)) return;

        _finished.Insert(0, enc);
        if (_finished.Count > Options.MaxSegments) _finished.RemoveAt(_finished.Count - 1);
        _justFinished.Add(enc);
    }

    private void FlushFinished()
    {
        List<FightRecord>? records = null;
        List<BossNotice>? notices = null;
        List<FieldBossListEvent>? lists = null;
        List<DeathRecap>? recaps = null;
        List<string>? partyDeaths = null;
        List<PartyRequestEvent>? requests = null;
        List<PvpSession>? pvp = null;
        List<DungeonRun>? runs = null;
        lock (_gate)
        {
            if (_pendingRuns.Count > 0)
            {
                runs = new List<DungeonRun>(_pendingRuns);
                _pendingRuns.Clear();
            }
            if (_pendingPvp.Count > 0)
            {
                pvp = new List<PvpSession>(_pendingPvp);
                _pendingPvp.Clear();
            }
            if (_pendingRecaps.Count > 0)
            {
                recaps = new List<DeathRecap>(_pendingRecaps);
                _pendingRecaps.Clear();
            }
            if (_pendingRequests.Count > 0)
            {
                requests = new List<PartyRequestEvent>(_pendingRequests);
                _pendingRequests.Clear();
            }
            if (_pendingPartyDeaths.Count > 0)
            {
                partyDeaths = new List<string>(_pendingPartyDeaths);
                _pendingPartyDeaths.Clear();
            }
            if (_pendingNotices.Count > 0)
            {
                notices = new List<BossNotice>(_pendingNotices);
                _pendingNotices.Clear();
            }
            if (_pendingLists.Count > 0)
            {
                lists = new List<FieldBossListEvent>(_pendingLists);
                _pendingLists.Clear();
            }
            if (_justFinished.Count > 0)
            {
                records = new List<FightRecord>(_justFinished.Count);
                foreach (var enc in _justFinished)
                {
                    foreach (var c in enc.Combatants.Values) RefreshIdentity(c);
                    records.Add(BuildRecord(enc));
                }
                _justFinished.Clear();
            }
        }
        if (notices is not null && BossNoticed is { } notify)
            foreach (var n in notices) notify(n);
        if (lists is not null && FieldBossListed is { } listed)
            foreach (var l in lists) listed(l);
        if (runs is not null && DungeonRunEnded is { } runEnded)
            foreach (var r in runs) runEnded(r);
        if (pvp is not null && PvpSessionEnded is { } ended)
            foreach (var s in pvp) ended(s);
        if (recaps is not null && DeathRecapped is { } died)
            foreach (var r in recaps) died(r);
        if (partyDeaths is not null && PartyMemberDied is { } fell)
            foreach (var n in partyDeaths) fell(n);
        if (requests is not null && PartyRequested is { } asked)
            foreach (var r in requests) asked(r);
        if (records is not null && EncounterFinished is { } handler)
            foreach (var r in records) handler(r);
    }

    // ---------------------------------------------------------------- snapshots

    public IReadOnlyList<SegmentInfo> Segments()
    {
        lock (_gate)
        {
            var list = new List<SegmentInfo>(_finished.Count + 1);
            if (_current is { IsActive: true } cur && IsOurs(cur)) list.Add(Info(cur));
            foreach (var e in _finished) list.Add(Info(e));
            return list;
        }

        SegmentInfo Info(Encounter e) => new(e.Id, Title(e), ToTime(e.StartMs), e.CombatMs, e.IsActive, e.Reason, e.TotalDamage);
    }

    /// <summary>
    /// <paramref name="segmentId"/> null means "live": the running segment, or the last finished one until a new
    /// fight starts (so results stay readable after a kill).
    /// </summary>
    public EncounterSnapshot? Snapshot(Guid? segmentId, long nowMs)
    {
        lock (_gate)
        {
            var enc = Find(segmentId);
            return enc is null ? null : BuildSnapshot(enc, nowMs);
        }
    }

    public CombatantDetail? Detail(Guid? segmentId, uint actorId)
    {
        lock (_gate)
        {
            var enc = Find(segmentId);
            if (enc is null || !enc.Combatants.TryGetValue(actorId, out var c)) return null;
            RefreshIdentity(c);
            return BuildDetail(enc, c, Combatant.MaxCasts);
        }
    }

    /// <summary>The running fight at a glance: its id, whether a boss is engaged, whether the local player hit anything.</summary>
    public (Guid Id, bool Boss, bool Self)? LiveFight()
    {
        lock (_gate)
        {
            if (_current is not { IsActive: true } e || !IsOurs(e)) return null;
            return (e.Id, e.BossId is not null, _entities.SelfId is { } id && e.Combatants.ContainsKey(id));
        }
    }

    public FightRecord? Record(Guid? segmentId)
    {
        lock (_gate)
        {
            var enc = Find(segmentId);
            return enc is null ? null : BuildRecord(enc);
        }
    }

    private Encounter? Find(Guid? id)
    {
        if (id is null)
        {
            if (_current is { IsActive: true } && IsOurs(_current)) return _current;
            return _viewCleared ? null : _finished.FirstOrDefault();
        }
        if (_current?.Id == id) return _current;
        return _finished.FirstOrDefault(e => e.Id == id);
    }

    private string Title(Encounter e)
    {
        if (!string.IsNullOrEmpty(e.BossName)) return e.BossName!;
        if (e.DamageByTarget.Count > 0)
        {
            var top = e.DamageByTarget.MaxBy(kv => kv.Value).Key;
            var name = _entities.NpcName(top);
            return e.DamageByTarget.Count > 1 ? $"{name} +{e.DamageByTarget.Count - 1}" : name;
        }
        return e.Zone ?? "Combat";
    }

    /// <summary>NPC template of the fight's main target (boss, else the most damaged NPC) — lets history re-localise titles.</summary>
    private int MainTargetCode(Encounter e)
    {
        if (e.BossCode != 0) return e.BossCode;
        if (e.DamageByTarget.Count == 0) return 0;
        var top = e.DamageByTarget.MaxBy(kv => kv.Value).Key;
        return _entities.TryGetNpc(top, out var npc) ? npc.NpcCode : 0;
    }

    private static DateTimeOffset ToTime(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime();

    /// <summary>
    /// 1 HP: a complete roster replaces the party (one name: on your own again); a partial one only adds the members
    /// it could read, so a record the parser trips over never drops someone from the party.
    /// </summary>
    private void OnPartyRoster(PartyRosterEvent roster)
    {
        var names = roster.Names.Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        // Your own roster always lists you: one without you is another party's (or bytes that only look like a roster).
        if (_entities.Self?.Name is { Length: > 0 } me && !names.Contains(me.Trim(), StringComparer.OrdinalIgnoreCase)) return;
        if (roster.Powers is { } powers)
            foreach (var (name, power) in powers)
                if (power > 0 && !string.IsNullOrWhiteSpace(name)) _powers[name.Trim()] = power;
        // 1 HP: the roster's gear score too — the party's GS without inspecting anyone
        if (roster.Gear is { } gear)
            foreach (var (name, score) in gear)
                if (score > 0 && !string.IsNullOrWhiteSpace(name)) _gear[name.Trim()] = score;
        if (roster.Complete)
        {
            _party.Clear();
            _manualIn.Clear();
            _manualOut.Clear();
            foreach (var gone in _members.Keys.Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList()) _members.Remove(gone);
        }
        _healed.Clear(); // the roster tells who is in the party
        foreach (var name in names) _party.Add(name);
        if (roster.Complete && _party.Count <= 1) _party.Clear();
        _partyVersion++;
    }

    /// <summary>
    /// 1 HP: party members by the game's party share packet (<c>23 92</c>: sent for your party's members only, right
    /// after their kill credit — loot) — the party even when no roster came (Global rarely sends one) and you heal no
    /// one. Nothing comes while a boss is still alive, so someone invited mid-fight shows from the kill on, or at once
    /// when added by hand (<see cref="SetPartyMember"/>). A member with no share for
    /// <see cref="PartyMemberTimeoutMs"/> left the party.
    /// </summary>
    private readonly Dictionary<string, long> _members = new(StringComparer.OrdinalIgnoreCase);

    public const long PartyMemberTimeoutMs = 30 * 60_000;

    // 1 HP: players put in or out of the party by hand (the row's menu) — over anything the meter guessed.
    private readonly HashSet<string> _manualIn = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _manualOut = new(StringComparer.OrdinalIgnoreCase);

    // …and by id the players the meter has no name for yet ("#12345", ids last until the next zone).
    private readonly HashSet<uint> _manualIds = new();

    /// <summary>1 HP: puts a player in your party, or takes them out, by hand; false for you.</summary>
    public bool SetPartyMember(uint actorId, bool inParty)
    {
        lock (_gate)
        {
            if (_entities.SelfId == actorId) return false;
            if (!_entities.TryGetPlayer(actorId, out var p) || string.IsNullOrWhiteSpace(p.Name))
            {
                if (inParty ? !_manualIds.Add(actorId) : !_manualIds.Remove(actorId)) return false;
                _partyVersion++;
                return true;
            }
            _manualIds.Remove(actorId);
            var name = p.Name.Trim();
            (inParty ? _manualIn : _manualOut).Add(name);
            (inParty ? _manualOut : _manualIn).Remove(name);
            _partyVersion++;
            return true;
        }
    }

    /// <summary>1 HP: null for you; otherwise whether the party filter counts them.</summary>
    public bool? IsPartyMember(uint actorId)
    {
        lock (_gate)
        {
            if (_entities.SelfId == actorId) return null;
            if (!_entities.TryGetPlayer(actorId, out var p) || string.IsNullOrWhiteSpace(p.Name)) return _manualIds.Contains(actorId);
            return InParty(p.Name);
        }
    }

    private readonly BuffBook _buffBook = new();

    /// <summary>1 HP: your combat power as the game last sent it (<c>56 36</c>); 0 until then.</summary>
    public long SelfPower { get; private set; }

    /// <summary>1 HP: a player against you — gear score when both are inspected, else combat power.</summary>
    public ThreatInfo ThreatOf(string name, ThreatThresholds thresholds)
    {
        lock (_gate)
        {
            var me = (_entities.Self?.Name ?? KnownSelfName)?.Trim();
            var myGear = me is { Length: > 0 } ? _gear.GetValueOrDefault(me) : 0;
            var myPower = SelfPower > 0 ? SelfPower : me is { Length: > 0 } ? _powers.GetValueOrDefault(me) : 0;
            return PvpThreat.Assess(myGear, _gear.GetValueOrDefault(name.Trim()), myPower, _powers.GetValueOrDefault(name.Trim()), thresholds);
        }
    }

    // 1 HP: your tickets and energies as the game last sent them (id → count, bonus).
    private readonly Dictionary<int, (int Count, int Bonus)> _resources = new();

    /// <summary>1 HP: your tickets and energies (ids in <see cref="ResourceEvent"/>); empty until the game sends them.</summary>
    public IReadOnlyDictionary<int, (int Count, int Bonus)> Resources()
    {
        lock (_gate) return new Dictionary<int, (int, int)>(_resources);
    }

    // 1 HP: where an announced entity was last reported — spawn position, or anyone's move correction.
    private void Remember(uint actorId, float x, float y, float z, long timeMs)
    {
        if (_positions.Count > 8_192) _positions.Clear(); // a busy zone: start over rather than grow without end
        _positions[actorId] = (x, y, z, timeMs);
    }

    private static string? ClassName(GameClass cls) => cls == GameClass.Unknown ? null : cls.ToString().ToLowerInvariant();

    // 1 HP: skill cooldowns of you and your party: (actor, skill without its variant) → skill, start, ready at, charges.
    private readonly Dictionary<(uint Actor, int Skill), (int Code, long StartMs, long ReadyMs, int Charges)> _cooldowns = new();

    // 1 HP: where every announced entity (NPC spawn, anyone's move) was last reported, in game units (centimetres).
    // The local /state endpoint uses it for the distance to the target and to party members; the zone change clears it.
    private readonly Dictionary<uint, (float X, float Y, float Z, long Ms)> _positions = new();

    // 1 HP: skills seen putting on a buff that lasts until removed — the game's toggles (mantras, stances).
    private readonly HashSet<int> _toggleSkills = new(); // skill code / 10 000, like _cooldowns

    /// <summary>
    /// 1 HP: the live character and party state for the local <c>/state</c> endpoint (App/Services/StreamServer.cs), or
    /// null when there is nothing live to report — you are not identified yet, or the game's stream went quiet (the
    /// game is not running). Read under the same lock as the snapshots, so a caller off the capture thread is safe.
    /// </summary>
    public LiveState? State(long nowMs)
    {
        lock (_gate)
        {
            if (_entities.SelfId is not { } me) return null;
            if (LastEventMs == 0 || nowMs - LastEventMs > 30_000) return null;

            var self = _entities.Self;
            var (hp, hpMax, _) = _playerHp.TryGetValue(me, out var mine) ? mine : (-1L, 0L, 0L);
            var (mp, mpMax, _) = _playerMp.TryGetValue(me, out var mana) ? mana : (-1L, 0L, 0L);
            var buffs = _buffBook.On(me, nowMs);

            LiveBuff Buff(ActiveBuff b) =>
                new(b.Code, b.Name, b.EndMs > 0 ? Math.Max(0, b.EndMs - nowMs) : null, null, null);

            // A toggle is a skill seen putting on a buff that lasts until removed: on while that buff is on, off otherwise.
            bool? ToggleOn(int key) => !_toggleSkills.Contains(key) ? null
                : buffs.Any(b => b.CasterId == me && _buffSkills.TryGetValue(b.Code, out var src) && src / 10_000 == key);

            var skills = new List<LiveSkill>();
            var seen = new HashSet<int>();
            foreach (var ((actor, key), (code, start, ready, charges)) in _cooldowns)
            {
                if (actor != me || !seen.Add(key)) continue;
                skills.Add(new LiveSkill(GameData.BaseSkill(code), _data.SkillName(code), Math.Max(0, ready - nowMs),
                    Math.Max(0, ready - start), charges >= 0 ? charges : null, null, ToggleOn(key)));
            }
            // Skills whose lasting buff is on you right now: a toggle seen in the buff list but not cast since the start.
            foreach (var b in buffs.Where(b => b.CasterId == me))
            {
                if (!_buffSkills.TryGetValue(b.Code, out var source) || source <= 0 || !seen.Add(source / 10_000)) continue;
                skills.Add(new LiveSkill(GameData.BaseSkill(source), _data.SkillName(source), 0, 0, null, null, ToggleOn(source / 10_000)));
            }
            foreach (var key in _toggleSkills.Where(key => seen.Add(key)))
                skills.Add(new LiveSkill(key * 10_000, _data.SkillName(key * 10_000), 0, 0, null, null, ToggleOn(key)));
            skills.Sort((a, b) => (a.Id ?? int.MaxValue).CompareTo(b.Id ?? int.MaxValue));

            LiveTarget? target = null;
            if (_current is { IsActive: true } fight)
            {
                var id = fight.BossId ?? 0;
                if (id == 0 && fight.DamageByTarget.Count > 0) id = fight.DamageByTarget.MaxBy(kv => kv.Value).Key;
                if (id != 0 && _entities.TryGetNpc(id, out var npc))
                {
                    var name = npc.Name is { Length: > 0 } n ? n : npc.NpcCode != 0 ? _data.NpcName(npc.NpcCode) : null;
                    target = new LiveTarget(name, npc.Hp >= 0 ? npc.Hp : null, npc.MaxHp > 0 ? npc.MaxHp : null, true, Distance(id));
                }
            }

            var party = new List<LivePartyMember>();
            foreach (var name in PartySet().Where(n => !IsSelfName(n))
                         .OrderBy(n => _partySlots.GetValueOrDefault(n, int.MaxValue))
                         .ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                // A member the meter has no actor id for yet still has a row, without HP, buffs or distance.
                if (_entities.FindPlayerByName(name) is not { } id)
                {
                    party.Add(new LivePartyMember(name, null, null, null, null, null, null, []));
                    continue;
                }
                _entities.TryGetPlayer(id, out var player);
                long? memberHp = null, memberMax = null;
                bool? alive = null;
                if (_playerHp.TryGetValue(id, out var ph) && ph.Hp >= 0)
                {
                    memberHp = ph.Hp;
                    memberMax = ph.Max > 0 ? ph.Max : null;
                    alive = ph.Hp > 0;
                }
                party.Add(new LivePartyMember(player?.Name is { Length: > 0 } pn ? pn : name, ClassName(player?.Class ?? GameClass.Unknown),
                    alive, memberHp, memberMax, null, Distance(id),
                    _buffBook.On(id, nowMs).Where(b => b.Debuff).Select(Buff).ToList()));
            }

            return new LiveState(nowMs,
                new LiveSelf(self?.Name is { Length: > 0 } myName ? myName : null, ClassName(self?.Class ?? GameClass.Unknown),
                    hp >= 0 ? hp > 0 : null, hp >= 0 ? hp : null, hpMax > 0 ? hpMax : null,
                    mp >= 0 ? mp : null, mpMax > 0 ? mpMax : null,
                    _current is { IsActive: true },
                    null, // a cast in progress is not announced with its end
                    buffs.Where(b => !b.Debuff).Select(Buff).ToList(),
                    buffs.Where(b => b.Debuff).Select(Buff).ToList(),
                    skills),
                target, party);

            // Metres between you and an actor, from the last positions both reported. Your own position is only sent with
            // movement corrections (mostly skills and dashes), so a stale one would invent a distance: 30 s or nothing.
            double? Distance(uint other)
            {
                if (!_positions.TryGetValue(me, out var at) || nowMs - at.Ms > 30_000) return null;
                if (!_positions.TryGetValue(other, out var to)) return null;
                var dx = (double)at.X - to.X;
                var dy = (double)at.Y - to.Y;
                var dz = (double)at.Z - to.Z;
                return Math.Round(Math.Sqrt(dx * dx + dy * dy + dz * dz) / 100, 1); // game units are centimetres
            }
        }
    }

    /// <summary>
    /// 1 HP: your party's long cooldowns (from <paramref name="minMs"/>) that are still running, the soonest ready
    /// first — who, which skill, how long is left. You are left out (you see your own on the skill bar).
    /// </summary>
    public IReadOnlyList<PartyCooldown> PartyCooldowns(long nowMs, long minMs = 60_000)
    {
        lock (_gate)
        {
            foreach (var key in _cooldowns.Where(kv => kv.Value.ReadyMs < nowMs).Select(kv => kv.Key).ToList()) _cooldowns.Remove(key);
            var list = new List<PartyCooldown>();
            foreach (var ((actor, _), (code, start, ready, _)) in _cooldowns)
            {
                if (ready - start < minMs || actor == _entities.SelfId) continue;
                if (!_entities.TryGetPlayer(actor, out var p) || string.IsNullOrEmpty(p.Name) || !InParty(p.Name)) continue;
                list.Add(new PartyCooldown(p.Name, _data.SkillName(code), ready - nowMs, ready - start, code));
            }
            return list.OrderBy(c => c.LeftMs).ToList();
        }
    }

    // 1 HP: players' HP as the game last sent it: actor → hp, max, when.
    private readonly Dictionary<uint, (long Hp, long Max, long AtMs)> _playerHp = new();

    // 1 HP: players' MP as the game last sent it (00 8D resource 1): actor → mp, max, when.
    private readonly Dictionary<uint, (long Mp, long MpMax, long AtMs)> _playerMp = new();

    /// <summary>
    /// 1 HP: the party window — you first, then your party: class, HP (as last sent), the buffs and debuffs on them, and
    /// their running cooldowns (yours from <paramref name="selfMinMs"/>, the others' from <paramref name="partyMinMs"/>).
    /// A member the meter has not seen yet (no actor id) still has a row, without HP.
    /// </summary>
    public IReadOnlyList<PartyFrameMember> PartyFrame(long nowMs, long selfMinMs = 1_000, long partyMinMs = 30_000)
    {
        lock (_gate)
        {
            var rows = new List<PartyFrameMember>();
            var selfName = (_entities.Self?.Name ?? KnownSelfName)?.Trim();
            if (_entities.SelfId is { } me) rows.Add(Member(me, selfName ?? "", true, selfMinMs));
            foreach (var name in PartySet().Where(n => !IsSelfName(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                rows.Add(_entities.FindPlayerByName(name) is { } id ? Member(id, name, false, partyMinMs)
                    : new PartyFrameMember(name, GameClass.Unknown, false, null, -1, 0, 0, [], []));
            return rows;

            PartyFrameMember Member(uint id, string name, bool self, long minMs)
            {
                _entities.TryGetPlayer(id, out var p);
                var (hp, max, at) = _playerHp.TryGetValue(id, out var v) ? v : (-1L, 0L, 0L);
                var cds = _cooldowns.Where(kv => kv.Key.Actor == id && kv.Value.ReadyMs > nowMs && kv.Value.ReadyMs - kv.Value.StartMs >= minMs)
                    .Select(kv => new PartyCooldown(name, _data.SkillName(kv.Value.Code), kv.Value.ReadyMs - nowMs, kv.Value.ReadyMs - kv.Value.StartMs, kv.Value.Code))
                    .OrderBy(c => c.LeftMs).ToList();
                return new PartyFrameMember(p?.Name is { Length: > 0 } n ? n : name, p?.Class ?? GameClass.Unknown, self, id, hp, max, at, _buffBook.On(id, nowMs), cds);
            }
        }
    }

    /// <summary>1 HP: the buffs on you now, the soonest to run out first (debuffs too).</summary>
    public IReadOnlyList<ActiveBuff> SelfBuffs(long nowMs)
    {
        lock (_gate) return _entities.SelfId is { } me ? _buffBook.On(me, nowMs) : [];
    }

    /// <summary>1 HP: the buffs on a player or a boss now.</summary>
    public IReadOnlyList<ActiveBuff> BuffsOn(uint actorId, long nowMs)
    {
        lock (_gate) return _buffBook.On(actorId, nowMs);
    }

    // 1 HP: the game's own party packets came (00 92 / 0D 92 / 06 97): the party is known, guesses stay off.
    private bool _partyFromGame;

    private bool IsSelfName(string name) =>
        (_entities.Self?.Name is { Length: > 0 } me && string.Equals(me.Trim(), name, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(KnownSelfName) && string.Equals(KnownSelfName.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private void ClearParty()
    {
        _party.Clear();
        _partySlots.Clear();
        _members.Clear();
        _healed.Clear();
        _manualIn.Clear();
        _manualOut.Clear();
        _manualIds.Clear();
        _partyVersion++;
    }

    /// <summary>1 HP: <c>00 92</c> — the whole party: it replaces whatever the meter knew or guessed.</summary>
    private void OnPartyList(PartyListEvent list)
    {
        ClearParty();
        _partyFromGame = true;
        foreach (var m in list.Members)
            if (_entities.SelfId != m.ActorId && !IsSelfName(m.Name))
            {
                var name = m.Name.Trim();
                _party.Add(name);
                _partySlots[name] = m.Slot;
            }
    }

    /// <summary>1 HP: <c>0D 92</c> — someone joined (or a member's card): in the party from now, the whole fight counted.</summary>
    private void OnPartyJoin(PartyJoinEvent join)
    {
        var name = join.Member.Name.Trim();
        if (_entities.SelfId == join.Member.ActorId || IsSelfName(name)) return;
        _partyFromGame = true;
        _healed.Clear();
        _manualOut.Remove(name);
        _partySlots[name] = join.Member.Slot;
        if (_party.Add(name)) _partyVersion++;
    }

    private void OnPartyMember(PartyMemberEvent m)
    {
        var name = m.Name.Trim();
        if (name.Length == 0) return;
        if (_entities.Self?.Name is { Length: > 0 } me && string.Equals(me.Trim(), name, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.IsNullOrEmpty(KnownSelfName) && string.Equals(KnownSelfName.Trim(), name, StringComparison.OrdinalIgnoreCase)) return;
        _healed.Clear(); // the game names the party: guesses from heals are no longer needed
        if (_members.TryAdd(name, m.TimeMs)) _partyVersion++;
        else _members[name] = Math.Max(_members[name], m.TimeMs);
    }

    private void ExpireMembers(long nowMs)
    {
        foreach (var gone in _members.Where(kv => nowMs - kv.Value > PartyMemberTimeoutMs).Select(kv => kv.Key).ToList())
        {
            _members.Remove(gone);
            _partyVersion++;
        }
    }

    /// <summary>
    /// The party as known: the roster with the members the status packet names, or — while neither came — the players
    /// you healed (an area heal also lands on players nearby, so it is only the last resort).
    /// </summary>
    private IEnumerable<string> PartySet() =>
        (_party.Count == 0 && _members.Count == 0 ? _healed : _party.Concat(_members.Keys))
        .Concat(_manualIn).Where(n => !_manualOut.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsNamed(Combatant c) => c.Name.Length > 0 && c.Name[0] != '#';

    /// <summary>The party roster or status packets, or — while neither came — the players you healed.</summary>
    private bool HasParty => _party.Count > 0 || _healed.Count > 0 || _members.Count > 0 || _manualIn.Count > 0 || _manualIds.Count > 0;

    private bool InParty(string name)
    {
        name = name.Trim();
        if (_manualOut.Contains(name)) return false;
        if (_manualIn.Contains(name)) return true;
        if (_party.Count == 0 && _members.Count == 0) return _healed.Contains(name);
        return _party.Contains(name) || _members.ContainsKey(name);
    }

    /// <summary>1 HP: the open world with you known — where fights of players passing by are told apart from yours.</summary>
    private bool WorldScope() => _entities.SelfId is not null && !GameData.IsDungeonMap(_entities.MapId);

    /// <summary>You, or a party member by name.</summary>
    private bool IsOursActor(uint id)
    {
        if (_entities.SelfId == id || _manualIds.Contains(id)) return true;
        return HasParty && _entities.TryGetPlayer(id, out var p) && !string.IsNullOrEmpty(p.Name) && InParty(p.Name);
    }

    /// <summary>A named player who is neither you nor in your party.</summary>
    private bool IsStranger(uint id) =>
        _entities.SelfId != id && _entities.TryGetPlayer(id, out var p) && !string.IsNullOrEmpty(p.Name) && !InParty(p.Name);

    /// <summary>
    /// 1 HP: a fight you or your party take part in. Outside instances a fight of players passing by is not shown or
    /// saved (in an instance everyone is with you; before the meter knows you it cannot tell).
    /// </summary>
    private bool IsOurs(Encounter enc)
    {
        if (enc.InDungeon || _entities.SelfId is not { } self) return true;
        foreach (var c in enc.Combatants.Values)
        {
            if (c.ActorId == self || c.IsSelf) return true;
            if (HasParty && _entities.TryGetPlayer(c.ActorId, out var p) && !string.IsNullOrEmpty(p.Name) && InParty(p.Name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 1 HP: who is listed with <see cref="MeterOptions.PartyOnly"/>: you, your party (by name) and the pets nobody
    /// could be tied to (listed last, never ranked). A party member the meter has no name for yet still counts while
    /// a roster name has no row. Null: everyone (the option is off, an instance, or the meter does not know you yet).
    /// </summary>
    private Func<Combatant, bool>? PartyFilter(Encounter enc)
    {
        if (!Options.PartyOnly || enc.InDungeon) return null;
        if (_entities.SelfId is null)
        {
            // You are not known yet (a meter started mid-session): your party and your name from earlier runs only.
            if (!HasParty) return null;
            var me = KnownSelfName?.Trim();
            return c => _manualIds.Contains(c.ActorId) || IsNamed(c) && (InParty(c.Name) || (me is { Length: > 0 } && string.Equals(c.Name.Trim(), me, StringComparison.OrdinalIgnoreCase)));
        }
        // Strictly by name: a player the meter has no name for yet, and pets nobody could be tied to, may be anyone's.
        return c => c.IsSelf || _manualIds.Contains(c.ActorId) || (IsNamed(c) && InParty(c.Name));
    }

    /// <summary>1 HP: what the party filter is doing right now, for the overlay's PARTY chip.</summary>
    public PartyFilterStatus PartyStatus()
    {
        lock (_gate)
        {
            var state = !Options.PartyOnly ? PartyFilterState.Off
                : _current is { IsActive: true, InDungeon: true } || GameData.IsDungeonMap(_entities.MapId) ? PartyFilterState.Instance
                : _entities.SelfId is null && !HasParty ? PartyFilterState.SelfUnknown
                : !HasParty ? PartyFilterState.Solo
                : PartyFilterState.Party;
            var source = _partyFromGame && _party.Count > 0 ? PartySource.Game
                : _party.Count > 0 ? PartySource.Roster
                : _members.Count > 0 ? PartySource.Loot
                : _manualIn.Count > 0 || _manualIds.Count > 0 ? PartySource.Manual
                : _healed.Count > 0 ? PartySource.Heals
                : PartySource.None;
            return new PartyFilterStatus(state, PartySet().OrderBy(n => n).ToList(), source);
        }
    }

    private EncounterSnapshot BuildSnapshot(Encounter enc, long nowMs)
    {
        var combatMs = enc.CombatMs;
        var seconds = combatMs / 1000.0;
        foreach (var c in enc.Combatants.Values) RefreshIdentity(c);
        var filter = PartyFilter(enc);
        var shown = filter is null ? enc.Combatants.Values.ToList() : enc.Combatants.Values.Where(filter).ToList();
        var shownDamage = filter is null ? enc.TotalDamage : shown.Sum(c => c.Total.Damage);
        var total = Math.Max(1, shownDamage);
        var rows = new List<CombatantSnapshot>(shown.Count);
        foreach (var c in shown)
        {
            var t = c.Total;
            rows.Add(new CombatantSnapshot(
                c.ActorId, c.Name, c.Class, c.IsSelf,
                t.Damage, t.Damage / seconds, (double)t.Damage / total,
                t.Hits, t.Hits > 0 ? (double)t.Crits / t.Hits : 0,
                t.Max, c.DamageTaken, c.ServerId, IsNamed(c) ? _powers.GetValueOrDefault(c.Name.Trim()) : 0)
            { Healing = c.Healing, Deaths = c.Deaths });
        }
        rows.Sort((a, b) => b.Damage.CompareTo(a.Damage));
        // Pets without a known owner are not a player: list them last so they never take a place in the ranking.
        if (rows.FindIndex(r => r.ActorId == Combatant.UnknownSummonsId) is var pets and >= 0)
        {
            var row = rows[pets];
            rows.RemoveAt(pets);
            rows.Add(row);
        }

        BossSnapshot? boss = null;
        if (enc.BossId is { } bossId)
        {
            var hp = enc.BossHp;
            if (_entities.TryGetNpc(bossId, out var npc) && enc.IsActive) hp = npc.Hp;
            // HP the boss lost beyond every hit counted on it: the part of the fight the meter did not see.
            var uncounted = enc.BossMaxHpKnown && enc.BossMaxHp > 0 && hp >= 0
                ? Math.Max(0, enc.BossMaxHp - hp - enc.DamageByTarget.GetValueOrDefault(bossId))
                : 0;
            boss = new BossSnapshot(bossId, enc.BossCode, enc.BossName ?? _entities.NpcName(bossId), hp, enc.BossMaxHp,
                enc.BossMaxHpKnown, uncounted) { Forecast = enc.IsActive ? Forecast(enc, hp, nowMs) : null };
        }

        return new EncounterSnapshot(
            enc.Id, Title(enc), enc.Zone, ToTime(enc.StartMs), enc.ClockMs(nowMs), combatMs, enc.IsActive, enc.Reason,
            shownDamage, shownDamage / seconds, boss, rows, MainTargetCode(enc), enc.InDungeon);
    }

    private CombatantDetail BuildDetail(Encounter enc, Combatant c, int maxCasts)
    {
        var seconds = enc.CombatMs / 1000.0;
        var total = Math.Max(1, c.Total.Damage);
        var skills = c.Skills.Values
            .Select(s =>
            {
                var st = s.Stats;
                return new SkillRow(
                    s.SkillCode, _data.SkillName(s.SkillCode), st.Hits, st.DotTicks, st.Damage, st.Damage / seconds,
                    (double)st.Damage / total,
                    st.Damage / Math.Max(1, st.Hits + st.DotTicks),
                    st.Hits > 0 ? st.Min : 0, st.Max,
                    Rate(st.Crits, st.Hits), Rate(st.Back, st.Hits), Rate(st.Front, st.Hits),
                    Rate(st.Perfect, st.Hits), Rate(st.Heavy, st.Hits), Rate(st.Multi, st.Hits));
            })
            .OrderByDescending(s => s.Damage)
            .ToList();

        var t = c.Total;
        var quality = new HitQuality(
            t.Hits, t.DotTicks, Rate(t.Crits, t.Hits), Rate(t.Back, t.Hits), Rate(t.Front, t.Hits),
            Rate(t.Perfect, t.Hits), Rate(t.Heavy, t.Hits), Rate(t.Multi, t.Hits),
            t.Max, c.MaxHitSkill != 0 ? _data.SkillName(c.MaxHitSkill) : null);

        var casts = c.Casts.Count <= maxCasts ? c.Casts.ToArray() : c.Casts.GetRange(0, maxCasts).ToArray();
        return new CombatantDetail(
            c.ActorId, c.Name, c.Class, c.IsSelf, t.Damage, t.Damage / seconds, (double)t.Damage / Math.Max(1, enc.TotalDamage),
            enc.CombatMs, c.DamageTaken, c.HitsTaken, quality, skills, c.PerSecond.ToArray(), casts, c.ServerId)
        {
            Healing = c.Healing,
            HealedBy = Sources(c.HealedBy),
            TakenFrom = Sources(c.TakenFrom),
        };
    }

    /// <summary>1 HP: healers or attackers of a player by name, most first.</summary>
    private IReadOnlyList<SourceRow> Sources(Dictionary<uint, (long Amount, int Hits)> by) =>
        by.Select(kv => new SourceRow(Who(kv.Key).Name, kv.Value.Amount, kv.Value.Hits))
            .GroupBy(r => r.Name).Select(g => new SourceRow(g.Key, g.Sum(r => r.Amount), g.Sum(r => r.Hits)))
            .OrderByDescending(r => r.Amount).ToList();

    private FightRecord BuildRecord(Encounter enc)
    {
        var snapshot = BuildSnapshot(enc, enc.EndMs > 0 ? enc.EndMs : enc.LastDamageMs);
        var details = enc.Combatants.Values
            .OrderByDescending(c => c.Total.Damage)
            .Select(c => BuildDetail(enc, c, 5_000))
            .ToList();
        return new FightRecord(1, snapshot, details);
    }

    /// <summary>Null when nothing was measured, so the UI can show a dash instead of a fake zero.</summary>
    private static double? Rate(int count, int hits) => hits > 0 ? (double)count / hits : null;
}
