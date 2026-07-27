namespace ScanSystem.Shared;

/// <summary>وضعیت‌های ممکن برای یک درخواست اسکن.</summary>
public static class ScanStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Done = "Done";
    public const string Error = "Error";
}
