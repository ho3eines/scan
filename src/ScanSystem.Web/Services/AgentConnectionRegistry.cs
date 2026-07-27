using System.Collections.Concurrent;

namespace ScanSystem.Web.Services;

/// <summary>
/// نگاشت در حافظه بین «نام ماشین Agent» و «ConnectionId فعلی» در SignalR.
/// ConnectionId در جدول Agents نگه‌داری نمی‌شود (طبق اسکیمای ضروری)؛
/// این Registry مختص Hub است و برای ارسال پیام هدفمند به Agent آنلاین استفاده می‌شود.
/// </summary>
public class AgentConnectionRegistry
{
    private readonly ConcurrentDictionary<string, string> _machineToConnection = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _connectionToMachine = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ثبت/به‌روزرسانی اتصال یک Agent. thread-safe.</summary>
    public void Register(string machineName, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(machineName) || string.IsNullOrWhiteSpace(connectionId)) return;

        // اگر قبلاً connection دیگری برای همین ماشین وجود داشت، آن را پاک می‌کنیم.
        if (_machineToConnection.TryGetValue(machineName, out var oldConn))
            _connectionToMachine.TryRemove(oldConn, out _);

        _machineToConnection[machineName] = connectionId;
        _connectionToMachine[connectionId] = machineName;
    }

    /// <summary>ConnectionId فعلی یک ماشین آنلاین (یا null اگر آفلاین است).</summary>
    public string? GetConnectionId(string machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return null;
        return _machineToConnection.TryGetValue(machineName, out var conn) ? conn : null;
    }

    /// <summary>نام ماشین متعلق به یک ConnectionId (یا null).</summary>
    public string? GetMachineName(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return null;
        return _connectionToMachine.TryGetValue(connectionId, out var machine) ? machine : null;
    }

    /// <summary>قطع اتصال: حذف هر دو جهت. نام ماشین قطع شده را برمی‌گرداند (برای SetOffline).</summary>
    public string? Unregister(string connectionId)
    {
        if (!_connectionToMachine.TryRemove(connectionId, out var machine)) return null;
        _machineToConnection.TryRemove(machine, out _);
        return machine;
    }

    /// <summary>آیا ماشین الان آنلاین است؟</summary>
    public bool IsOnline(string machineName)
        => !string.IsNullOrWhiteSpace(machineName) && _machineToConnection.ContainsKey(machineName);
}
