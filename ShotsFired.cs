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

[Guid("075ED322-CDF9-4FA3-BAE9-A195E991A453")]
[AddonProperties("Shots Fired", "DevKilo", "1.0")]
public class ShotsFired : Callout
{
    static Random rnd = new Random();
    List<Ped> suspects = new();
    bool endedEarly = true;

    RelationshipGroup suspectGroup = new(API.GetHashKey("SUSPECTS"));

    public ShotsFired()
    {
        if (CalloutConfig.ShotsFiredConfig.FixedLocation && CalloutConfig.ShotsFiredConfig.Locations.Any())
            InitInfo(CalloutConfig.ShotsFiredConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());
        ShortName = "Shots Fired";
        CalloutDescription = "911 Report: Gunshots have been reported in the area! Respond code 3.";
        ResponseCode = 3;
        StartDistance = CalloutConfig.ShotsFiredConfig.StartDistance;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.ShotsFiredConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.ShotsFiredConfig.MinSpawnDistance,
            CalloutConfig.ShotsFiredConfig.MaxSpawnDistance);
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

    private VehicleHash GetRandomVehicleHash()
    {
        var vehicleHashes = Enum.GetValues(typeof(VehicleHash)).Cast<VehicleHash>()
            .Where(v => API.IsModelAVehicle((uint)v)).ToArray();
        VehicleHash selectedHash;
        do
        {
            selectedHash = vehicleHashes[rnd.Next(vehicleHashes.Length)];
        } while (!(new Model(selectedHash) is Model { IsCar: false, IsValid: true } model &&
                   API.GetVehicleClassFromName((uint)model.Hash) < 13));

        return selectedHash;
    }

    private WeaponHash GetRandomWeaponHash()
    {
        var weaponHashes = Enum.GetValues(typeof(WeaponHash)).Cast<WeaponHash>().ToArray();
        WeaponHash selectedHash;
        do
        {
            selectedHash = weaponHashes[rnd.Next(weaponHashes.Length)];
        } while (API.GetWeapontypeGroup((uint)selectedHash) != (uint)API.GetHashKey("GROUP_PISTOL"));

        return selectedHash;
    }

    public override async Task OnAccept()
    {
        InitBlip();
        int suspectsNumber = rnd.Next(CalloutConfig.ShotsFiredConfig.MinSuspects,
            CalloutConfig.ShotsFiredConfig.MaxSuspects);
        for (int i = 0; i <= suspectsNumber; i++)
        {
            Ped suspect = await SpawnPed(GetRandomPedHash(), Location, rnd.Next(0, 360));
            suspects.Add(suspect);
            suspect.RelationshipGroup = suspectGroup;
            suspect.Weapons.Give(GetRandomWeaponHash(), 255, true, true);
            suspect.Task.WanderAround();
            await BaseScript.Delay(100);
        }

        suspectGroup.SetRelationshipBetweenGroups(Game.PlayerPed.RelationshipGroup, Relationship.Hate, true);
    }

    public override async void OnStart(Ped closest)
    {
        suspects.ForEach((suspect) =>
        {
            suspect.AttachBlip();
            suspect.AlwaysKeepTask = true;
            suspect.BlockPermanentEvents = true;
            suspect.Task.FightAgainstHatedTargets(StartDistance, -1);
        });
        base.OnStart(closest);
        await QueueService.Predicate(() => !suspects.Any(s => s.IsAlive));
        FinishCallout();
    }

    public override void OnCancelBefore()
    {
        foreach (Ped suspect in suspects.ToArray())
        {
            if (suspect is null) continue;
            suspect?.AttachedBlip?.Delete();
        }
    }
}