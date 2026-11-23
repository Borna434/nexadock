using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>Default speaker volume/mute and microphone mute through the documented Core Audio API.</summary>
public sealed class CoreAudioService : IAudioService
{
    private const int ERender = 0;
    private const int ECapture = 1;
    private const int EMultimedia = 1;
    private const int ECommunications = 2;
    private const int ClsctxAll = 23;
    private static Guid _endpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static PROPERTYKEY _friendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
    private static Guid _context = Guid.Empty;

    private readonly ILogger<CoreAudioService> _logger;

    public CoreAudioService(ILogger<CoreAudioService> logger) => _logger = logger;

    public AudioEndpointState GetSpeaker() => Read(ERender, EMultimedia);

    public AudioEndpointState GetMicrophone() => Read(ECapture, ECommunications);

    public void SetSpeakerVolume(double volume01) =>
        WithVolume(ERender, EMultimedia, v => v.SetMasterVolumeLevelScalar((float)Math.Clamp(volume01, 0, 1), ref _context));

    public void SetSpeakerMuted(bool muted) => WithVolume(ERender, EMultimedia, v => v.SetMute(muted, ref _context));

    public void SetMicrophoneMuted(bool muted) => WithVolume(ECapture, ECommunications, v => v.SetMute(muted, ref _context));

    private AudioEndpointState Read(int flow, int role)
    {
        AudioEndpointState state = new(false, string.Empty, 0, false);
        WithDevice(flow, role, (device, volume) =>
        {
            volume.GetMasterVolumeLevelScalar(out var level);
            volume.GetMute(out var muted);
            state = new AudioEndpointState(true, FriendlyName(device), level, muted);
            return 0;
        });
        return state;
    }

    private void WithVolume(int flow, int role, Func<IAudioEndpointVolume, int> action) =>
        WithDevice(flow, role, (_, volume) => action(volume));

    private void WithDevice(int flow, int role, Func<IMMDevice, IAudioEndpointVolume, int> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? volumeObject = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(flow, role, out device) != 0 || device is null)
            {
                return; // no such device (e.g. no microphone)
            }

            if (device.Activate(ref _endpointVolumeIid, ClsctxAll, IntPtr.Zero, out volumeObject) != 0 || volumeObject is not IAudioEndpointVolume volume)
            {
                return;
            }

            action(device, volume);
        }
        catch (COMException ex)
        {
            _logger.LogDebug(ex, "Core Audio call failed.");
        }
        finally
        {
            Release(volumeObject);
            Release(device);
            Release(enumerator);
        }
    }

    private static string FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0, out var store) != 0 || store is null)
        {
            return string.Empty;
        }

        try
        {
            if (store.GetValue(ref _friendlyName, out var pv) != 0)
            {
                return string.Empty;
            }

            try
            {
                return pv.vt == VT_LPWSTR ? Marshal.PtrToStringUni(pv.pointerValue) ?? string.Empty : string.Empty;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object anInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
