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

[Guid("A1B2C3D4-E5F6-7890-1234-567890ABCDEF")]
[AddonProperties("Rabid Animal", "^3DevKilo", "1.0")]
public class RabidAnimal : Callout
{
    static Random rnd = new Random();
    private List<Ped> _animals = new();

    public RabidAnimal()
    {
        if (CalloutConfig.RabidAnimalConfig.FixedLocation && CalloutConfig.RabidAnimalConfig.Locations.Any())
            InitInfo(CalloutConfig.RabidAnimalConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());
            
        ShortName = "Rabid Animal";
        CalloutDescription = "911 Report: A wild animal is acting aggressively. Respond with caution.";
        ResponseCode = 3;
        StartDistance = CalloutConfig.RabidAnimalConfig.StartDistance;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.RabidAnimalConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.RabidAnimalConfig.MinSpawnDistance,
            CalloutConfig.RabidAnimalConfig.MaxSpawnDistance);
        var offsetX = rnd.Next(-1 * distance, distance);
        var offsetY = rnd.Next(-1 * distance, distance);
        return World.GetNextPositionOnStreet(Game.PlayerPed.GetOffsetPosition(new Vector3(offsetX, offsetY, 0)));
    }

    private PedHash GetRandomAnimalHash()
    {
        var animals = new[] { 
            "a_c_coyote", 
            "a_c_mtlion", 
            "a_c_boar", 
            "a_c_rottweiler",
            "a_c_deer"
        };
        var selected = animals[rnd.Next(animals.Length)];
        return (PedHash)API.GetHashKey(selected);
    }

    public override async Task OnAccept()
    {
        InitBlip();
        
        int count = rnd.Next(CalloutConfig.RabidAnimalConfig.MinSuspects, CalloutConfig.RabidAnimalConfig.MaxSuspects);
        if (count < 1) count = 1;

        for (int i = 0; i < count; i++)
        {
            var hash = GetRandomAnimalHash();
            var animal = await SpawnPed(hash, Location, rnd.Next(0, 360));
            
            if (animal is not null)
            {
                API.SetPedCombatAttributes(animal.Handle, 46, true); 
                API.SetPedFleeAttributes(animal.Handle, 0, false);
                API.SetPedCombatRange(animal.Handle, 2); 
                animal.Task.WanderAround();
                _animals.Add(animal);
                await BaseScript.Delay(100);
            }
        }
    }

    public override async void OnStart(Ped closest)
    {
        if (_animals.Any())
        {
            foreach(var animal in _animals)
            {
                var blip = animal.AttachBlip();
                blip.Name = "Rabid Animal";
                blip.Sprite = BlipSprite.Enemy; 
                animal.AlwaysKeepTask = true;
                animal.BlockPermanentEvents = true;
                animal.Task.FightAgainst(Game.PlayerPed);
            }
            NotificationService.ShowNetworkedNotification("Dispatch: Animal control is unavailable. Handle the aggressive animal.", "Dispatch");
        }
        else
        {
            EndCallout();
            return;
        }

        base.OnStart(closest);

        await QueueService.Predicate(() => 
        {
            if (_animals.All(a => !a.IsAlive)) return false;
            return true;
        });

        FinishCallout();
    }

    public override void OnCancelBefore()
    {
        foreach(var animal in _animals)
        {
            if (animal is not null && animal.Exists())
                animal.AttachedBlip?.Delete();
        }
            
        base.OnCancelBefore();
    }
}
