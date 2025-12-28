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

[Guid("CF0C7C6C-4501-4878-926E-856A4DD19DE3")]
[AddonProperties("Public Intoxication", "DevKilo", "1.0")]
public class PublicIntoxication : Callout
{
    static Random rnd = new Random();
    private Ped _suspect;
    private Blip _blip;

    public PublicIntoxication()
    {
        InitInfo(GetLocation());
        ShortName = "Public Intoxication";
        CalloutDescription = "911 Report: Reports of an individual causing a disturbance due to public intoxication. Respond and assess the situation.";
        ResponseCode = 2;
        StartDistance = CalloutConfig.PublicIntoxicationConfig.StartDistance;
    }

    public override Task<bool> CheckRequirements() => Task.FromResult(CalloutConfig.PublicIntoxicationConfig.Enabled);

    private static Vector3 GetLocation()
    {
        var distance = rnd.Next(CalloutConfig.PublicIntoxicationConfig.MinSpawnDistance,
            CalloutConfig.PublicIntoxicationConfig.MaxSpawnDistance);
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
        
        _suspect = await SpawnPed(GetRandomPedHash(), Location, rnd.Next(0, 360));
        
        // Apply drunk movement clipset
        API.RequestClipSet("move_m@drunk@verydrunk");
        await QueueService.Predicate(() => !API.HasClipSetLoaded("move_m@drunk@verydrunk"));
        API.SetPedMovementClipset(_suspect.Handle, "move_m@drunk@verydrunk", 1.0f);
        
        _suspect.Task.WanderAround();
    }

    public override async void OnStart(Ped closest)
    {
        if (_suspect != null && _suspect.Exists())
        {
            _blip = _suspect.AttachBlip();
            _blip.Name = "Intoxicated Subject";
            
            _suspect.AlwaysKeepTask = true;
            _suspect.BlockPermanentEvents = true;
            
            // Occasionally play a drunk idle animation or shout
            _ = Task.Run(async () =>
            {
                while (_suspect.IsAlive && !_suspect.IsCuffed)
                {
                    await BaseScript.Delay(rnd.Next(5000, 15000));
                    if (_suspect.IsWalking)
                    {
                         // Stumble
                         _suspect.Task.PlayAnimation("move_m@drunk@verydrunk", "idle", 8.0f, -8.0f, 2000, AnimationFlags.None, 0f);
                    }
                }
            });
        }
        else
        {
            EndCallout();
            return;
        }

        base.OnStart(closest);

        // Predicate returns true to continue waiting, false to stop waiting
        await QueueService.Predicate(() => 
        {
            if (!_suspect.IsAlive || _suspect.IsCuffed) return false;
            return true;
        });

        FinishCallout();
    }

    public override void OnCancelBefore()
    {
        if (_blip != null && _blip.Exists())
            _blip.Delete();
            
        base.OnCancelBefore();
    }
}
