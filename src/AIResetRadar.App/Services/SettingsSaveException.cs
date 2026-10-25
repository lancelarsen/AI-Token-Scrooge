namespace AIResetRadar.App.Services;

public sealed class SettingsSaveException(string message, Exception inner) : Exception(message, inner);
