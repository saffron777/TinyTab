namespace StatisticEngine.Api.Contracts;

/// <summary>Ruta publicada por el servicio, tal como la ve el enrutador.</summary>
public sealed record EndpointDescriptor(string Method, string Path, string? Name, string? Summary);

/// <summary>Inventario de rutas vivas (se genera del propio enrutador, no de una lista fija).</summary>
public sealed record EndpointInventory(IReadOnlyList<EndpointDescriptor> Endpoints);
