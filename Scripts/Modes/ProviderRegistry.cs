using System;
using System.Collections.Generic;
using System.Linq;

namespace EmuStack.Scripts.Modes;

public class ProviderRegistry
{
    private readonly List<Mode> _providers = new()
    {
        new ModeEden(),
        new ModeRyubing(),
        new ModeYuzu()
    };

    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Eden"] = "eden",
        ["Ryubing"] = "ryubing",
        ["Ryujinx"] = "ryubing",
        ["Yuzu"] = "yuzu"
    };

    public IReadOnlyList<Mode> Providers => _providers;

    public Mode DefaultProvider => Get("ryubing");

    public Mode Get(string providerId)
    {
        var normalizedId = NormalizeProviderId(providerId);
        return _providers.FirstOrDefault(provider => provider.Id == normalizedId) ?? DefaultProvider;
    }

    public string NormalizeProviderId(string providerId)
    {
        if (string.IsNullOrEmpty(providerId))
        {
            return "ryubing";
        }

        if (_aliases.TryGetValue(providerId, out var alias))
        {
            return alias;
        }

        return _providers.Any(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            ? providerId.ToLowerInvariant()
            : "ryubing";
    }
}
