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

[Guid("E5F6A7B8-C901-2345-6789-0123DEFBC234")]
[AddonProperties("Kidnapping in Progress", "^3DevKilo", "1.0")]
public class KidnappingInProgress : Callout
{
    static Random rnd = new Random();
    private Vehicle _vehicle;
    private Ped _suspect;
    private Ped _victim;
    private Prop _headbag;
    private bool _pursuitActive = false;

    public KidnappingInProgress()
    {
        if (CalloutConfig.KidnappingInProgressConfig.FixedLocation && CalloutConfig.KidnappingInProgressConfig.Locations.Any())
            InitInfo(CalloutConfig.KidnappingInProgressConfig.Locations.SelectRandom());
        else
            InitInfo(GetLocation());

        ShortName = "Kidnapping in Progress";
        CalloutDescription = "911 Report: Witness states a person was forced into a vehicle. Suspect is armed and dangerous.";
        ResponseCode = 3;
        StartDistance = CalloutConfig.KidnappingInProgressConfig.StartDistance;
        API.CancelAllPoliceReports();
        API.PlayPoliceReport("SCRIPTED_SCANNER_REPORT_FRANLIN_0_KIDNAP", 0f);
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.KidnappingInProgressConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.KidnappingInProgressConfig.MinSpawnDistance,
            CalloutConfig.KidnappingInProgressConfig.MaxSpawnDistance);
        var offsetX = rnd.Next(-1 * distance, distance);
        var offsetY = rnd.Next(-1 * distance, distance);
        return World.GetNextPositionOnStreet(Game.PlayerPed.GetOffsetPosition(new Vector3(offsetX, offsetY, 0)));
    }

    private VehicleHash GetRandomVehicleHash()
    {
        var vehicleHashes = Enum.GetValues(typeof(VehicleHash)).Cast<VehicleHash>().Where(v => API.IsModelAVehicle((uint)v)).ToArray();
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

        var hash = GetRandomVehicleHash();
        _vehicle = await SpawnVehicle(hash, Location, rnd.Next(0, 360));

        if (_vehicle is not null)
        {
            // Spawn suspect (driver)
            _suspect = await SpawnPed(PedHash.Abigail, Location, 0); // Placeholder hash
            if (_suspect is not null)
            {
                _suspect.SetIntoVehicle(_vehicle, VehicleSeat.Driver);
                // Drive normally initially to blend in
                _suspect.Task.CruiseWithVehicle(_vehicle, 20f, 786603); 
                _suspect.Weapons.Give(WeaponHash.Pistol, 100, true, true);
            }

            // Spawn victim (passenger/trunk)
            _victim = await SpawnPed(PedHash.Bevhills01AFM, Location, 0); // Placeholder hash
            if (_victim is not null)
            {
                // Put victim in back seat or trunk if possible (using back seat for simplicity and visibility)
                _victim.SetIntoVehicle(_vehicle, VehicleSeat.RightRear);
                _victim.Task.Cower(-1); // Make them look distressed

                // Add bag over head
                _headbag = await World.CreateProp(API.GetHashKey("prop_money_bag_01"), Location, new Vector3(0f, 0f, 0f), false, true);
                if (_headbag is not null)
                {
                    _headbag.IsPersistent = true;
                    API.AttachEntityToEntity(_headbag.Handle, _victim.Handle, API.GetPedBoneIndex(_victim.Handle, 12844), 0.2f, 0.04f, 0f, 0f, 270f,
                        60f, true, false, false, true, 1, true);
                    API.SetEntityCompletelyDisableCollision(_headbag.Handle, false, true);
                }
            }
        }
    }

    public override async void OnStart(Ped closest)
    {
        if (_vehicle is not null && _vehicle.Exists())
        {
            var blip = _vehicle.AttachBlip();
            blip.Name = "Suspect Vehicle";
            blip.Sprite = BlipSprite.PersonalVehicleCar;
            blip.Color = BlipColor.Red;

            NotificationService.ShowNetworkedNotification("Dispatch: Suspect vehicle located. Approach with caution, hostage reported inside.", "Dispatch");

            if (_suspect is not null)
            {
                _suspect.AlwaysKeepTask = true;
                _suspect.BlockPermanentEvents = true;
            }
            if (_victim is not null)
            {
                _victim.AlwaysKeepTask = true;
                _victim.BlockPermanentEvents = true;
            }
        }
        else
        {
            EndCallout();
            return;
        }

        base.OnStart(closest);

        // Logic loop
        await QueueService.Predicate(() =>
        {
            if (_vehicle is null || !_vehicle.Exists()) return false;
            if (_suspect is null || !_suspect.IsAlive) return false;

            // Trigger behavior if player is close
            if (!_pursuitActive && Game.PlayerPed.Position.DistanceToSquared(_vehicle.Position) < 2500.0f) // 50m
            {
                // Suspect spots police and bolts
                _pursuitActive = true;
                _suspect.Task.FleeFrom(Game.PlayerPed);
                API.SetDriverAbility(_suspect.Handle, 1.0f); // Max driving skill
                API.SetDriverAggressiveness(_suspect.Handle, 1.0f); // Aggressive
                
                // Notify player about hostage situation
                NotificationService.InfoNotify("I probably shouldn't plow into that hostage...", "Innerthought");
            }

            // Fail condition: Player rams vehicle too hard
            if (_vehicle.HasCollided)
            {
                // Check if it collided with the player's vehicle
                if (_vehicle.IsTouching(Game.PlayerPed.CurrentVehicle))
                {
                    // Simple check for collision, could be refined with speed check
                    // For now, we just warn or fail if vehicle health drops too low
                    if (_vehicle.BodyHealth < 800)
                    {
                        NotificationService.InfoNotify("I just injured that hostage...", "Innerthought");
                        // Could end callout here or just let it play out with consequences
                    }
                }
                
                // Clear collision state for next frame
                API.ClearEntityLastDamageEntity(_vehicle.Handle);
            }
            
            // End condition: Suspect arrested or dead
            if (_suspect.IsCuffed || _suspect.IsDead) return false;

            return true;
        });

        FinishCallout();
    }

    public override void OnCancelBefore()
    {
        if (_vehicle is not null)
            _vehicle.AttachedBlip?.Delete();

        if (_suspect is not null)
            _suspect.AttachedBlip?.Delete();
            
        if (_victim is not null)
            _victim.AttachedBlip?.Delete();

        if (_headbag is not null)
            _headbag.Delete();

        base.OnCancelBefore();
    }
}