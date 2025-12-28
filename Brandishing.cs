using System;
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

[Guid("D4E5F6A7-B8C9-0D1E-2F3A-4B5C6D7E8F90")]
[AddonProperties("Person with a Firearm", "DevKilo", "1.0")]
public class Brandishing : Callout
{
    static Random rnd = new Random();
    private Ped _suspect;
    private Blip _blip;
    private bool _hasReacted = false;

    public Brandishing()
    {
        InitInfo(GetLocation());
        ShortName = "Person with a Firearm";
        CalloutDescription = "911 Report: A subject is reported to be walking in public with a visible firearm.";
        ResponseCode = 3;
        StartDistance = CalloutConfig.BrandishingConfig.StartDistance;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.BrandishingConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.BrandishingConfig.MinSpawnDistance,
            CalloutConfig.BrandishingConfig.MaxSpawnDistance);
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
        
        _suspect = await SpawnPed(GetRandomPedHash(), Location, rnd.Next(0, 360));
        _suspect.AlwaysKeepTask = true;
        _suspect.BlockPermanentEvents = true;
        var weapon = GetRandomWeaponHash();
        _suspect.Weapons.Give(weapon, 255, true, true);
        _suspect.Task.WanderAround();
    }

    public override async void OnStart(Ped closest)
    {
        if (_suspect is not null && _suspect.Exists())
        {
            _blip = _suspect.AttachBlip();
            _blip.Name = "Armed Subject";
        }
        else
        {
            EndCallout();
            return;
        }

        base.OnStart(closest);

        // Logic loop for reaction
        // Predicate returns true to continue waiting, false to stop waiting
        await QueueService.Predicate(() => 
        {
            if (!_suspect.IsAlive || _suspect.IsCuffed) return false;

            if (!_hasReacted && Game.PlayerPed.Position.DistanceToSquared(_suspect.Position) < 400f) // 20 meters
            {
                _hasReacted = true;
                ReactToPolice();
            }

            return true;
        });
        
        FinishCallout();
    }

    private void ReactToPolice()
    {
        int reaction = rnd.Next(0, 100);
        if (reaction < 40) // 40% chance to attack
        {
             NotificationService.InfoNotify("Suspect is engaging!", "Observation");
             _suspect.Task.FightAgainst(Game.PlayerPed);
        }
        else if (reaction < 70) // 30% chance to flee
        {
             NotificationService.InfoNotify("Suspect is fleeing!", "Observation");
             _suspect.Task.FleeFrom(Game.PlayerPed);
        }
        else // 30% chance to surrender
        {
             NotificationService.InfoNotify("Suspect is complying.", "Observation");
             _suspect.Task.ClearAll();
             _suspect.Task.HandsUp(120000); // Hands up for 2 minutes
        }
    }

    public override void OnCancelBefore()
    {
        if (_blip != null && _blip.Exists())
            _blip.Delete();
            
        base.OnCancelBefore();
    }
}
