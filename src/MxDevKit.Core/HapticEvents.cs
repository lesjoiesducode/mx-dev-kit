using System;
using System.Collections.Generic;

namespace MxDevKit;

// Haptic events raised by the plugin. The names must match package/events/DefaultEventSource.yaml
// and package/events/extra/eventMapping.yaml, where the default waveforms are repeated.
public static class HapticEvents
{
    public const string ActionSucceeded = "actionSucceeded";
    public const string ActionFailed = "actionFailed";
    public const string CiPassed = "ciPassed";
    public const string CiFailed = "ciFailed";
    public const string ReviewRequested = "reviewRequested";
    public const string Mentioned = "mentioned";
    public const string Assigned = "assigned";
    public const string PrApproved = "prApproved";
    public const string PrMerged = "prMerged";
    public const string ChangesRequested = "changesRequested";
    public const string PrCiFailed = "prCiFailed";
    public const string FocusEnded = "focusEnded";
    public const string BreakEnded = "breakEnded";

    public static readonly IReadOnlyDictionary<string, string> DefaultWaveforms =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionSucceeded] = "completed",
            [ActionFailed] = "angry_alert",
            [CiPassed] = "happy_alert",
            [CiFailed] = "angry_alert",
            [ReviewRequested] = "knock",
            [Mentioned] = "knock",
            [Assigned] = "knock",
            [PrApproved] = "happy_alert",
            [PrMerged] = "happy_alert",
            [ChangesRequested] = "angry_alert",
            [PrCiFailed] = "angry_alert",
            [FocusEnded] = "jingle",
            [BreakEnded] = "knock",
        };

    // The 15 waveforms of the MX Master 4, as listed in the Logi Actions SDK documentation.
    public static readonly IReadOnlySet<string> Waveforms = new HashSet<string>(StringComparer.Ordinal)
    {
        "sharp_state_change", "damp_state_change",
        "sharp_collision", "damp_collision", "subtle_collision",
        "happy_alert", "angry_alert", "completed",
        "square", "wave", "firework", "mad", "knock", "jingle", "ringing",
    };

    public static string WaveformFor(string eventName, IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides != null && overrides.TryGetValue(eventName, out var w) && Waveforms.Contains(w))
        {
            return w;
        }

        return DefaultWaveforms.TryGetValue(eventName, out var d) ? d : "completed";
    }

    // The GitHub inbox: "on t'attend" knocks, merged and approved are happy, changes and CI red are angry.
    public static string For(InboxKind kind) => kind switch
    {
        InboxKind.ReviewRequested => ReviewRequested,
        InboxKind.Mention or InboxKind.TeamMention => Mentioned,
        InboxKind.Assigned => Assigned,
        InboxKind.Approved => PrApproved,
        InboxKind.Merged => PrMerged,
        InboxKind.ChangesRequested => ChangesRequested,
        _ => PrCiFailed,
    };

    public static string DisplayName(string eventName, Strings s) => eventName switch
    {
        ActionSucceeded => s.EventActionSucceeded,
        ActionFailed => s.EventActionFailed,
        CiPassed => s.EventCiPassed,
        CiFailed => s.EventCiFailed,
        ReviewRequested => s.EventReviewRequested,
        Mentioned => s.EventMentioned,
        Assigned => s.EventAssigned,
        PrApproved => s.EventPrApproved,
        PrMerged => s.EventPrMerged,
        ChangesRequested => s.EventChangesRequested,
        PrCiFailed => s.EventPrCiFailed,
        FocusEnded => s.EventFocusEnded,
        BreakEnded => s.EventBreakEnded,
        _ => eventName,
    };
}
