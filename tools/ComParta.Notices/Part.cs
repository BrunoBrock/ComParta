namespace ComParta.Notices;

/// <summary>One package that ships with ComParta, as its own files describe it.</summary>
public sealed record Part(string Name, string Version, string? License, string? Copyright, string? ProjectUrl, PartSource Source);

public enum PartSource { NuGet, Npm }
