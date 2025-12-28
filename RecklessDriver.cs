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

[Guid("07BF85A3-31CB-40F7-A216-390030273903")]
[AddonProperties("Reckless Driver", "^3DevKilo", "1.0.0")]
public class RecklessDriver : Callout
{
    static Random rnd = new Random();
    private Vehicle _suspectVehicle;
    private Ped _suspectDriver;
    private Blip _blip;

    public RecklessDriver()
    {
        if (CalloutConfig.RecklessDriverConfig.FixedLocation && CalloutConfig.RecklessDriverConfig.Locations.Any())
            InitInfo(CalloutConfig.RecklessDriverConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());
            
        ShortName = "Reckless Driver";
        CalloutDescription = "911 Report: A vehicle is driving erratically and endangering public safety. Intercept and investigate.";
        StartDistance = CalloutConfig.RecklessDriverConfig.StartDistance;
        ResponseCode = 2;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.RecklessDriverConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.RecklessDriverConfig.MinSpawnDistance,
            CalloutConfig.RecklessDriverConfig.MaxSpawnDistance);
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

    public override async Task OnAccept()
    {
        InitBlip();
        
        Vector3 outPosition = default;
        float outHeading = default;
        API.GetClosestVehicleNodeWithHeading(Location.X, Location.Y, Location.Z, ref outPosition, ref outHeading, 1, 3.0f, 0);

        _suspectVehicle = await SpawnVehicle(GetRandomVehicleHash(), outPosition, outHeading);
        _suspectDriver = await SpawnPed(GetRandomPedHash(), outPosition, outHeading);
        
        _suspectDriver.SetIntoVehicle(_suspectVehicle, VehicleSeat.Driver);
        
        // Add passengers if configured
        int totalSuspects = rnd.Next(CalloutConfig.RecklessDriverConfig.MinSuspects, CalloutConfig.RecklessDriverConfig.MaxSuspects + 1);
        int passengers = totalSuspects - 1;
        
        if (passengers > 0)
        {
             for (int i = 0; i < passengers; i++)
             {
                 if (API.IsVehicleSeatFree(_suspectVehicle.Handle, i))
                 {
                     var passenger = await SpawnPed(GetRandomPedHash(), outPosition, outHeading);
                     passenger.SetIntoVehicle(_suspectVehicle, (VehicleSeat)i);
                     passenger.BlockPermanentEvents = true;
                     passenger.AlwaysKeepTask = true;
                 }
             }
        }
        
        // Initial wander task so they are moving when player arrives
        _suspectDriver.Task.CruiseWithVehicle(_suspectVehicle, 20f, 786603); // Rushed driving style
    }

    public override async void OnStart(Ped closest)
    {
        if (_suspectVehicle is not null && _suspectVehicle.Exists())
        {
            _blip = _suspectVehicle.AttachBlip();
            _blip.Name = "Reckless Vehicle";
            
            // Make them drive aggressively
            _suspectDriver.Task.CruiseWithVehicle(_suspectVehicle, 60f, 786491); // AvoidTrafficExtremely | IgnoreLights
            
            _suspectDriver.AlwaysKeepTask = true;
            _suspectDriver.BlockPermanentEvents = true;
            
            NotificationService.ShowNetworkedNotification("Dispatch: Vehicle is driving erratically. Intercept immediately.", "Dispatch");
        }
        else
        {
            EndCallout();
            return;
        }

        base.OnStart(closest);

        // Wait until the vehicle stops or the driver is arrested/dead
        // Predicate returns true to continue waiting, false to stop waiting
        await QueueService.Predicate(() => 
        {
            if (!_suspectDriver.IsAlive || _suspectDriver.IsCuffed) return false;
            
            if (_suspectVehicle.Speed < 1.0f && Game.PlayerPed.Position.DistanceToSquared(_suspectVehicle.Position) < 100f && _suspectVehicle.State.Get("StoppedState") is true)
                return false;
            
            return true;
        }, 1000);
        
        if (_suspectDriver is { IsAlive: true, IsCuffed: false })
        {
             NotificationService.ShowNetworkedNotification("The vehicle has come to a stop. Proceed with caution.", "Dispatch");
        }
        
        FinishCallout();
    }

    public override void OnCancelBefore()
    {
        if (_blip is not null && _blip.Exists())
            _blip.Delete();
            
        base.OnCancelBefore();
    }
}
