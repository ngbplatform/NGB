namespace NGB.Contracts.Common;

/// <summary>
/// A universal reference value for metadata-driven APIs.
/// 
/// Backend may return this shape for Guid reference fields so UI can display <see cref="Display"/>
/// while still sending only <see cref="Id"/> back to the server.
/// </summary>
public sealed record RefValueDto(Guid Id, string Display);
