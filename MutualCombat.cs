using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using FiveFR._API.Attributes;
using FiveFR._API.Classes;
using FiveFR._API.Extensions;
using FiveFR._API.Services;

namespace GreatCallouts_FiveFR;

[Guid("8A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D")]
[AddonProperties("Mutual Combat", "^3DevKilo", "1.0")]
public class MutualCombat : Callout
{
    static Random rnd = new Random();
    List<Ped> suspects = new();

    private RelationshipGroup fighterGroup1;
    private RelationshipGroup fighterGroup2;

    public MutualCombat()
    {
        fighterGroup1 = new(API.GetHashKey("FIGHTER_ONE"));
        fighterGroup2 = new(API.GetHashKey("FIGHTER_TWO"));
        
        if (CalloutConfig.MutualCombatConfig.FixedLocation && CalloutConfig.MutualCombatConfig.Locations.Any())
            InitInfo(CalloutConfig.MutualCombatConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());
            
        ShortName = "911 Report: Mutual Combat";
        CalloutDescription = "911 Report: A physical altercation has broken out between two individuals.";
        ResponseCode = 2;
        StartDistance = CalloutConfig.MutualCombatConfig.StartDistance;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.MutualCombatConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.MutualCombatConfig.MinSpawnDistance,
            CalloutConfig.MutualCombatConfig.MaxSpawnDistance);
        var offsetX = rnd.Next(-1 * distance, distance);
        var offsetY = rnd.Next(-1 * distance, distance);
        return World.GetNextPositionOnStreet(Game.PlayerPed.GetOffsetPosition(new Vector3(offsetX, offsetY, 0)));
    }

    private PedHash GetRandomPedHash()
    {
        var pedHashes = Enum.GetValues(typeof(PedHash)).Cast<PedHash>().Where(p => API.IsModelAPed((uint)p)).ToArray();
        PedHash selectedHash;
        var mathPed = -1;
        do
        {
            selectedHash = pedHashes[rnd.Next(pedHashes.Length)];
            if (mathPed > -1)
            {
                API.DeleteEntity(ref mathPed);
                mathPed = -1;
            }

            mathPed = API.CreatePed(0, (uint)selectedHash, Location.X, Location.Y, Location.Z, 0f, false, true);
        } while (!API.IsPedHuman(mathPed));

        if (mathPed > -1)
            API.DeleteEntity(ref mathPed);

        return selectedHash;
    }

    public override async Task OnAccept()
    {
        InitBlip();
        
        int suspectsNumber = rnd.Next(CalloutConfig.MutualCombatConfig.MinSuspects, CalloutConfig.MutualCombatConfig.MaxSuspects);
        if (suspectsNumber < 2) suspectsNumber = 2;

        for (int i = 0; i < suspectsNumber; i++)
        {
            Ped suspect = await SpawnPed(GetRandomPedHash(), Location, rnd.Next(0, 360));
            suspects.Add(suspect);
            
            if (i % 2 == 0)
                suspect.RelationshipGroup = fighterGroup1;
            else
                suspect.RelationshipGroup = fighterGroup2;
                
            suspect.Task.WanderAround();
            suspect.AlwaysKeepTask = true;
            suspect.BlockPermanentEvents = true;
            
            await BaseScript.Delay(100);
        }

        // Set hostility
        fighterGroup1.SetRelationshipBetweenGroups(fighterGroup2, Relationship.Hate, true);
        fighterGroup2.SetRelationshipBetweenGroups(fighterGroup1, Relationship.Hate, true);
    }

    public override void OnStart(Ped closest)
    {
        foreach (var suspect in suspects)
        {
            suspect.AttachBlip();
            suspect.Task.FightAgainstHatedTargets(200f, -1);
        }

        _ = QueueService.Predicate(() => !suspects.All(s => !s.IsAlive || s.IsCuffed)).ContinueWith(_ => FinishCallout()); // Finish callout when complete.
        base.OnStart(closest);
    }

    public override void OnCancelBefore()
    {
        foreach (Ped suspect in suspects.ToArray())
        {
            if (suspect is null) continue;
            suspect?.AttachedBlip?.Delete();
        }
        fighterGroup1.Remove();
        fighterGroup2.Remove();
    }
}
