namespace Shadowline.Data;

public sealed class DataFormatException(string message, Exception? inner = null) : Exception(message, inner);
