using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    public double? GetAppVolume(string appHint)
    {
        double? result = null;
        WithAppSession(appHint, v =>
        {
            v.GetMasterVolume(out var level);
            result = level;
        });
        return result;
    }

    public void SetAppVolume(string appHint, double volume01) =>
        WithAppSession(appHint, v => v.SetMasterVolume((float)Math.Clamp(volume01, 0, 1), ref _context));

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var result = new List<AudioDeviceInfo>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out var current) == 0 && current is not null)
            {
                current.GetId(out defaultId);
                Release(current);
            }

            if (enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out collection) != 0 || collection is null)
            {
                return result;
            }

            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0 || device is null)
                {
                    continue;
                }

                try
                {
                    if (device.GetId(out var id) == 0 && !string.IsNullOrEmpty(id))
                    {
                        result.Add(new AudioDeviceInfo(id, FriendlyName(device), string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                    }
                }
                finally
                {
                    Release(device);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _logger.LogDebug(ex, "Listing audio outputs failed.");
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }

        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public bool SetDefaultOutput(string deviceId)
    {
        object? policy = null;
        try
        {
            // IPolicyConfig is not in the SDK, but it is what the Windows sound flyout itself uses.
            policy = new PolicyConfigComObject();
            var config = (IPolicyConfig)policy;
            var ok = true;
            foreach (var role in new[] { 0, EMultimedia, ECommunications })
            {
                ok &= config.SetDefaultEndpoint(deviceId, role) == 0;
            }

            return ok;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _logger.LogWarning(ex, "Switching the audio output failed.");
            return false;
        }
        finally
        {
            Release(policy);
        }
    }

    public IReadOnlyList<AudioAppSession> GetAppSessions()
    {
        var rows = new Dictionary<string, AudioAppSession>(StringComparer.Ordinal);
        ForEachSession((pid, state, volume) =>
        {
            volume.GetMasterVolume(out var level);
            volume.GetMute(out var muted);
            var playing = state == 1;
            AudioAppSession row;
            if (pid == 0)
            {
                row = new AudioAppSession(AudioAppSession.SystemSoundsKey, string.Empty, null, level, muted, playing);
            }
            else if (Describe(pid) is { } app)
            {
                row = new AudioAppSession(app.Key, app.Name, app.Path, level, muted, playing);
            }
            else
            {
                return;
            }

            // One row per app: browsers play from several processes.
            if (!rows.TryGetValue(row.Key, out var existing))
            {
                rows[row.Key] = row;
            }
            else if (row.IsPlaying && !existing.IsPlaying)
            {
                rows[row.Key] = existing with { IsPlaying = true, Volume = row.Volume, Muted = row.Muted };
            }
        });

        return rows.Values
            .OrderBy(r => r.IsSystemSounds ? 1 : 0)
            .ThenByDescending(r => r.IsPlaying)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SetSessionVolume(string key, double volume01) =>
        ForEachSession((pid, _, volume) =>
        {
            if (SessionKey(pid) == key)
            {
                volume.SetMasterVolume((float)Math.Clamp(volume01, 0, 1), ref _context);
            }
        });

    public void SetSessionMuted(string key, bool muted) =>
        ForEachSession((pid, _, volume) =>
        {
            if (SessionKey(pid) == key)
            {
                volume.SetMute(muted, ref _context);
            }
        });

    private static string? SessionKey(uint pid) => pid == 0 ? AudioAppSession.SystemSoundsKey : Describe(pid)?.Key;

    private static (string Key, string Name, string? Path)? Describe(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var key = process.ProcessName.ToLowerInvariant();
            string? path = null;
            var name = process.ProcessName;
            try
            {
                path = process.MainModule?.FileName;
                if (path is not null && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                {
                    name = description;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // an elevated or protected process: keep the process name
            }

            return (key, name, path);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null; // the process has just ended
        }
    }

    private void ForEachSession(Action<uint, int, ISimpleAudioVolume> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? managerObject = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out device) != 0 || device is null
                || device.Activate(ref _sessionManagerIid, ClsctxAll, IntPtr.Zero, out managerObject) != 0
                || managerObject is not IAudioSessionManager2 manager
                || manager.GetSessionEnumerator(out sessions) != 0 || sessions is null)
            {
                return;
            }

            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var control) != 0 || control is null)
                {
                    continue;
                }

                try
                {
                    if (control is IAudioSessionControl2 control2 && control2.GetProcessId(out var pid) == 0
                        && control2.GetState(out var state) == 0 && state != 2 // 2 = expired
                        && control is ISimpleAudioVolume volume)
                    {
                        action(pid, state, volume);
                    }
                }
                finally
                {
                    Release(control);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _logger.LogDebug(ex, "Audio session call failed.");
        }
        finally
        {
            Release(sessions);
            Release(managerObject);
            Release(device);
            Release(enumerator);
        }
    }

    /// <summary>Finds the audio sessions of the app on the default speaker (all of its processes).</summary>
    private void WithAppSession(string appHint, Action<ISimpleAudioVolume> action)
    {
        var hint = NormalizeHint(appHint);
        if (hint.Length < 2)
        {
            return;
        }

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? managerObject = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out device) != 0 || device is null
                || device.Activate(ref _sessionManagerIid, ClsctxAll, IntPtr.Zero, out managerObject) != 0
                || managerObject is not IAudioSessionManager2 manager
                || manager.GetSessionEnumerator(out sessions) != 0 || sessions is null)
            {
                return;
            }

            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var control) != 0 || control is null)
                {
                    continue;
                }

                try
                {
                    if (control is IAudioSessionControl2 control2 && control2.GetProcessId(out var pid) == 0 && pid != 0
                        && ProcessMatches(pid, hint) && control is ISimpleAudioVolume volume)
                    {
                        action(volume);
                    }
                }
                finally
                {
                    Release(control);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _logger.LogDebug(ex, "Audio session call failed.");
        }
        finally
        {
            Release(sessions);
            Release(managerObject);
            Release(device);
            Release(enumerator);
        }
    }

    /// <summary>"Spotify.exe", "SpotifyAB.SpotifyMusic_zpdn…!Spotify", "Chrome" → "spotify", "chrome".</summary>
    private static string NormalizeHint(string hint)
    {
        var name = hint.Split('!')[^1].Split('_')[0];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        var dot = name.LastIndexOf('.');
        return (dot >= 0 ? name[(dot + 1)..] : name).ToLowerInvariant();
    }

    private static bool ProcessMatches(uint pid, string hint)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = process.ProcessName.ToLowerInvariant();
            return name == hint || name.Contains(hint, StringComparison.Ordinal) || hint.Contains(name, StringComparison.Ordinal) && name.Length >= 4;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static Guid _sessionManagerIid = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport]
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(IntPtr grouping, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private const int DeviceStateActive = 1;

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigComObject
    {
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr deviceId, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr deviceId);
        [PreserveSig] int SetDeviceFormat(IntPtr deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(IntPtr deviceId, int defaultPeriod, IntPtr defaultTime, IntPtr minimumTime);
        [PreserveSig] int SetProcessingPeriod(IntPtr deviceId, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr deviceId, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr deviceId, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr deviceId, int visible);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

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
            NexaDock.Windows.Interop.ComRelease.Release(store);
        }
    }

    private static void Release(object? com) => NexaDock.Windows.Interop.ComRelease.Release(com);

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
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object anInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
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
