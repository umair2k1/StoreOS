namespace StoreOS.Application.Abstractions;

/// <summary>
/// Query that only reads tenant data: ReadCommitted, READ ONLY.
/// </summary>
public interface IReadOnlyRequest { }

/// <summary>
/// Heavy analytics/exports: runs on ReportingDbContext (P-119): RepeatableRead, READ ONLY (one consistent snapshot).
/// </summary>
public interface IReportingRequest { }

/// <summary>
/// Command that needs SERIALIZABLE isolation (checkout, estimate conversion).
/// </summary>
public interface ISerializableRequest { }
