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

[Guid("D4E5F6A7-B8C9-0D1E-2F3A-4B5C6D7E8F90")]
[AddonProperties("Person with a Firearm", "^3DevKilo^7", "1.0")]
public class Brandishing : Callout
{
    static Random rnd = new Random();
    private List<Ped> _suspects = new();
    private bool _hasReacted = false;

    public Brandishing()
    {
        if (CalloutConfig.BrandishingConfig.FixedLocation && CalloutConfig.BrandishingConfig.Locations.Any())
            InitInfo(CalloutConfig.BrandishingConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());
            
        ShortName = "911 Report: Brandishing";
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
        
        int count = rnd.Next(CalloutConfig.BrandishingConfig.MinSuspects, CalloutConfig.BrandishingConfig.MaxSuspects);
        if (count < 1) count = 1;

        for (int i = 0; i < count; i++)
        {
            var suspect = await SpawnPed(GetRandomPedHash(), Location, rnd.Next(0, 360));
            suspect.AlwaysKeepTask = true;
            suspect.BlockPermanentEvents = true;
            var weapon = GetRandomWeaponHash();
            suspect.Weapons.Give(weapon, 255, true, true);
            suspect.Task.WanderAround();
            _suspects.Add(suspect);
            await BaseScript.Delay(100);
        }
    }

    public override async void OnStart(Ped closest)
    {
        if (_suspects.Any())
        {
            foreach(var s in _suspects)
            {
                var blip = s.AttachBlip();
                blip.Name = "Armed Subject";
                blip.Scale = 0.7f;
            }
            NotificationService.ShowNetworkedNotification("Dispatch: Caller reports subject is brandishing a weapon. Approach with caution.", "Dispatch");
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
            foreach (var suspect in _suspects)
            {
                if (suspect.IsDead && suspect.AttachedBlip is not null)
                {
                    suspect.AttachedBlip.Delete();
                }
                else if (suspect.IsCuffed && suspect.AttachedBlip is not null)
                {
                    if (suspect.AttachedBlip.Color != BlipColor.Blue)
                    {
                        suspect.AttachedBlip.Color = BlipColor.Blue;
                        suspect.AttachedBlip.IsFlashing = true;
                    }
                }
            }

            if (_suspects.All(s => !s.IsAlive || s.IsCuffed)) return false;

            if (!_hasReacted)
            {
                foreach(var s in _suspects)
                {
                    if (s.IsAlive && !s.IsCuffed && Game.PlayerPed.Position.DistanceToSquared(s.Position) < 400f) // 20 meters
                    {
                        _hasReacted = true;
                        ReactToPolice();
                        break;
                    }
                }
            }

            return true;
        });
        
        FinishCallout();
    }

    private void ReactToPolice()
    {
        NotificationService.InfoNotify("Subjects are reacting to police presence.", "Observation");
        foreach(var suspect in _suspects)
        {
            if (!suspect.IsAlive || suspect.IsCuffed) continue;

            int reaction = rnd.Next(0, 100);
            if (reaction < 40) // 40% chance to attack
            {
                 suspect.Task.FightAgainst(Game.PlayerPed);
                 NotificationService.ShowNetworkedNotification("Subject is engaging!", "Dispatch");
            }
            else if (reaction < 70) // 30% chance to flee
            {
                 suspect.Task.FleeFrom(Game.PlayerPed);
                 NotificationService.ShowNetworkedNotification("Subject is fleeing!", "Dispatch");
            }
            else // 30% chance to surrender
            {
                 suspect.Task.ClearAll();
                 suspect.Task.HandsUp(120000); // Hands up for 2 minutes
                 NotificationService.ShowNetworkedNotification("Subject is complying.", "Dispatch");
            }
        }
    }

    public override void OnCancelBefore()
    {
        foreach(var s in _suspects)
        {
            if (s is not null && s.Exists())
                s.AttachedBlip?.Delete();
        }
            
        base.OnCancelBefore();
    }
}
