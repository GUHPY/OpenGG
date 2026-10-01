using OpenGG;
using OneRGB.Application.Control;
using OneRGB.Application.Devices.Keyboard;
using OneRGB.Application.Devices.Input;
using OneRGB.Domain;
using OneRGB.Windows;
using System.Buffers.Binary;

int checks = 0;
void Check(bool value,string name) { if (!value) { throw new Exception(name); } checks++; }
var first = Guid.NewGuid(); var second = Guid.NewGuid();
HidInterfaceInfo Interface(ushort vendor,ushort product,ushort page,ushort usage,Guid container,int mi = 0,ushort feature = 0) =>
    new("test",new UsbIdentity(vendor,product,mi,page,usage),0,"Test keyboard","SteelSeries",65,65,feature,null) { ContainerId = container };
var devices = SteelSeriesKeyboards.Discover([
    Interface(0x1038,0x1644,1,6,first), Interface(0x1038,0x1644,0xFFC0,1,first,3,642),
    Interface(0x1038,0x1644,1,6,second), Interface(0x1038,0x1644,0xFFC0,1,second,3,641),
    Interface(0x1038,0x1830,1,2,Guid.NewGuid()), Interface(0x046D,0xC341,1,6,Guid.NewGuid()),
    Interface(0x1038,0x1830,1,6,Guid.NewGuid()) with { Product = "SteelSeries Rival Mouse" },
    Interface(0x1038,0x9999,0xFFC0,1,Guid.NewGuid(),3,642)]);
Check(devices.Count == 2,"Only keyboard collections from SteelSeries are accepted; identical models retain separate containers.");
Check(devices.Count(d => d.HasVerifiedReceiver) == 1,"Advanced controls require the exact report sizes.");
Check(!(devices.Single(d => d.HasVerifiedReceiver) with { Interfaces = [] }).HasVerifiedReceiver,"Remembered devices cannot grant hardware write support while disconnected.");
Check(OneRGB.Hardware.Lighting.ApexLighting.Find([Interface(0x1038,0x1646,0xFFC0,1,Guid.NewGuid(),3,642)]) is null,"An unvalidated cable interface must not steal the shared receiver link.");
using (var link = new OneRGB.Hardware.Native.ApexHidLink(() => []))
{
    var faults = 0; link.Faulted += () => faults++;
    link.ClearTemporary();
    Check(faults == 0,"Normal RGB release must preserve the loaded live state.");
    link.Drop();
    Check(faults == 1,"Explicit ownership drop must invalidate the loaded live state.");
    try { await link.ReadProfileAsync(2,CancellationToken.None); throw new Exception("Missing device accepted."); }
    catch(InvalidOperationException) { Check(faults == 2,"A failed shared-channel operation must invalidate the loaded live state."); }
}
var state = new ApexLive.State(2,.3);
var descriptor = AnalogKeyboardDescriptor.ApexProTklGen3;
var changes = descriptor.With(KeyCapabilities.ActuationAdjustable).ToDictionary(k => KeyboardSettings.Actuation(k.Code),_ => (ControlValue)new NumberValue(1.5));
var original = state.Clone();
var packets = ApexLive.ApplyBatch(state,changes);
Check(changes.Count == 60 && packets.Length == 1 && packets[0][1] == 0x6F && packets[0][3] == 68,"60 visible analog keys must produce one complete 68-key actuation report.");
var selected = descriptor.With(KeyCapabilities.ActuationAdjustable).Select(k => ApexProtocol.LedId(k.Code)!.Value).ToHashSet();
foreach (var usage in ApexProtocol.AnalogKeys) { Check(state.Actuations[usage] == (selected.Contains(usage) ? ((byte)28,(byte)31) : original.Actuations[usage]),"Non-selected keys must be preserved."); }
Check(original.Actuations.Values.All(p => p == ((byte)45,(byte)49)),"A staging clone must not mutate its source.");
var rt = ApexLive.ApplyBatch(state,[new(KeyboardSettings.RapidTriggerEnabled,new ToggleValue(true)),new(KeyboardSettings.Sensitivity("KeyW"),new NumberValue(.5))]);
Check(rt.Length == 2 && rt.Select(r => r[1]).SequenceEqual(new byte[] { 0x76,0x77 }),"RT changes are consolidated and ordered.");
var blob = new byte[ApexProfile.Length];
BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(4),19); blob.AsSpan(ApexProfile.BodyEnd).Fill(0xFF);
blob[11278]=1; blob[11279]=11; blob.AsSpan(11293,8).Fill(0xFF);
uint crc=uint.MaxValue;
foreach(var value in blob.AsSpan(8,ApexProfile.BodyEnd-8)) { crc^=value; for(int bit=0;bit<8;bit++) { crc=(crc>>1) ^ ((crc&1)==1 ? 0xEDB88320u : 0); } }
BinaryPrimitives.WriteUInt32LittleEndian(blob,~crc);
ApexProfile.Validate(blob); ApexProfile.SeedLive(blob,state);
Check(state.Actuations.Values.All(p => p == ((byte)45,(byte)49)),"Global mode overrides stale per-key threshold bytes.");
var baseline = ApexProfile.ReadConfig(blob);
var draft = baseline.Clone();
draft.ProtectionSensitivities["KeyW"] = 37;
draft.Protection = draft.Protection with { Enabled = true, Keys = ["KeyW"] };
var patched = ApexProfile.Patch(blob,baseline,draft);
var reread = ApexProfile.ReadConfig(patched);
Check(patched[12156+16] == 37 && reread.ProtectionSensitivities["KeyW"] == 37 && reread.Protection.Keys.SequenceEqual(new[] { "KeyW" }),"Protection uses the physical per-key byte and selection mask, with a valid CRC.");
Check(blob.AsSpan(8,12138-8).SequenceEqual(patched.AsSpan(8,12138-8)) && blob.AsSpan(12226).SequenceEqual(patched.AsSpan(12226)),"Protection editing preserves actuation, OLED, mappings, macros and unknown trailing bytes.");
var roundtrip = KeyboardConfig.From(descriptor,k => draft.ToValues().GetValueOrDefault(k));
Check(roundtrip.ProtectionSensitivities["KeyW"] == 37,"Raw Protection sensitivity survives local profile serialization.");
draft.ProtectionSensitivities["KeyW"] = 37.5;
try { ApexProfile.Patch(blob,baseline,draft); throw new Exception("Fractional native sensitivity accepted."); } catch(InvalidOperationException) { checks++; }
blob[500]^=1;
try { ApexProfile.Validate(blob); throw new Exception("Damaged profile accepted."); } catch(InvalidDataException) { checks++; }
Console.WriteLine($"OpenGG: {checks} offline checks passed. No HID handle opened.");

if (args.Contains("--audio"))
{
    var audio = new OneRGB.Application.Lighting.Spatial.EffectContext();
    using var capture = new OneRGB.Hardware.Audio.LoopbackCapture(audio);
    capture.Start(); await Task.Delay(600);
    if (capture.Error is { } error) { throw new Exception(error); }
    Console.WriteLine($"WASAPI loopback initialized; level {audio.AudioLevel:F3}. No recording saved.");
}

if (args.Contains("--hardware"))
{
    foreach(var name in new[] { "SteelSeriesEngine","SteelSeriesPrism","OneRGB","OpenGG","OpenRGB","SignalRgb" })
    {
        var owners = System.Diagnostics.Process.GetProcessesByName(name);
        var present = owners.Length != 0; foreach(var owner in owners) { owner.Dispose(); }
        if(present) { throw new InvalidOperationException($"Close {name} before the external hardware check."); }
    }
    var interfaces = HidEnumerator.Enumerate("vid_1038");
    if (SteelSeriesKeyboards.Discover(interfaces).Count(k => k.HasVerifiedReceiver) != 1) { throw new InvalidOperationException("Exactly one verified receiver required."); }
    using var link = new OneRGB.Hardware.Native.ApexHidLink(() => interfaces);
    var before = await link.ReadProfileAsync(2,CancellationToken.None);
    await link.LoadProfileAsync(2,CancellationToken.None);
    var live = new ApexLive.State(2,.3); ApexProfile.SeedLive(before,live);
    using var stop = new CancellationTokenSource();
    var ids = OneRGB.Application.Lighting.Spatial.LedMaps.Keyboard.Leds.Select(l => ApexProtocol.LedId(l.Id)).ToArray();
    var colors = ids.Select(_ => new OneRGB.Application.Lighting.Rgb(20,0,20)).ToArray();
    var rgb = Task.Run(async () => {
        try { while(true) { await link.SendFeatureAsync(ApexProtocol.Frame(0x61,ids,colors),stop.Token); await Task.Delay(33,stop.Token); } }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
    });
    try
    {
        await Task.Delay(150);
        var bulk = changes.ToDictionary(p => p.Key,_ => (ControlValue)new NumberValue(2));
        var reports = ApexLive.ApplyBatch(live,bulk);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await link.SendFeaturesAsync(reports,CancellationToken.None); timer.Stop();
        var after = await link.ReadProfileAsync(2,CancellationToken.None);
        if(!before.AsSpan().SequenceEqual(after)) { throw new Exception("Live write changed stored profile."); }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { selectedKeys=bulk.Count, reportKeys=68, reports=reports.Length,
            opcode="0x6F", targetMm=2, continuousRgb=true, elapsedSeconds=timer.Elapsed.TotalSeconds,
            unchangedStoredProfile=true, sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)), restore="Slot 2 reloaded in finally" }));
    }
    finally { stop.Cancel(); await rgb; link.ClearTemporary(); await link.LoadProfileAsync(2,CancellationToken.None); }
}
